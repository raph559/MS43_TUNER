namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// Protocol 10: V9 plus the pops stabs (group 139) and the downshift blip (140, with its gears and tap in 141 and its gear
/// ratios in 142). Each group is 8 bytes, its flashed defaults after pops' at 0C:FF88. V10's state takes pool A's bytes
/// from 668, so allocations end there and the status reports 896 free. V10.1 (header minor 1) adds 139/4, the rise
/// allowed once stabbing, and 141/6, the blip lead, and takes the pops AFR target down to 7.4. V10.2 (minor 2) makes the
/// pops AFR target the real AFR × 10 in petrol AFR (5.0..14.7) and adds 141/7, the blip's spark above its target. V10.3
/// (minor 3) removes the stabs: a pops window keeps its own opening and ends at a 160 rpm rise, and 139 is reserved.
/// </summary>
public static class OverlayV10
{
    public const int Version = 10, PoolABytes = 668, CopyBytes = 896, Stabs = 139, Blip = 140, BlipGears = 141, BlipRatios = 142;

    /// <summary>The header minor of V10.1, whose rules and settings <see cref="MapsFor"/> and <see cref="ValidateCopy"/> follow.</summary>
    public const int V101Minor = 1;

    /// <summary>The header minor of V10.2: the real pops AFR (138/3) and the blip's spark above its target (141/7).</summary>
    public const int V102Minor = 2;

    /// <summary>The header minor of V10.3: V10.2 without the pops stabs; group 139 is reserved.</summary>
    public const int V103Minor = 3;
    public const string BlipCategory = "Downshift blip";

    private const int PopsAfr = 3, PopsThrottle = 6, BlipOpening = 5, HoldOpening = 6, StabsRise = 4, BlipLead = 6, CapSpark = 7;
    private const string Note = "A change takes effect at the next downshift at the latest. The live settings fall back to the flashed ones 3 s after the app goes quiet.";

    /// <summary>Each byte's lowest and highest value in groups 139..142, as the firmware's <c>v10_limits</c> holds them; reserved bytes are 0.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> Limits { get; } = new Dictionary<int, IReadOnlyList<(byte Low, byte High)>>
    {
        [Stabs] = Array.AsReadOnly(new (byte, byte)[] { (0, 50), (0, 150), (5, 30), (10, 100), (0, 0), (0, 0), (0, 0), (0, 0) }),
        [Blip] = Array.AsReadOnly(new (byte, byte)[] { (0, 2), (1, 3), (10, 255), (63, 187), (3, 30), (0, 150), (0, 150), (63, 143) }),
        [BlipGears] = Array.AsReadOnly(new (byte, byte)[] { (0, 1), (0, 1), (0, 1), (0, 1), (11, 85), (10, 60), (0, 0), (0, 0) }),
        [BlipRatios] = Array.AsReadOnly(new (byte, byte)[] { (180, 255), (100, 160), (70, 110), (50, 85), (0, 0), (0, 0), (0, 0), (0, 0) }),
    };

