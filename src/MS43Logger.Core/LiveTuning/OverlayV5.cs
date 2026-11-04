namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for allocation and directory status.</summary>
public static class OverlayV5
{
    public const int BaseSlots = OverlayMemory.BaseSlots, RecordBytes = OverlayMemory.RecordBytes;
    public const int PoolBStart = OverlayMemory.PoolBStart, PoolBBytes = OverlayMemory.PoolBBytes, CopyBytes = OverlayMemory.CopyBytes;
    public const int PoolBAddress = OverlayMemory.PoolBAddress, LookupBytes = OverlayMemory.LookupBytes, LookupOffset = OverlayMemory.LookupOffset;
    public const int PoolAEnd = OverlayMemory.PoolAEnd, Budget = OverlayMemory.Budget;

    public static IReadOnlyList<OverlayMap> Catalogue => OverlayCatalogue.Maps;
    public static bool Fits(int offset, int bytes) => OverlayMemory.Fits(offset, bytes);
    public static int? RecordOffset(OverlayEntry entry) => OverlayMemory.RecordOffset(entry);
    public static int TrackingBytes(OverlayStatus status) => OverlayMemory.TrackingBytes(status);
    public static byte[] Load(int table, OverlayPlacement placement) => OverlayMemory.Load(table, placement);
    public static OverlayStatus ReadStatus(IDs2Line line) => OverlayMemory.ReadStatus(line);
    public static OverlayStatus ParseStatus(IReadOnlyList<byte> reply, ReadOnlySpan<byte> poolB) => OverlayMemory.ParseStatus(reply, poolB);
    public static OverlayPlacement? Place(int table, OverlayStatus status) => OverlayMemory.Place(table, status);
    public static int LargestBlock(OverlayStatus status) => OverlayMemory.LargestBlock(status);
    public static int? FreeFirst(int table, OverlayStatus status) => OverlayMemory.FreeFirst(table, status);
}
