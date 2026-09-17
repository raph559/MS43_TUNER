using System.Buffers.Binary;
using System.Text.Json;

namespace MS43Logger.Core.LiveTuning;

public sealed record LimiterSettings(byte[] GearLimits, byte Hysteresis, bool IgnitionCut, bool Enabled,
    bool Confirmed = false, bool AppliedIgnitionCut = false)
{
    public byte[] Payload()
    {
        if (GearLimits.Length != 6 || GearLimits.Any(limit => limit <= Hysteresis))
            throw new ArgumentException("Each limiter threshold must be greater than its hysteresis.");
        return [.. GearLimits, Hysteresis, (byte)((IgnitionCut ? 1 : 0) | (Enabled ? 0x80 : 0))];
    }

    public static LimiterSettings Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8 || (bytes[7] & ~0xE1) != 0) throw new InvalidDataException("Invalid limiter state.");
        return new(bytes[..6].ToArray(), bytes[6], (bytes[7] & 1) != 0, (bytes[7] & 0x80) != 0,
            (bytes[7] & 0x20) != 0, (bytes[7] & 0x40) != 0);
    }
}

public static class OverlayV4
{
    public const int Version = 4, CopyBytes = 932, MaxSlots = 5, LimiterAddress = 0x41FD8;
    public static IReadOnlyList<OverlayMap> Maps { get; } = LoadCatalogue();
    public static int Physical(int offset) => offset is >= 0 and < 704 ? 0x41D00 + offset
        : offset is >= 704 and < CopyBytes ? 0xE6E0 + offset - 704 : throw new ArgumentOutOfRangeException(nameof(offset));

    public static bool Fits(int offset, int bytes) => offset >= 0 && (offset & 1) == 0 && bytes >= 8 && (bytes & 1) == 0
        && (offset < 704 ? offset + bytes <= 704 : offset + bytes <= CopyBytes);

    public static byte[] Patch(int table, int at, uint session, uint revision, ReadOnlySpan<byte> eightBytes, IReadOnlyList<OverlayMap>? catalogue = null)
    {
        var maps = catalogue ?? Maps;
        if (session == 0 || revision == uint.MaxValue || eightBytes.Length != 8
            || (table != 255 && (table < 0 || table >= maps.Count || at < 0 || (at & 1) != 0 || at + 8 > maps[table].Bytes))
            || (table == 255 && (at != 0 || (eightBytes[7] & ~0x81) != 0)))
            throw new ArgumentException("Invalid protocol 4 patch.");
        var p = new byte[26]; p[0] = 0x6D; p[1] = 9;
        Put(p, 2, session); Put(p, 6, revision); p[10] = (byte)table;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(12), (ushort)at);
        eightBytes.CopyTo(p.AsSpan(14)); Put(p, 22, OverlayContract.Crc32(p.AsSpan(2, 20))); return p;
    }

    private static IReadOnlyList<OverlayMap> LoadCatalogue()
    {
        var assembly = typeof(OverlayV4).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("live_overlay_v4_catalog.json")))!;
        using var doc = JsonDocument.Parse(stream);
        return Array.AsReadOnly(doc.RootElement.EnumerateArray().Select(e =>
        {
            var id = e.GetProperty("id").GetInt32();
            if (id < 5) return OverlayContract.Maps[id] with { RicherOnly = false };
            var name = e.GetProperty("name").GetString()!;
            var group = name.Contains("cam_") ? "VANOS" : name.Contains("iga_") ? "Timing" : "Other maps";
            return new OverlayMap(id, name, group, e.GetProperty("address").GetInt32(), e.GetProperty("bytes").GetInt32(), false);
        }).ToArray());
    }

    private static uint Get(byte[] p, int at) => BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(at));
    private static void Put(byte[] p, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(at), value);
}
