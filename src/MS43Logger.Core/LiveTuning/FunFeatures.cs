using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.LiveTuning;

/// <summary>Which of the Fun tab's features a connected ECU or an open bin has, in the tab's order.</summary>
public static class FunFeatures
{
    public const string LaunchControl = "lc", NoLiftShift = "nls", RollingAntiLag = "ral", Hardcut = "hardcut", Pops = "pops", Blip = "blip";

    public static IReadOnlyList<string> Order { get; } = [LaunchControl, NoLiftShift, RollingAntiLag, Hardcut, Pops, Blip];
}
