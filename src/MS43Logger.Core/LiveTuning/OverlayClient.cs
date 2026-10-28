namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// What detection found: the header, the supported maps' bytes as the ECU's flash holds them, and the problem when the
/// overlay is not there ("not found"), is another version than V10.4, or is not the one this app knows ("not understood").
/// </summary>
public sealed record OverlayDetection(OverlayHeader? Header, IReadOnlyList<byte[]> Flash, string? Problem,
    IReadOnlyList<OverlayMap>? Catalogue = null, LimiterSettings? LimiterFlash = null);

/// <summary>A send's outcome: null and null when the map is live; else the refusal, or the map to free first when it does not fit.</summary>
public sealed record SendResult(OverlayReason? Refused, int? FreeFirst);

/// <summary>
/// Talks to the live-tuning overlay V10.4 over a DS2 line. Detection reads only (DS2 command 06); every other call sends
/// 0x6D, so it is for an ECU whose detection found the overlay. Any other version is refused at detection.
/// </summary>
public sealed class OverlayClient(IDs2Line line)
{
    private readonly IDs2Line _line = line ?? throw new ArgumentNullException(nameof(line));
    private IReadOnlyList<byte[]>? _flash;
    private OverlayV3Client? _v3;
    private OverlayV4Client? _v4;
    private int _version;
    private int _minor;
    private OverlaySessionClient? _client;
    private IReadOnlyList<OverlayMap> _maps = OverlayContract.Maps;

    /// <summary>Reads the header, verified catalogue and flashed maps. Sends no 0x6D.</summary>
    public OverlayDetection Detect()
    {
        _flash = null;
        _v3 = null;
        _v4 = null;
        _version = 0;
        _maps = OverlayContract.Maps;
        var headerBytes = _line.Read(OverlayContract.HeaderAddress, 16);
        var header = OverlayHeader.Parse(headerBytes);
        if (header is null)
        {
            return new OverlayDetection(null, Array.Empty<byte[]>(), "not found");
        }

        if (header.Version is not (2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10))
        {
            return new OverlayDetection(header, Array.Empty<byte[]>(),
                $"Unsupported live-tuning overlay version {header.Version}; version 2, 3, 4, 5, 6, 7, 8, 9 or 10 is required. No live commands will be sent.");
        }

        if (header.Version is 6 or 7 or 8 or 9 or 10 && (headerBytes[14] != 0xD0 + header.Version || headerBytes[15] != 0xC0))
            return new OverlayDetection(header, Array.Empty<byte[]>(), "not understood");

        _maps = header.Version is 8 or 9 or 10 ? OverlayV9.MapsFor(header.Version, header.Minor) : header.Version == 7 ? OverlayV7.Maps : header.Version == 6 ? OverlayV6.Maps : header.Version is 4 or 5 ? OverlayV4.Maps : OverlayContract.Maps;
        var list = header.CodeStart >= 0xB554 && header.CodeEnd > header.CodeStart
            && header.DataEnd <= 0x10000 && header.DataEnd - header.CodeEnd >= _maps.Count * 8
            && header.TableCount == _maps.Count
            ? OverlayHeader.ParseTableList(_line.Read(0x0C0000 + header.CodeEnd, _maps.Count * 8))
            : Array.Empty<(int, int, int)>();
        if (!list.Select(t => (t.Address, t.Bytes, t.Flags)).SequenceEqual(_maps.Select(m => (m.Address, m.Bytes, header.Version == 2 && m.RicherOnly ? 1 : m.Flags))))
        {
            return new OverlayDetection(header, Array.Empty<byte[]>(), "not understood");
        }

        _flash = _maps.Select(m => _line.Read(m.Address, m.Bytes)).ToArray();
        _version = header.Version;
        _minor = header.Minor;
        if (header.Version == OverlayV3.Version) _v3 = new OverlayV3Client(_line);
        LimiterSettings? limiter = null;
        if (header.Version is 4 or 5 or 6 or 7 or 8 or 9 or 10)
        {
            _v4 = new OverlayV4Client(_line, header.Version);
            limiter = LimiterSettings.Read([.. _line.Read(0x75D47, 6), _line.Read(0x70BFF, 1)[0], _line.Read(0x7131E, 1)[0]]);
        }
        return new OverlayDetection(header, _flash.Select(bytes => (byte[])bytes.Clone()).ToArray(), null, _maps, limiter);
    }

    /// <summary>The refusal for an overlay of another version than V10.4: the app sends it nothing.</summary>
    public static string Outdated(OverlayHeader header) =>
        $"The ECU runs live-tuning overlay V{header.Label}; this app works with V{OverlayProtocol.Label} only. "
        + $"Flash the V{OverlayProtocol.Label} bin to tune live. No live commands will be sent.";

    public OverlayStatus Status()
    {
        RequireSupported();
        return _v4?.Status() ?? _v3?.Status() ?? OverlayContract.ParseStatus(_line.Request(OverlayContract.Status()));
    }

