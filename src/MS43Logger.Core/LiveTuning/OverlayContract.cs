namespace MS43Logger.Core.LiveTuning;

/// <summary>A live map's state in the ECU's overlay.</summary>
public enum OverlayState : byte
{
    /// <summary>Not in the ECU's copy area: the flashed map is read.</summary>
    Free,

    /// <summary>Copied and perhaps written since, not confirmed by an on.</summary>
    Loaded,

    /// <summary>Switched on before with a matching content CRC, and switched off since.</summary>
    Ready,

    /// <summary>Read instead of the flashed map.</summary>
    On,
}

/// <summary>Why the overlay refused a request (the second byte of <c>12 05 B0|B1 reason xor</c>).</summary>
public enum OverlayReason : byte
{
    Length = 1,
    Sub,
    Table,
    Range,
    Overlap,
    NotRunning,
    TableOn,
    NotLoaded,
    Integrity,
    Leaner,
    EditLimit,
    Session,
    Revision,
}

/// <summary>One map's state, place in the copy area, and CRC-32 of its last verified complete content.</summary>
public sealed record OverlayEntry(OverlayState State, int Offset, uint ContentCrc32, int DirectorySlot = -1);

/// <summary>The status reply's fields: the version, whether the engine runs, the richer-only rule, the five entries, the free bytes.</summary>
public sealed record OverlayStatus(int Version, bool Running, bool RicherOnly, IReadOnlyList<OverlayEntry> Tables, int FreeBytes,
    uint Session = 0, uint Revision = 0, LimiterSettings? Limiter = null);

/// <summary>A setting with no XDF entry carries its own definition: how the app shows, scales and limits it.</summary>
public sealed record OverlayScalarDefinition(string Title, string Description, string Units, string Equation,
    double RangeLow, double RangeHigh, int Decimals, string Category);

public sealed record OverlayScalar(string Key, string Label, int Offset, OverlayScalarDefinition? Definition = null);

public sealed record OverlayScalarGroup(string Title, IReadOnlyList<OverlayScalar> Fields, string? Note = null);

/// <summary>
/// A live map: its index in the overlay, its key, its group on the page, its address in the flash and its size. A reserved
/// entry (V10.3's group 139) keeps its place in the catalogue so the IDs after it hold; the app never shows or sends it.
/// </summary>
public sealed record OverlayMap(int Index, string Key, string Group, int Address, int Bytes, bool RicherOnly,
    OverlayScalarGroup? ScalarGroup = null, int ElementBits = 8, bool Reserved = false)
{
    /// <summary>The catalogue's flags: 2 for a settings group (a reserved one too), 4 for 16-bit cells.</summary>
    public int Flags => ScalarGroup is not null || Reserved ? 2 : ElementBits == 16 ? 4 : 0;
}

/// <summary>
/// The live-tuning overlay's DS2 command 0x6D, as the overlay bin implements it (MS43LAB plan 6 record, "The contract as
/// built"): the requests as payloads from the command byte on, and readers for the replies. Request arguments and status
/// fields are big-endian.
/// </summary>
public static class OverlayContract
{
    public const byte Command = 0x6D;
    public const int SupportedVersion = 2;
    public const int MaxCellDelta = 8;

    /// <summary>The v2 copy area at external RAM 041D30, readable with DS2 command 06.</summary>
    public const int CopyBytes = 688;

    public const int CopyRamAddress = 0x041D30;

    /// <summary>The overlay's 16-byte header in the flash, at 0C:B544.</summary>
    public const int HeaderAddress = 0x0CB544;

    /// <summary>The most data one write carries: its request is then 128 bytes, inside the firmware's 129.</summary>
    public const int MostWrite = 120;

    /// <summary>Without a 0x6D frame for this long the ECU switches everything off.</summary>
    public const int SilenceSeconds = 3;

    private const byte EcuAddress = 0x12;

    private enum Sub : byte { Status, Load, Write, On, Off, AllOn, AllOff, Free, Revert }

