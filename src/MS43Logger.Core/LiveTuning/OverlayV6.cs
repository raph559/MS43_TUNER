namespace MS43Logger.Core.LiveTuning;

/// <summary>The original stock launch-group catalogue and activation limits.</summary>
public static class OverlayV6
{
    public const int Version = 6, LaunchControl = 124, NoLiftShift = 125, RollingAntiLag = 126;
    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayCatalogue.Maps.Take(127).ToArray());
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash) =>
        OverlaySettings.ValidateStockLaunchCopy(table, copy, flash);
}
