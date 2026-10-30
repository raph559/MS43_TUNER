using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.LiveTuning;

/// <summary>Which of the Fun tab's features a connected ECU or an open bin has, in the tab's order.</summary>
public static class FunFeatures
{
    public const string LaunchControl = "lc", NoLiftShift = "nls", RollingAntiLag = "ral", Hardcut = "hardcut", Pops = "pops", Blip = "blip";

    public static IReadOnlyList<string> Order { get; } = [LaunchControl, NoLiftShift, RollingAntiLag, Hardcut, Pops, Blip];

    /// <summary>The feature a settings group belongs to: the blip's gears and ratios to the blip.</summary>
    public static string? KeyOf(int mapIndex) => mapIndex switch
    {
        OverlaySettings.LaunchControl => LaunchControl,
        OverlaySettings.NoLiftShift => NoLiftShift,
        OverlaySettings.RollingAntiLag => RollingAntiLag,
        OverlaySettings.Pops => Pops,
        OverlaySettings.Blip or OverlaySettings.BlipGears or OverlaySettings.BlipRatios => Blip,
        _ => null,
    };

    /// <summary>The feature's main group, whose byte 0 is its switch.</summary>
    public static int? MapIndexOf(string key) => key switch
    {
        LaunchControl => OverlaySettings.LaunchControl,
        NoLiftShift => OverlaySettings.NoLiftShift,
        RollingAntiLag => OverlaySettings.RollingAntiLag,
        Pops => OverlaySettings.Pops,
        Blip => OverlaySettings.Blip,
        _ => null,
    };

    /// <summary>
    /// Every group the feature's page shows, its main group first: the blip with its gears and ratios (a switch per downshift on the blip's page).
    /// A group the ECU or the bin lacks is left out by the caller.
    /// </summary>
    public static IReadOnlyList<int> MapIndicesOf(string key) => key switch
    {
        Blip => [OverlaySettings.Blip, OverlaySettings.BlipGears, OverlaySettings.BlipRatios],
        _ => MapIndexOf(key) is { } index ? [index] : [],
    };

    /// <summary>Connected: the settings groups the verified catalogue holds (with their definitions), and the hardcut when the overlay has the limiter.</summary>
    public static IReadOnlyList<string> Live(IEnumerable<int> groupsWithDefinitions, bool limiter)
    {
        var present = groupsWithDefinitions.Select(KeyOf).OfType<string>().ToHashSet();
        if (limiter) present.Add(Hardcut);
        return Order.Where(present.Contains).ToArray();
    }

    /// <summary>
    /// Offline: LC, NLS and RAL when every field has its XDF constant; the hardcut when its three XDF items exist; pops and
    /// the blip when the bin carries the current overlay (V10.4).
    /// </summary>
    public static IReadOnlyList<string> Offline(CalibrationDocument bin, IReadOnlyList<OverlayMap>? groups = null)
    {
        ArgumentNullException.ThrowIfNull(bin);
        var present = new HashSet<string>();
        var current = HasCurrentOverlay(bin);
        foreach (var map in groups ?? OverlayCatalogue.Maps.Where(m => m.ScalarGroup is not null).ToArray())
        {
            if (KeyOf(map.Index) is not { } key) continue;
            if (key is Pops or Blip && !current) continue;
            if (LiveScalarDefinitions.ConstantsFor(map, bin.Xdf) is not null) present.Add(key);
        }
        if (LimiterInBin.Find(bin.Xdf) is not null) present.Add(Hardcut);
        return Order.Where(present.Contains).ToArray();
    }

    /// <summary>The overlay header in the bin, or null when the bin has no live overlay.</summary>
    public static OverlayHeader? OverlayOf(CalibrationDocument bin) =>
        OverlayHeader.Parse(bin.Image.Bytes.Slice(OverlayContract.HeaderAddress & 0x7FFFF, 16).ToArray());

    /// <summary>Whether the bin carries the current overlay, V10.4.</summary>
    public static bool HasCurrentOverlay(CalibrationDocument bin) => OverlayProtocol.IsCurrent(OverlayOf(bin));
}