    /// <summary>The five live maps, in the overlay's order.</summary>
    public static IReadOnlyList<OverlayMap> Maps { get; } = new[]
    {
        new OverlayMap(0, "ign98_pl", "Timing", 0x77315, 320, false),
        new OverlayMap(1, "ign98_is", "Timing", 0x775D4, 36, false),
        new OverlayMap(2, "af98", "Fuel", 0x7D69C, 192, true),
        new OverlayMap(3, "cam_in", "VANOS", 0x76390, 192, false),
        new OverlayMap(4, "cam_ex", "VANOS", 0x7629C, 192, false),
    };

    /// <summary>The frame on the line: 12h, the length, the payload, the xor of all of them.</summary>
    public static byte[] Frame(IReadOnlyList<byte> payload)
    {
        var frame = new byte[payload.Count + 3];
        frame[0] = EcuAddress;
        frame[1] = (byte)frame.Length;
        for (var i = 0; i < payload.Count; i++)
        {
            frame[i + 2] = payload[i];
        }

        byte xor = 0;
        for (var i = 0; i < frame.Length - 1; i++)
        {
            xor ^= frame[i];
        }

        frame[^1] = xor;
        return frame;
    }

    public static byte[] Status() => Payload(Sub.Status);

    /// <summary>Places <paramref name="table"/> at <paramref name="offset"/> in the copy area, as a copy of the flashed map.</summary>
    public static byte[] Load(int table, int offset) => Payload(Sub.Load, (byte)table, (byte)(offset >> 8), (byte)offset);

    /// <summary>Writes 1–120 bytes into <paramref name="table"/>'s copy from its byte <paramref name="at"/>.</summary>
    public static byte[] Write(int table, int at, ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > MostWrite)
        {
            throw new ArgumentException($"a write carries 1 to {MostWrite} bytes, not {data.Length}", nameof(data));
        }

