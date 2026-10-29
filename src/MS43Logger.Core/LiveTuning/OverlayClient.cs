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
    private OverlaySessionClient? _client;
    private readonly IReadOnlyList<OverlayMap> _maps = OverlayCatalogue.Maps;

    /// <summary>Reads the header, verified catalogue and flashed maps. Sends no 0x6D.</summary>
    public OverlayDetection Detect()
    {
        _flash = null;
        _client = null;
        var headerBytes = _line.Read(OverlayContract.HeaderAddress, 16);
        var header = OverlayHeader.Parse(headerBytes);
        if (header is null)
        {
            return new OverlayDetection(null, Array.Empty<byte[]>(), "not found");
        }

        if (!OverlayProtocol.IsCurrent(header))
        {
            return new OverlayDetection(header, Array.Empty<byte[]>(), Outdated(header));
        }

        if (headerBytes[14] != 0xD0 + header.Version || headerBytes[15] != 0xC0)
            return new OverlayDetection(header, Array.Empty<byte[]>(), "not understood");

        var list = header.CodeStart >= 0xB554 && header.CodeEnd > header.CodeStart
            && header.DataEnd <= 0x10000 && header.DataEnd - header.CodeEnd >= _maps.Count * 8
            && header.TableCount == _maps.Count
            ? OverlayHeader.ParseTableList(_line.Read(0x0C0000 + header.CodeEnd, _maps.Count * 8))
            : Array.Empty<(int, int, int)>();
        if (!list.Select(t => (t.Address, t.Bytes, t.Flags)).SequenceEqual(_maps.Select(m => (m.Address, m.Bytes, m.Flags))))
        {
            return new OverlayDetection(header, Array.Empty<byte[]>(), "not understood");
        }

        _flash = _maps.Select(m => _line.Read(m.Address, m.Bytes)).ToArray();
        _client = new OverlaySessionClient(_line);
        var limiter = LimiterSettings.Read([.. _line.Read(0x75D47, 6), _line.Read(0x70BFF, 1)[0], _line.Read(0x7131E, 1)[0]]);
        return new OverlayDetection(header, _flash.Select(bytes => (byte[])bytes.Clone()).ToArray(), null, _maps, limiter);
    }

    /// <summary>The refusal for an overlay of another version than V10.4: the app sends it nothing.</summary>
    public static string Outdated(OverlayHeader header) =>
        $"The ECU runs live-tuning overlay V{header.Label}; this app works with V{OverlayProtocol.Label} only. "
        + $"Flash the V{OverlayProtocol.Label} bin to tune live. No live commands will be sent.";

    public OverlayStatus Status() => Client().Status();

    /// <summary>
    /// Puts <paramref name="copy"/> live: an active map is patched in checked eight-cell groups; a free one is loaded,
    /// written, read back and switched on with its CRC.
    /// </summary>
    public SendResult Send(int table, byte[] copy, byte[] flash, OverlayStatus current)
    {
        var client = Client();
        if (table < 0 || table >= _maps.Count || copy.Length != _maps[table].Bytes
            || !_flash![table].AsSpan().SequenceEqual(flash) || current.Version != OverlayProtocol.Version)
        {
            throw new ArgumentException("The table, flashed baseline, size or overlay version is invalid.");
        }

        if (OverlaySettings.ValidateCopy(table, copy, flash) is { } invalid)
            return new SendResult(invalid, null);

        return client.Send(table, (byte[])copy.Clone(), (byte[])flash.Clone(), current);
    }

    public OverlayReason? On(int table, uint crc) => Run(OverlayContract.On(table, crc));

    public OverlayReason? Off(int table) => Run(OverlayContract.Off(table));

    public OverlayReason? AllOn() => Run(OverlayContract.AllOn());

    public OverlayReason? AllOff() => Run(OverlayContract.AllOff());

    public OverlayReason? Free(int table) => Run(OverlayContract.Free(table));

    public OverlayReason? Revert() => Run(OverlayContract.Revert());

    /// <summary>The bytes of a map's copy in the ECU, read back with DS2 command 06 (a read: no detection needed).</summary>
    public byte[] ReadCopy(int table, OverlayEntry entry)
    {
        if (table < 0 || table >= _maps.Count || entry.State == OverlayState.Free || !OverlayMemory.Fits(entry.Offset, _maps[table].Bytes))
            throw new InvalidDataException("Invalid map allocation.");
        return _line.Read(OverlayMemory.Physical(entry.Offset), _maps[table].Bytes);
    }

    private OverlayReason? Run(byte[] payload) => Client().Run(payload);

    public OverlayReason? SendLimiter(LimiterSettings settings) => Client().SendLimiter(settings);

    private OverlaySessionClient Client() => _client
        ?? throw new InvalidOperationException($"Overlay V{OverlayProtocol.Label} must be detected before sending live commands.");
}
