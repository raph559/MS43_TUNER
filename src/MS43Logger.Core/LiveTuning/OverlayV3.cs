namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for session framing introduced in V3.</summary>
public static class OverlayV3
{
    public const int PatchBytes = OverlayProtocol.PatchBytes;
    public const int MostWrite = OverlayProtocol.MostWrite;
    public const int MinimumPatchGapMilliseconds = OverlayProtocol.MinimumPatchGapMilliseconds;

    public static byte[] Open(uint session) => OverlayProtocol.Open(session);
    public static byte[] Mutation(byte[] command, uint session, uint revision) => OverlayProtocol.Mutation(command, session, revision);
    public static IEnumerable<int> ChangedWindows(byte[] before, byte[] after) => OverlayProtocol.ChangedWindows(before, after);
    public static (OverlayReason? Reason, uint Revision) Response(byte[] response) => OverlayProtocol.Response(response);
}
