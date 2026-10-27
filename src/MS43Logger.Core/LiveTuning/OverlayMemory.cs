using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

public static class OverlayMemory
{
    public const int BaseSlots = 5, RecordBytes = 8, PoolBStart = 704, PoolBBytes = 228;
    public const int CopyBytes = PoolBStart + PoolBBytes;
    public const int PoolBAddress = 0xE6E0;

    /// <summary>The live-map lookup index at the end of pool B.</summary>
    public const int LookupBytes = 20, LookupOffset = CopyBytes - LookupBytes;

    /// <summary>Where pool A ends for allocations: the overlay's own state (pops, the watch record, the blip) takes the rest.</summary>
    public const int PoolAEnd = 668;

    /// <summary>The bytes the live maps can use: the status reports 896.</summary>
    public const int Budget = 896;

    public const int LimiterAddress = 0x41FD8;

    public static int Physical(int offset) => offset is >= 0 and < 704 ? 0x41D00 + offset
        : offset is >= 704 and < CopyBytes ? 0xE6E0 + offset - 704 : throw new ArgumentOutOfRangeException(nameof(offset));

    /// <summary>Raw storage geometry with two-byte alignment; live allocations use <see cref="Fits"/>.</summary>
    public static bool FitsStorage(int offset, int bytes) => offset >= 0 && (offset & 1) == 0 && bytes >= 8 && (bytes & 1) == 0
        && (offset < 704 ? offset + bytes <= 704 : offset + bytes <= CopyBytes);

    public static bool Fits(int offset, int bytes) => (offset & 3) == 0 && (bytes & 3) == 0
        && FitsStorage(offset, bytes) && (offset >= PoolBStart || offset + bytes <= PoolAEnd);

    public static int? RecordOffset(OverlayEntry entry) => entry.DirectorySlot >= BaseSlots
        ? PoolBStart + 4 * (entry.DirectorySlot - BaseSlots) : null;

    public static int TrackingBytes(OverlayStatus status)
    {
        var records = status.Tables.Count(e => e.State != OverlayState.Free && e.DirectorySlot >= BaseSlots);
        return records == 0 ? 0 : records * RecordBytes + LookupBytes;
    }

    public static byte[] Load(int table, OverlayPlacement placement) =>
        [.. OverlayContract.Load(table, placement.Offset),
            placement.DirectorySlot < BaseSlots ? (byte)255 : checked((byte)(placement.DirectorySlot - BaseSlots))];

    private static void ValidateSummary(IReadOnlyList<byte> reply)
    {
        OverlayContract.ValidateFrame(reply);
        if (reply.Count != 68 || reply[2] != 0xA0 || !reply.Skip(3).Take(5).SequenceEqual(new byte[] { 76, 84, 79, 86, OverlayProtocol.Version })
            || (reply[8] & ~1) != 0) throw new InvalidDataException("Invalid overlay status.");
    }

    private static int? DataOffset(int table, IReadOnlyList<OverlayMap> maps, IEnumerable<(int Start, int Bytes)> gaps, bool originalPools)
    {
        var preferB = originalPools && table is 1 or 3;
        return gaps.Where(g => g.Bytes >= maps[table].Bytes)
            .OrderBy(g => (g.Start >= PoolBStart) == preferB ? 0 : 1).ThenBy(g => g.Bytes).ThenBy(g => g.Start)
            .Select(g => (int?)g.Start).FirstOrDefault();
    }
}