    /// <summary>
    /// Puts <paramref name="copy"/> live. Protocol 3 patches an active copy in checked eight-cell groups. Protocol 2
    /// switches it off, loads and writes the copy, verifies readback and activates it with its CRC, retrying a mismatch
    /// once. Protocol 2's bounded-edit policy remains in force only for that version.
    /// </summary>
    public SendResult Send(int table, byte[] copy, byte[] flash, OverlayStatus current)
    {
        RequireSupported();
        if (table < 0 || table >= _maps.Count || copy.Length != _maps[table].Bytes
            || !_flash![table].AsSpan().SequenceEqual(flash) || current.Version != _version)
        {
            throw new ArgumentException("The table, flashed baseline, size or overlay version is invalid.");
        }

        if (_version is 6 or 7 or 8 or 9 or 10 && OverlayV9.ValidateFor(_version, table, copy, flash, _minor) is { } invalid)
            return new SendResult(invalid, null);

        copy = (byte[])copy.Clone();
        flash = (byte[])flash.Clone();
        if (_v4 is not null) return _v4.Send(table, copy, flash, current);
        if (_v3 is not null) return _v3.Send(table, copy, flash, current);
        if (OverlayContract.LeanerCells(table, copy, flash).Count > 0)
        {
            return new SendResult(OverlayReason.Leaner, null);
        }

        if (OverlayContract.EditLimitCells(table, copy, flash).Count > 0)
        {
            return new SendResult(OverlayReason.EditLimit, null);
        }

        if (current.Tables[table].State == OverlayState.On && Off(table) is { } off)
        {
            return new SendResult(off, null);
        }

        if (CopyAreaPlanner.Place(table, current) is not { } offset)
        {
            return new SendResult(null, CopyAreaPlanner.FreeFirst(table, current));
        }

        var crc = OverlayContract.Crc32(copy);
        for (var attempt = 0; ; attempt++)
        {
            if (Run(OverlayContract.Load(table, offset)) is { } load)
            {
                return new SendResult(load, null);
            }

            foreach (var write in OverlayContract.Writes(table, copy, flash))
            {
                if (Run(write) is { } refused)
                {
                    return new SendResult(refused, null);
                }
            }

            if (!ReadCopy(table, new OverlayEntry(OverlayState.Loaded, offset, 0)).AsSpan().SequenceEqual(copy))
            {
                if (attempt == 0)
                {
                    continue;
                }

                return new SendResult(OverlayReason.Integrity, null);
            }

            var on = On(table, crc);
            if (on == OverlayReason.Integrity && attempt == 0)
            {
                continue;
            }

            return new SendResult(on, null);
        }
    }

    public OverlayReason? On(int table, uint crc) => Run(OverlayContract.On(table, crc));

    public OverlayReason? Off(int table) => Run(OverlayContract.Off(table));

    public OverlayReason? AllOn() => Run(OverlayContract.AllOn());

    public OverlayReason? AllOff() => Run(OverlayContract.AllOff());

    public OverlayReason? Free(int table) => Run(OverlayContract.Free(table));

    public OverlayReason? Revert() => Run(OverlayContract.Revert());

    /// <summary>The bytes of a map's copy in the ECU, read back with DS2 command 06.</summary>
    public byte[] ReadCopy(int table, OverlayEntry entry)
    {
        if (_v4 is not null)
        {
            if (table < 0 || table >= _maps.Count || entry.State == OverlayState.Free
                || !(_version is 5 or 6 or 7 or 8 or 9 or 10 ? OverlayV5.Fits(entry.Offset, _maps[table].Bytes, _version) : OverlayV4.Fits(entry.Offset, _maps[table].Bytes)))
                throw new InvalidDataException("Invalid map allocation.");
            return _line.Read(OverlayV4.Physical(entry.Offset), _maps[table].Bytes);
        }
        if (_v3 is not null)
        {
            if (table is < 0 or > 4 || entry.State == OverlayState.Free || entry.Offset != OverlayV3.Offset(table))
                throw new InvalidDataException("Invalid fixed map address.");
            return _line.Read(OverlayV3.Addresses[table], OverlayContract.Maps[table].Bytes);
        }
        if (table < 0 || table >= OverlayContract.Maps.Count || entry.State == OverlayState.Free || entry.Offset < 0
            || entry.Offset + OverlayContract.Maps[table].Bytes > OverlayContract.CopyBytes)
        {
            throw new InvalidDataException("Invalid overlay copy address.");
        }

        return _line.Read(OverlayContract.CopyRamAddress + entry.Offset, OverlayContract.Maps[table].Bytes);
    }

    private OverlayReason? Run(byte[] payload)
    {
        RequireSupported();
        return _v4 is not null ? _v4.Run(payload) : _v3 is not null ? _v3.Run(payload) : OverlayContract.Refusal(_line.Request(payload));
    }

    public OverlayReason? SendLimiter(LimiterSettings settings)
    {
        RequireSupported();
        return (_v4 ?? throw new InvalidOperationException("Limiter editing requires overlay 4, 5 or 6.")).SendLimiter(settings);
    }

    private void RequireSupported()
    {
        if (_flash is null)
        {
            throw new InvalidOperationException($"A matching version {OverlayContract.SupportedVersion} overlay must be detected before sending live commands.");
        }
    }

    private OverlaySessionClient Client() => _client
        ?? throw new InvalidOperationException($"Overlay V{OverlayProtocol.Label} must be detected before sending live commands.");
}
