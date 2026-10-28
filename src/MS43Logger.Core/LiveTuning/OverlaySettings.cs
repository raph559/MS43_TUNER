namespace MS43Logger.Core.LiveTuning;

/// <summary>Current settings groups, field definitions and firmware range checks.</summary>
public static class OverlaySettings
{
    public const int LaunchControl = 124, NoLiftShift = 125, RollingAntiLag = 126;
    public const int Pops = 138, DefaultsAddress = 0xCFF80, Reserved = 139, Blip = 140, BlipGears = 141, BlipRatios = 142;

    public const string PopsCategory = "Pops & Bangs";
    public const string BlipCategory = "Downshift blip";

    private const int BlipOpening = 5, HoldOpening = 6, BlipLead = 6, CapSpark = 7;
    private const string Note = "A change takes effect at the next downshift at the latest. The live settings fall back to the flashed ones 3 s after the app goes quiet.";

    /// <summary>Each byte's lowest and highest value in groups 139..142, as the firmware's <c>v10_limits</c> holds them; reserved bytes are 0.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> Limits { get; } = new Dictionary<int, IReadOnlyList<(byte Low, byte High)>>
    {
        [Reserved] = Array.AsReadOnly(new (byte, byte)[8]),
        [Blip] = Array.AsReadOnly(new (byte, byte)[] { (0, 2), (1, 3), (10, 255), (63, 187), (3, 30), (0, 150), (0, 150), (63, 143) }),
        [BlipGears] = Array.AsReadOnly(new (byte, byte)[] { (0, 1), (0, 1), (0, 1), (0, 1), (11, 85), (10, 60), (0, 30), (0, 143) }),
        [BlipRatios] = Array.AsReadOnly(new (byte, byte)[] { (180, 255), (100, 160), (70, 110), (50, 85), (0, 0), (0, 0), (0, 0), (0, 0) }),
    };

    // Petrol AFR × 10 and spark angle −23.625..+30.0 °BTDC.
    public static IReadOnlyList<(byte Low, byte High)> PopsLimits { get; } = Array.AsReadOnly(new (byte, byte)[]
    {
        (0, 2), (1, 50), (0, 143), (50, 147), (50, 219), (144, 255), (0, 200), (0, 255),
    });

