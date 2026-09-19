using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

public sealed record OverlayPlacement(int Offset, int DirectorySlot);

public static class OverlayV5
{
    public const int Version = 5, BaseSlots = 5, RecordBytes = 8, PoolBStart = 704, PoolBBytes = 228;
    public const int CopyBytes = PoolBStart + PoolBBytes;
    public const int PoolBAddress = 0xE6E0;
    public const int IndexBytes = 16, IndexOffset = CopyBytes - IndexBytes;
    public static int LookupBytes(int version) => version >= OverlayV7.Version ? OverlayV7.IndexBytes : IndexBytes;
    public static int LookupOffset(int version) => CopyBytes - LookupBytes(version);
    public static IReadOnlyList<OverlayMap> Maps => OverlayV4.Maps;

    public static int? RecordOffset(OverlayEntry entry) => entry.DirectorySlot >= BaseSlots
        ? PoolBStart + 4 * (entry.DirectorySlot - BaseSlots) : null;

    public static int TrackingBytes(OverlayStatus status)
    {
        var records = status.Tables.Count(e => e.State != OverlayState.Free && e.DirectorySlot >= BaseSlots);
        return records == 0 ? 0 : records * RecordBytes + LookupBytes(status.Version);
    }

    public static byte[] Load(int table, OverlayPlacement placement) =>
        [.. OverlayContract.Load(table, placement.Offset),
            placement.DirectorySlot < BaseSlots ? (byte)255 : checked((byte)(placement.DirectorySlot - BaseSlots))];

    private static void ValidateSummary(IReadOnlyList<byte> reply, int version)
    {
        OverlayContract.ValidateFrame(reply);
        if (reply.Count != 68 || reply[2] != 0xA0 || !reply.Skip(3).Take(5).SequenceEqual(new byte[] { 76, 84, 79, 86, checked((byte)version) })
            || (reply[8] & ~1) != 0) throw new InvalidDataException($"Invalid protocol {version} status.");
    }

    private static int? DataOffset(int table, IReadOnlyList<OverlayMap> maps, IEnumerable<(int Start, int Bytes)> gaps, bool originalPools)
    {
        var preferB = originalPools && table is 1 or 3;
        return gaps.Where(g => g.Bytes >= maps[table].Bytes)
            .OrderBy(g => (g.Start >= PoolBStart) == preferB ? 0 : 1).ThenBy(g => g.Bytes).ThenBy(g => g.Start)
            .Select(g => (int?)g.Start).FirstOrDefault();
    }
}