        return Payload(Sub.Write, new[] { (byte)table, (byte)(at >> 8), (byte)at }.Concat(data.ToArray()).ToArray());
    }

    /// <summary>Switches a table on only if the complete RAM content matches the supplied CRC-32.</summary>
    public static byte[] On(int table, uint crc) => Payload(Sub.On, (byte)table,
        (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc);

    public static byte[] Off(int table) => Payload(Sub.Off, (byte)table);

    public static byte[] AllOn() => Payload(Sub.AllOn);

    public static byte[] AllOff() => Payload(Sub.AllOff);

    public static byte[] Free(int table) => Payload(Sub.Free, (byte)table);

    public static byte[] Revert() => Payload(Sub.Revert);

    /// <summary>CRC-32/ISO-HDLC of the complete table, in byte order (check value CBF43926 for ASCII 123456789).</summary>
    public static uint Crc32(ReadOnlySpan<byte> copy)
    {
        var crc = uint.MaxValue;
        foreach (var value in copy)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
        }

        return ~crc;
    }

    /// <summary>The writes that turn a copy of <paramref name="flash"/> into <paramref name="copy"/>: one per run of changed bytes, long runs split.</summary>
    public static IReadOnlyList<byte[]> Writes(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        RequireSameSize(copy, flash);
        var writes = new List<byte[]>();
        for (var i = 0; i < copy.Length;)
        {
            if (copy[i] == flash[i])
            {
                i++;
                continue;
            }

            var end = i;
            while (end < copy.Length && copy[end] != flash[end] && end - i < MostWrite)
            {
                end++;
            }

            writes.Add(Write(table, i, copy[i..end]));
            i = end;
        }

        return writes;
    }

    /// <summary>The cells of a richer-only map below their flashed value (λ = 1.5 − X/256: lower is leaner); none for the other maps.</summary>
    public static IReadOnlyList<int> LeanerCells(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        RequireSameSize(copy, flash);
        if (!Maps[table].RicherOnly)
        {
            return Array.Empty<int>();
        }

        var leaner = new List<int>();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] < flash[i])
            {
                leaner.Add(i);
            }
        }

        return leaner;
    }

    /// <summary>Cells outside the immutable v2 limit relative to flash: +/-8 raw for ignition/cams, 0..8 for AFR.</summary>
    public static IReadOnlyList<int> EditLimitCells(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        RequireSameSize(copy, flash);
        var outside = new List<int>();
        var minimum = Maps[table].RicherOnly ? 0 : -MaxCellDelta;
        for (var i = 0; i < copy.Length; i++)
        {
            var delta = (int)copy[i] - flash[i];
            if (delta < minimum || delta > MaxCellDelta)
            {
                outside.Add(i);
            }
        }

        return outside;
    }

    /// <summary>The fields of a whole v2 status reply, <c>12 34 A0 …</c>.</summary>
    public static OverlayStatus ParseStatus(IReadOnlyList<byte> reply)
    {
        if (reply.Count > 7 && reply[7] == OverlayV4.Version) return OverlayV4.ParseStatus(reply);
        if (reply.Count > 7 && reply[7] == OverlayV3.Version) return OverlayV3.ParseStatus(reply);
        ValidateFrame(reply);
        if (reply.Count != 52 || reply[2] != 0xA0
            || reply[3] != 'L' || reply[4] != 'T' || reply[5] != 'O' || reply[6] != 'V')
        {
            throw new InvalidDataException("not the overlay's status reply: " + Convert.ToHexString(reply.ToArray()));
        }

        var p = reply.Skip(3).ToArray();
        if (p[4] != SupportedVersion)
        {
            throw new InvalidDataException($"Unsupported live-tuning overlay version {p[4]}; version {SupportedVersion} is required.");
        }

        int Word(int at) => p[at] << 8 | p[at + 1];
        uint DWord(int at) => (uint)p[at] << 24 | (uint)p[at + 1] << 16 | (uint)p[at + 2] << 8 | p[at + 3];
        var tables = Enumerable.Range(0, Maps.Count)
            .Select(i => new OverlayEntry((OverlayState)p[6 + 8 * i], Word(8 + 8 * i), DWord(10 + 8 * i)))
            .ToArray();
        for (var i = 0; i < tables.Length; i++)
        {
            var entry = tables[i];
            if ((byte)entry.State > (byte)OverlayState.On || p[7 + 8 * i] != 0
                || (entry.State != OverlayState.Free && entry.Offset + Maps[i].Bytes > CopyBytes))
            {
                throw new InvalidDataException("Invalid overlay table directory.");
            }

            for (var j = 0; j < i; j++)
            {
                if (entry.State != OverlayState.Free && tables[j].State != OverlayState.Free
                    && entry.Offset < tables[j].Offset + Maps[j].Bytes && tables[j].Offset < entry.Offset + Maps[i].Bytes)
                {
                    throw new InvalidDataException("Overlapping overlay table directory.");
                }
            }
        }

        var free = CopyBytes - tables.Select((entry, i) => entry.State == OverlayState.Free ? 0 : Maps[i].Bytes).Sum();
        if (Word(46) != free)
        {
            throw new InvalidDataException("Invalid overlay free-byte count.");
        }

        return new OverlayStatus(p[4], (p[5] & 1) != 0, (p[5] & 2) != 0, tables, free);
    }

    /// <summary>The reason of a B0/B1 refusal; null for an A0 reply. Anything else is not the overlay's reply.</summary>
    public static OverlayReason? Refusal(IReadOnlyList<byte> reply)
    {
        ValidateFrame(reply);

        return reply[2] switch
        {
            0xA0 when reply.Count == 4 => null,
            0xB0 or 0xB1 when reply.Count == 5 && Enum.IsDefined((OverlayReason)reply[3]) => (OverlayReason)reply[3],
            _ => throw new InvalidDataException("not an overlay reply: " + Convert.ToHexString(reply.ToArray())),
        };
    }

    public static void ValidateFrame(IReadOnlyList<byte> reply)
    {
        if (reply.Count < 4 || reply[0] != EcuAddress || reply[1] != reply.Count
            || reply.Aggregate((byte)0, (xor, value) => (byte)(xor ^ value)) != 0)
        {
            throw new InvalidDataException("Invalid DS2 reply length, address or checksum.");
        }
    }

    private static void RequireSameSize(ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (copy.Length != flash.Length)
        {
            throw new ArgumentException("the copy and the flashed map differ in size");
        }
    }

    private static byte[] Payload(Sub sub, params byte[] arguments) => new[] { Command, (byte)sub }.Concat(arguments).ToArray();
}
