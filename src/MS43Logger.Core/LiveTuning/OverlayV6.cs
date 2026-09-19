namespace MS43Logger.Core.LiveTuning;

public static class OverlayV6
{
    public const int Version = 6, LaunchControl = 124, NoLiftShift = 125, RollingAntiLag = 126;

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV4.Maps.Concat(new[]
    {
        new OverlayMap(LaunchControl, "launch_control", "Launch control", 0x7132C, 8, false,
            new OverlayScalarGroup("Launch control", new OverlayScalar[]
            {
                new("c_conf_lc", "Activation", 0),
                new("c_lc_vs_max", "Maximum vehicle speed", 1),
                new("c_lc_pvs_min", "Minimum pedal position", 2),
                new("c_lc_n_max", "Engine speed limit", 3),
                new("c_lc_iga_ofs_e85", "Ignition offset · E85", 4),
                new("c_lc_iga_ofs_ron98", "Ignition offset · RON98", 5),
                new("c_lc_af_target_e85", "AFR target · E85", 6),
                new("c_lc_af_target_ron98", "AFR target · RON98", 7),
            })),
        new OverlayMap(NoLiftShift, "no_lift_shift", "No-lift shift", 0x71334, 8, false,
            new OverlayScalarGroup("No-lift shift", new OverlayScalar[]
            {
                new("c_conf_nls", "Activation", 0),
                new("c_nls_vs_min", "Minimum vehicle speed", 1),
                new("c_nls_pvs_min", "Minimum pedal position", 2),
                new("c_nls_act_ofs_n_max", "Engine speed offset at activation", 3),
                new("c_nls_iga_ofs_e85", "Ignition offset · E85", 4),
                new("c_nls_iga_ofs_ron98", "Ignition offset · RON98", 5),
                new("c_nls_af_target_e85", "AFR target · E85", 6),
                new("c_nls_af_target_ron98", "AFR target · RON98", 7),
            }, "The engine speed offset takes effect at the next no-lift-shift activation.")),
        new OverlayMap(RollingAntiLag, "rolling_anti_lag", "Rolling anti-lag", 0x7133C, 8, false,
            new OverlayScalarGroup("Rolling anti-lag", new OverlayScalar[]
            {
                new("c_conf_ral", "Activation", 0),
                new("c_ral_pvs_min", "Minimum pedal position", 1),
                new("c_ral_act_ofs_n_max", "Engine speed offset at activation", 2),
                new("c_ral_iga_ofs_e85", "Ignition offset · E85", 3),
                new("c_ral_iga_ofs_ron98", "Ignition offset · RON98", 4),
                new("c_ral_af_target_e85", "AFR target · E85", 5),
                new("c_ral_af_target_ron98", "AFR target · RON98", 6),
            }, "The engine speed offset takes effect at the next rolling anti-lag activation.")),
    }).ToArray());

    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table is < LaunchControl or > RollingAntiLag) return null;
        if (copy.Length != 8 || flash.Length != 8 || copy[0] > (table == RollingAntiLag ? 1 : 2))
            return OverlayReason.Range;
        // The eighth RAL byte belongs to the adjacent overboost calibration.
        return table == RollingAntiLag && copy[7] != flash[7] ? OverlayReason.Range : null;
    }
}