    internal static IReadOnlyList<OverlayMap> LaunchMaps { get; } = Array.AsReadOnly(new[]
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
    });

    public static OverlayScalarGroup PopsGroup { get; } = new("Pops & bangs", new OverlayScalar[]
    {
        new("pops_switch", "Switch", 0, new("Switch", PopsSwitchDescription(), "-", "X", 0, 0x9F, 0, PopsCategory)),
        new("pops_duration", "Duration", 1, new("Pops duration",
            "How long the overrun fuel cut is held off after the moment the ECU would cut the fuel. At most 5.0 s.",
            "s", "0.1*X", 0.1, 5.0, 1, PopsCategory)),
        new("pops_ignition", "Ignition angle", 2, new("Pops ignition angle",
            "The fixed spark angle during pops, whatever the engine speed and load. Negative is after top dead "
                + "centre. −23.625 to +30.0 °BTDC, the firmware's limits.",
            "°BTDC", "0.375*X-23.625", -23.625, 30.0, 3, PopsCategory)),
        new("pops_afr", "AFR target (petrol)", 3, new("Pops AFR target (petrol)",
            "The real mixture during pops, in petrol AFR. On E85 the same number gives the same richness. "
                + "The ECU uses this or the car file's own target, whichever is richer.",
            "AFR", "0.1*X", 5.0, 14.7, 1, PopsCategory)),
        new("pops_rpm_min", "Minimum engine speed", 4, new("Pops minimum engine speed",
            "No pops below this engine speed; never below 1,600 rpm, where the lambda control would fight the rich mixture.",
            "rpm", "32*X", 1600, 7008, 0, PopsCategory)),
        new("pops_coolant_min", "Minimum coolant temperature", 5, new("Pops minimum coolant temperature",
            "No pops with a colder engine; never below 60 °C.",
            "°C", "0.75*X-48", 60, 143.25, 2, PopsCategory)),
        new("pops_throttle", "Throttle opening", 6, new("Pops throttle opening",
            "Added to the stock throttle setpoint during pops, at most 20.0 °TPS. Pops stops as soon as the engine "
                + "speed rises 160 rpm, so the car cannot keep pulling.",
            "°TPS", "0.1*X", 0, 20.0, 1, PopsCategory)),
        new("pops_speed_min", "Minimum vehicle speed", 7, new("Pops minimum vehicle speed",
            "No pops below this road speed. The clutch, the pedal, LC, NLS and rolling anti-lag also end pops.",
            "km/h", "X", 0, 255, 0, PopsCategory)),
    }, "A change takes effect at the next lift-off. The live settings fall back to the flashed ones 3 s after the app goes quiet.");

    public static OverlayScalarGroup BlipGroup { get; } = new("Downshift blip", new OverlayScalar[]
    {
        new("blip_switch", "Switch", 0, new("Downshift blip switch", SwitchDescription(), "-", "X", 0, 0x9F, 0, BlipCategory)),
        new("blip_signs", "Signs", 1, new("Downshift blip signs",
            "What starts a blip once the clutch is in: braking (both brake switches pressed), or a short tap of the pedal. One blip per clutch press.\n"
                + "1: While braking\n2: On a pedal tap\n3: Braking or tap",
            "-", "X", 1, 3, 0, BlipCategory)),
        new("blip_speed_min", "Minimum vehicle speed", 2, new("Downshift blip minimum vehicle speed",
            "No blip below this road speed, and a blip ends when the car slows under it. Never below 10 km/h.",
            "km/h", "X", 10, 255, 0, BlipCategory)),
        new("blip_rpm_max", "Maximum engine speed", 3, new("Downshift blip maximum engine speed",
            "The blip's target engine speed is capped here, and a blip ends when the engine reaches it. At most 5,984 rpm.",
            "rpm", "32*X", 2016, 5984, 0, BlipCategory)),
        new("blip_time_max", "Maximum time", 4, new("Downshift blip maximum time",
            "A blip ends after this long, even with the clutch still in.",
            "s", "0.1*X", 0.3, 3.0, 1, BlipCategory)),
        new("blip_opening", "Blip opening", 5, new("Downshift blip opening",
            "Added to the stock throttle setpoint while the engine speed rises to the target: the road speed times the lower gear's rpm per km/h. At most 30.0 °TPS.",
            "°TPS", "0.2*X", 0, 30.0, 1, BlipCategory)),
        new("blip_hold", "Hold opening", 6, new("Downshift blip hold opening",
            "Added once the engine speed is near the target, while it is below it, to hold it there until the clutch comes up. At most the blip opening.",
            "°TPS", "0.2*X", 0, 30.0, 1, BlipCategory)),
        new("blip_spark", "Spark angle", 7, new("Downshift blip spark angle",
            "The fixed spark angle during a blip, in place of the overrun's retarded spark. 0.0 to +30.0 °BTDC.",
            "°BTDC", "0.375*X-23.625", 0.0, 30.0, 3, BlipCategory)),
    }, Note);

    public static OverlayScalarGroup BlipGearsGroup { get; } = new("Downshift blip: gears and tap", new OverlayScalar[]
    {
        new("blip_5_4", "5th → 4th", 0, Downshift("5th → 4th", "Whether a downshift from 5th to 4th blips.")),
        new("blip_4_3", "4th → 3rd", 1, Downshift("4th → 3rd", "Whether a downshift from 4th to 3rd blips.")),
        new("blip_3_2", "3rd → 2nd", 2, Downshift("3rd → 2nd", "Whether a downshift from 3rd to 2nd blips.")),
        new("blip_2_1", "2nd → 1st", 3, Downshift("2nd → 1st",
            "Whether a downshift from 2nd to 1st blips, on a pedal tap only: braking in 2nd is mostly a stop, so it never blips.")),
        new("blip_tap_threshold", "Tap threshold", 4, new("Blip tap threshold",
            "How far the pedal must rise for a tap. Pressed past this during a blip, the pedal ends the blip: the driver has it.",
            "°PVS", "0.46875*X", 5.15625, 39.84375, 2, BlipCategory)),
        new("blip_tap_length", "Tap length", 5, new("Blip tap length",
            "A tap is the pedal up past the threshold and back below it within this time; the blip starts as it falls back. Held longer, as at the end of an upshift, the pedal is the driver's and nothing happens.",
            "ms", "10*X", 100, 600, 0, BlipCategory)),
        new("blip_lead", "Blip lead", BlipLead, new("Downshift blip lead",
            "How far ahead the blip looks while the engine speed rises: the blip opening gives way to the hold opening when the "
                + "engine speed, climbing as fast as it is now, would be within 192 rpm of the target this long from now. "
                + "Longer ends the rise earlier, against an overshoot; 0 waits for the engine speed itself.",
            "ms", "10*X", 0, 300, 0, BlipCategory)),
        new("blip_cap_spark", "Spark above the target", CapSpark, new("Downshift blip spark above the target",
            "The spark while the engine is above the blip's target. A late angle makes less torque from the air already in "
                + "the intake, so the blip overshoots less. Back at or below the target, the spark returns to the blip angle "
                + "over a few firings. At or above the blip's own angle the cap is off. −23.625 to +30.0 °BTDC.",
            "°BTDC", "0.375*X-23.625", -23.625, 30.0, 3, BlipCategory)),
    }, Note);

    public static OverlayScalarGroup BlipRatiosGroup { get; } = new("Downshift blip: gear ratios", new OverlayScalar[]
    {
        Ratio(0, "1st", 90, 127.5), Ratio(1, "2nd", 50, 80), Ratio(2, "3rd", 35, 55), Ratio(3, "4th", 25, 42.5),
    }, Note);

    /// <summary>Checks firmware byte limits, CAN trigger modes, the RAL neighbour byte and the blip hold/opening rule.</summary>
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table == Pops) return ValidatePops(copy, PopsLimits);
        if (!Limits.ContainsKey(table)) return ValidateLaunchCopy(table, copy, flash);
        if (copy.Length != 8 || !WithinLimits(table, copy)) return OverlayReason.Range;
        return table == Blip && copy[HoldOpening] > copy[BlipOpening] ? OverlayReason.Range : null;
    }

    /// <summary>Names a pops or blip refusal; LC, NLS and RAL callers supply their own message.</summary>
    public static string? Refusal(int table, ReadOnlySpan<byte> copy) => table switch
    {
        Pops => "A pops setting is outside its limits",
        Blip when copy.Length == 8 && WithinLimits(table, copy) && copy[HoldOpening] > copy[BlipOpening]
            => "A blip setting is outside its limits: the hold opening is above the blip opening",
        Blip or BlipGears or BlipRatios => "A blip setting is outside its limits",
        _ => null,
    };

    /// <summary>Whether every byte of an 8-byte copy of group 139..142 is within its limits (140's switch may be a CAN bit).</summary>
    private static bool WithinLimits(int table, ReadOnlySpan<byte> copy)
    {
        var limits = Limits[table];
        for (var i = 0; i < 8; i++)
            if ((table != Blip || i != 0 || !OverlayTriggers.IsCanBit(copy[i])) && (copy[i] < limits[i].Low || copy[i] > limits[i].High))
                return false;
        return true;
    }

    /// <summary>The blip switch's choices are pops' own, one "n: label" line each, which the editor shows as a list.</summary>
    private static string SwitchDescription() => string.Join("\n",
        new[] { "When the ECU may blip the throttle on a downshift. Cruise control armed: the main switch is on and no speed is held." }
            .Concat(OverlayTriggers.Pops.Select(trigger => $"{trigger.Value}: {trigger.Label}")));

    private static OverlayScalarDefinition Downshift(string shift, string description) =>
        new($"Blip on {shift}", description + "\n0: Off\n1: On", "-", "X", 0, 1, 0, BlipCategory);

    private static OverlayScalar Ratio(int offset, string gear, double low, double high) =>
        new($"blip_ratio_{offset + 1}", $"rpm per km/h, {gear}", offset, new($"Engine speed per km/h in {gear}",
            $"The engine rpm per km/h of road speed in {gear}. A blip into {gear} aims at the road speed times this; check it against a log in {gear}.",
            "rpm/(km/h)", "0.5*X", low, high, 1, BlipCategory));

    private static OverlayReason? ValidateLaunchCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table is < LaunchControl or > RollingAntiLag || copy.Length != 8 || !OverlayTriggers.IsCanBit(copy[0]))
            return ValidateStockLaunchCopy(table, copy, flash);
        var stock = copy.ToArray(); stock[0] = 1; // A CAN trigger stands for an enabled mode.
        return ValidateStockLaunchCopy(table, stock, flash);
    }

    internal static OverlayReason? ValidateStockLaunchCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table is < LaunchControl or > RollingAntiLag) return null;
        if (copy.Length != 8 || flash.Length != 8 || copy[0] > (table == RollingAntiLag ? 1 : 2))
            return OverlayReason.Range;
        // The eighth RAL byte belongs to the adjacent overboost calibration.
        return table == RollingAntiLag && copy[7] != flash[7] ? OverlayReason.Range : null;
    }

    /// <summary>A pops copy against <paramref name="limits"/>; the switch may be a CAN bit.</summary>
    internal static OverlayReason? ValidatePops(ReadOnlySpan<byte> copy, IReadOnlyList<(byte Low, byte High)> limits)
    {
        if (copy.Length != 8) return OverlayReason.Range;
        for (var i = 0; i < 8; i++)
            if ((i != 0 || !OverlayTriggers.IsCanBit(copy[i])) && (copy[i] < limits[i].Low || copy[i] > limits[i].High))
                return OverlayReason.Range;
        return null;
    }

    /// <summary>The switch's choices, one "n: label" line per trigger, which the editor shows as a list.</summary>
    private static string PopsSwitchDescription() => string.Join("\n",
        new[] { "When pops may happen after a lift-off. Cruise control armed: the main switch is on and no speed is held." }
            .Concat(OverlayTriggers.Pops.Select(trigger => $"{trigger.Value}: {trigger.Label}")));
}
