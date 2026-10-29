namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for the current V10.4 overlay.</summary>
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

    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> Limits => OverlaySettings.Limits;

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
    public static IReadOnlyList<(byte Low, byte High)> PopsLimits => OverlaySettings.PopsLimits;
    public static OverlayScalarGroup PopsGroup => OverlaySettings.PopsGroup;

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
    public static OverlayScalarGroup BlipGroup => OverlaySettings.BlipGroup;
    public static OverlayScalarGroup BlipGearsGroup => OverlaySettings.BlipGearsGroup;
    public static OverlayScalarGroup BlipRatiosGroup => OverlaySettings.BlipRatiosGroup;
    public static IReadOnlyList<OverlayMap> Maps => OverlayCatalogue.Maps;

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
    public static bool IsCurrent(OverlayHeader? header) => OverlayProtocol.IsCurrent(header);
    public static bool IsCurrent(ReadOnlySpan<byte> image) => OverlayProtocol.IsCurrent(image);
    public static string? Refusal(int table, ReadOnlySpan<byte> copy) => OverlaySettings.Refusal(table, copy);

    /// <summary>Whether every byte of an 8-byte copy of group 139..142 is within its limits (140's switch may be a CAN bit).</summary>
    private static bool WithinLimits(int table, ReadOnlySpan<byte> copy, IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> all)
    {
        var limits = all[table];
        for (var i = 0; i < 8; i++)
            if ((table != Blip || i != 0 || !OverlayV8.IsCanBit(copy[i])) && (copy[i] < limits[i].Low || copy[i] > limits[i].High))
                return false;
        return true;
    }
}
