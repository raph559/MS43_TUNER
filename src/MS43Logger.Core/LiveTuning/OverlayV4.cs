namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for the original catalogue, storage geometry and patch encoding.</summary>
public static class OverlayV4
{
    public const int CopyBytes = OverlayMemory.CopyBytes, LimiterAddress = OverlayMemory.LimiterAddress;
    public static IReadOnlyList<OverlayMap> Maps => OverlayCatalogue.BaseMaps;
    public static int Physical(int offset) => OverlayMemory.Physical(offset);
    public static bool Fits(int offset, int bytes) => OverlayMemory.FitsStorage(offset, bytes);
    public static byte[] Patch(int table, int at, uint session, uint revision, ReadOnlySpan<byte> eightBytes, IReadOnlyList<OverlayMap>? catalogue = null) =>
        OverlayProtocol.Patch(table, at, session, revision, eightBytes, catalogue);
}