    /// <summary>V10.1's limits: V10's, with 139/4 the rise allowed once stabbing (5..31) and 141/6 the blip lead (0..30).</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsV101 { get; } = Limits.ToDictionary(
        pair => pair.Key, pair => (IReadOnlyList<(byte Low, byte High)>)Array.AsReadOnly(pair.Value.Select((limit, i) =>
            (pair.Key, i) switch { (Stabs, StabsRise) => ((byte)5, (byte)31), (BlipGears, BlipLead) => ((byte)0, (byte)30), _ => limit }).ToArray()));

    /// <summary>V10.1's pops limits: V9's, with the AFR target up to 255 (lambda 0.504, AFR 7.4).</summary>
    public static IReadOnlyList<(byte Low, byte High)> PopsLimitsV101 { get; } =
        Array.AsReadOnly(OverlayV9.PopsLimits.Select((limit, i) => i == PopsAfr ? ((byte)128, (byte)255) : limit).ToArray());

    /// <summary>V10.2's limits: V10.1's, with 141/7 the spark above the blip's target (0..143, −23.625..+30.0 °BTDC).</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsV102 { get; } = LimitsV101.ToDictionary(
        pair => pair.Key, pair => (IReadOnlyList<(byte Low, byte High)>)Array.AsReadOnly(pair.Value.Select((limit, i) =>
            (pair.Key, i) switch { (BlipGears, CapSpark) => ((byte)0, (byte)143), _ => limit }).ToArray()));

    /// <summary>V10.2's pops limits: V10.1's, with the AFR target the real AFR × 10, 50..147 (5.0..14.7 in petrol AFR).</summary>
    public static IReadOnlyList<(byte Low, byte High)> PopsLimitsV102 { get; } =
        Array.AsReadOnly(PopsLimitsV101.Select((limit, i) => i == PopsAfr ? ((byte)50, (byte)147) : limit).ToArray());

    /// <summary>V10.3's limits: V10.2's, with group 139 reserved, every byte 0.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsV103 { get; } = LimitsV102.ToDictionary(
        pair => pair.Key, pair => pair.Key == Stabs ? (IReadOnlyList<(byte Low, byte High)>)Array.AsReadOnly(new (byte, byte)[8]) : pair.Value);

    /// <summary>The limits of groups 139..142 for an overlay of this minor: V10.3's from minor 3, V10.2's from 2, V10.1's from 1.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsFor(int minor) =>
        minor >= V103Minor ? LimitsV103 : minor >= V102Minor ? LimitsV102 : minor >= V101Minor ? LimitsV101 : Limits;

    /// <summary>V9's pops group, with the opening's hover warning that the stabs need 4.0 °TPS or more.</summary>
    public static OverlayScalarGroup PopsGroup { get; } = OverlayV9.PopsGroup with
    {
        Fields = OverlayV9.PopsGroup.Fields.Select(field => field.Offset != PopsThrottle ? field : field with
        {
            Definition = field.Definition! with
            {
                Description = field.Definition.Description + " Below 4.0 °TPS the stabs' gaps blur: the ECU closes the throttle slowly there.",
            },
        }).ToArray(),
    };

    public static OverlayScalarGroup StabsGroup { get; } = new("Pops stabs", new OverlayScalar[]
    {
        new("stabs_load", "Load time", 0, new("Pops stabs load time",
            "How long a pops window runs at the plain throttle opening before the first stab. A window no longer than this has no stab.",
            "s", "0.1*X", 0, 5.0, 1, OverlayV8.Category)),
        new("stabs_size", "Stab size", 1, new("Pops stab size",
            "Added on top of the pops throttle opening during each stab, at most 30.0 °TPS. 0 turns the stabs off, and the window keeps the plain opening, as on V9.",
            "°TPS", "0.2*X", 0, 30.0, 1, OverlayV8.Category)),
        new("stabs_length", "Stab length", 2, new("Pops stab length",
            "How long each stab lasts, 50 to 300 ms. A stab's extra drops out while the ECU's torque check has less than 20 Nm of margin left.",
            "ms", "10*X", 50, 300, 0, OverlayV8.Category)),
        new("stabs_gap", "Gap", 3, new("Pops stab gap",
            "The time at the plain opening between two stabs, 100 ms to 1.0 s.",
            "ms", "10*X", 100, 1000, 0, OverlayV8.Category)),
    }, "A change takes effect at the next stab at the latest. The live settings fall back to the flashed ones 3 s after the app goes quiet.");

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
    }, Note);

    public static OverlayScalarGroup BlipRatiosGroup { get; } = new("Downshift blip: gear ratios", new OverlayScalar[]
    {
        Ratio(0, "1st", 90, 127.5), Ratio(1, "2nd", 50, 80), Ratio(2, "3rd", 35, 55), Ratio(3, "4th", 25, 42.5),
    }, Note);

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV9.Maps
        .Select(map => map.Index == OverlayV8.Pops ? map with { ScalarGroup = PopsGroup } : map)
        .Append(new OverlayMap(Stabs, "pops_stabs", "Pops stabs", 0xCFF88, 8, false, StabsGroup))
        .Append(new OverlayMap(Blip, "blip", "Downshift blip", 0xCFF90, 8, false, BlipGroup))
        .Append(new OverlayMap(BlipGears, "blip_gears", "Downshift blip: gears and tap", 0xCFF98, 8, false, BlipGearsGroup))
        .Append(new OverlayMap(BlipRatios, "blip_ratios", "Downshift blip: gear ratios", 0xCFFA0, 8, false, BlipRatiosGroup))
        .ToArray());

    /// <summary>V10.1's pops group: the AFR target down to 7.4, and the opening's rule for the rise once the stabs have begun.</summary>
    public static OverlayScalarGroup PopsGroupV101 { get; } = PopsGroup with
    {
        Fields = PopsGroup.Fields.Select(field => field.Offset switch
        {
            PopsAfr => field with { Definition = field.Definition! with { RangeLow = 7.40 } },
            PopsThrottle => field with
            {
                Definition = field.Definition! with
                {
                    Description = field.Definition.Description.Replace("rises 160 rpm,",
                        "rises 160 rpm during the load, or the stabs' allowed rise once they have begun,", StringComparison.Ordinal),
                },
            },
            _ => field,
        }).ToArray(),
    };

    public static OverlayScalarGroup StabsGroupV101 { get; } = StabsGroup with
    {
        Fields = StabsGroup.Fields.Append(new("stabs_rise", "Rise allowed during stabs", StabsRise, new("Pops stabs rise allowed",
            "Once the stabs have begun, a pops window ends when the engine speed rises this far above its lowest in the window; "
                + "during the load it ends at 160 rpm. On the car a stab adds about 200 rpm, so 160 rpm ended the window after one or two stabs.",
            "rpm", "32*X", 160, 992, 0, OverlayV8.Category))).ToArray(),
    };

    public static OverlayScalarGroup BlipGearsGroupV101 { get; } = BlipGearsGroup with
    {
        Fields = BlipGearsGroup.Fields.Append(new("blip_lead", "Blip lead", BlipLead, new("Downshift blip lead",
            "How far ahead the blip looks while the engine speed rises: the blip opening gives way to the hold opening when the "
                + "engine speed, climbing as fast as it is now, would be within 192 rpm of the target this long from now. "
                + "Longer ends the rise earlier, against an overshoot; 0 waits for the engine speed itself, as V10 did.",
            "ms", "10*X", 0, 300, 0, BlipCategory))).ToArray(),
    };

    /// <summary>V10.1's catalogue: V10's, with V10.1's pops, stabs and gears groups.</summary>
    public static IReadOnlyList<OverlayMap> MapsV101 { get; } = Array.AsReadOnly(Maps.Select(map => map.Index switch
    {
        OverlayV8.Pops => map with { ScalarGroup = PopsGroupV101 },
        Stabs => map with { ScalarGroup = StabsGroupV101 },
        BlipGears => map with { ScalarGroup = BlipGearsGroupV101 },
        _ => map,
    }).ToArray());

    /// <summary>V10.2's pops group: the AFR target is the real mixture in petrol AFR, 5.0..14.7 (a lambda, A/147, on either fuel).</summary>
    public static OverlayScalarGroup PopsGroupV102 { get; } = PopsGroupV101 with
    {
        Fields = PopsGroupV101.Fields.Select(field => field.Offset != PopsAfr ? field : field with
        {
            Label = "AFR target (petrol)",
            Definition = new("Pops AFR target (petrol)",
                "The real mixture during pops, in petrol AFR. On E85 the same number gives the same richness. "
                    + "The ECU uses this or the car file's own target, whichever is richer.",
                "AFR", "0.1*X", 5.0, 14.7, 1, OverlayV8.Category),
        }).ToArray(),
    };

    /// <summary>V10.2's gears group: V10.1's, then the spark above the blip's target.</summary>
    public static OverlayScalarGroup BlipGearsGroupV102 { get; } = BlipGearsGroupV101 with
    {
        Fields = BlipGearsGroupV101.Fields.Append(new("blip_cap_spark", "Spark above the target", CapSpark, new(
            "Downshift blip spark above the target",
            "The spark while the engine is above the blip's target. A late angle makes less torque from the air already in "
                + "the intake, so the blip overshoots less. Back at or below the target, the spark returns to the blip angle "
                + "over a few firings. At or above the blip's own angle the cap is off. −23.625 to +30.0 °BTDC.",
            "°BTDC", "0.375*X-23.625", -23.625, 30.0, 3, BlipCategory))).ToArray(),
    };

    /// <summary>V10.2's catalogue: V10.1's, with V10.2's pops and gears groups.</summary>
    public static IReadOnlyList<OverlayMap> MapsV102 { get; } = Array.AsReadOnly(MapsV101.Select(map => map.Index switch
    {
        OverlayV8.Pops => map with { ScalarGroup = PopsGroupV102 },
        BlipGears => map with { ScalarGroup = BlipGearsGroupV102 },
        _ => map,
    }).ToArray());

    /// <summary>V10.3's pops group: V10.2's, with V9's opening text (160 rpm, no stabs).</summary>
    public static OverlayScalarGroup PopsGroupV103 { get; } = PopsGroupV102 with
    {
        Fields = PopsGroupV102.Fields.Select(field => field.Offset != PopsThrottle ? field
            : OverlayV9.PopsGroup.Fields.Single(v9 => v9.Offset == PopsThrottle)).ToArray(),
    };

    /// <summary>V10.3's catalogue: V10.2's without the stabs. Group 139 stays as a reserved entry, never shown or sent.</summary>
    public static IReadOnlyList<OverlayMap> MapsV103 { get; } = Array.AsReadOnly(MapsV102.Select(map => map.Index switch
    {
        OverlayV8.Pops => map with { ScalarGroup = PopsGroupV103 },
        Stabs => new OverlayMap(Stabs, "reserved_139", "Reserved", 0xCFF88, 8, false, Reserved: true),
        _ => map,
    }).ToArray());

    /// <summary>The catalogue of an overlay 10 of this header minor: V10.3's from minor 3, V10.2's from 2, V10.1's from 1, V10's below.</summary>
    public static IReadOnlyList<OverlayMap> MapsFor(int minor) =>
        minor >= V103Minor ? MapsV103 : minor >= V102Minor ? MapsV102 : minor >= V101Minor ? MapsV101 : Maps;

    /// <summary>
    /// V10's rule for a copy of a settings group: in groups 139..142 every byte within its limits (140's switch may also be
    /// a CAN bit, 80h..9Fh) and 140's hold opening at most its blip opening; every other table as V9. From minor 1, V10.1's
    /// limits, and pops' AFR target up to 255; from minor 2, V10.2's, the AFR target the real AFR × 10, 50..147.
    /// </summary>
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash, int minor = 0)
    {
        if (table == OverlayV8.Pops && minor >= V101Minor) return OverlayV9.ValidatePops(copy, minor >= V102Minor ? PopsLimitsV102 : PopsLimitsV101);
        if (!Limits.ContainsKey(table)) return OverlayV9.ValidateCopy(table, copy, flash);
        if (copy.Length != 8 || !WithinLimits(table, copy, LimitsFor(minor))) return OverlayReason.Range;
        return table == Blip && copy[HoldOpening] > copy[BlipOpening] ? OverlayReason.Range : null;
    }

    /// <summary>
    /// Why the app refuses a copy of a settings group, in the words it shows: the group's limits, naming 140's hold rule
    /// when that alone is broken; null for LC, NLS and RAL, whose callers word their own refusal.
    /// </summary>
    public static string? Refusal(int table, ReadOnlySpan<byte> copy) => table switch
    {
        OverlayV8.Pops => "A pops setting is outside its limits",
        Stabs => "A stab setting is outside its limits",
        Blip when copy.Length == 8 && WithinLimits(table, copy, Limits) && copy[HoldOpening] > copy[BlipOpening]
            => "A blip setting is outside its limits: the hold opening is above the blip opening",
        Blip or BlipGears or BlipRatios => "A blip setting is outside its limits",
        _ => null,
    };

    /// <summary>Whether every byte of an 8-byte copy of group 139..142 is within its limits (140's switch may be a CAN bit).</summary>
    private static bool WithinLimits(int table, ReadOnlySpan<byte> copy, IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> all)
    {
        var limits = all[table];
        for (var i = 0; i < 8; i++)
            if ((table != Blip || i != 0 || !OverlayV8.IsCanBit(copy[i])) && (copy[i] < limits[i].Low || copy[i] > limits[i].High))
                return false;
        return true;
    }

    /// <summary>The blip switch's choices are pops' own, one "n: label" line each, which the editor shows as a list.</summary>
    private static string SwitchDescription() => string.Join("\n",
        new[] { "When the ECU may blip the throttle on a downshift. Cruise control armed: the main switch is on and no speed is held." }
            .Concat(OverlayV8.Triggers.Select(trigger => $"{trigger.Value}: {trigger.Label}")));

    private static OverlayScalarDefinition Downshift(string shift, string description) =>
        new($"Blip on {shift}", description + "\n0: Off\n1: On", "-", "X", 0, 1, 0, BlipCategory);

    private static OverlayScalar Ratio(int offset, string gear, double low, double high) =>
        new($"blip_ratio_{offset + 1}", $"rpm per km/h, {gear}", offset, new($"Engine speed per km/h in {gear}",
            $"The engine rpm per km/h of road speed in {gear}. A blip into {gear} aims at the road speed times this; check it against a log in {gear}.",
            "rpm/(km/h)", "0.5*X", low, high, 1, BlipCategory));
}
