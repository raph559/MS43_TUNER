namespace MS43Logger.Core.LiveTuning;

/// <summary>The original stock launch-group catalogue and activation limits.</summary>
public static class OverlayV6
{
    public const int LaunchControl = OverlaySettings.LaunchControl, NoLiftShift = OverlaySettings.NoLiftShift, RollingAntiLag = OverlaySettings.RollingAntiLag;
    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayCatalogue.Maps.Take(127).ToArray());
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash) =>
        OverlaySettings.ValidateStockLaunchCopy(table, copy, flash);
}
