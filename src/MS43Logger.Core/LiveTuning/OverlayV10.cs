namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// Protocol 10: V9 plus the pops stabs (group 139) and the downshift blip (140, with its gears and tap in 141 and its gear
/// ratios in 142). Each group is 8 bytes, its flashed defaults after pops' at 0C:FF88. V10's state takes pool A's bytes
/// from 668, so allocations end there and the status reports 896 free. V10.1 (header minor 1) adds 139/4, the rise
/// allowed once stabbing, and 141/6, the blip lead, and takes the pops AFR target down to 7.4. V10.2 (minor 2) makes the
/// pops AFR target the real AFR × 10 in petrol AFR (5.0..14.7) and adds 141/7, the blip's spark above its target.
/// </summary>
public static class OverlayV10
{
    public const int Version = 10, PoolABytes = 668, CopyBytes = 896, Stabs = 139, Blip = 140, BlipGears = 141, BlipRatios = 142;

    /// <summary>The header minor of V10.1, whose rules and settings <see cref="MapsFor"/> and <see cref="ValidateCopy"/> follow.</summary>
    public const int V101Minor = 1;

    /// <summary>The header minor of V10.2: the real pops AFR (138/3) and the blip's spark above its target (141/7).</summary>
    public const int V102Minor = 2;
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

    /// <summary>V10.2's limits: V10.1's, with 141/7 the spark above the blip's target (0..143, −23.625..+30.0 °BTDC).</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsV102 { get; } = LimitsV101.ToDictionary(
        pair => pair.Key, pair => (IReadOnlyList<(byte Low, byte High)>)Array.AsReadOnly(pair.Value.Select((limit, i) =>
            (pair.Key, i) switch { (BlipGears, CapSpark) => ((byte)0, (byte)143), _ => limit }).ToArray()));

    /// <summary>The limits of groups 139..142 for an overlay of this minor: V10.2's from minor 2, V10.1's from minor 1.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> LimitsFor(int minor) =>
        minor >= V102Minor ? LimitsV102 : minor >= V101Minor ? LimitsV101 : Limits;

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

    public static OverlayScalarGroup BlipGearsGroupV101 { get; } = BlipGearsGroup with
    {
        Fields = BlipGearsGroup.Fields.Append(new("blip_lead", "Blip lead", BlipLead, new("Downshift blip lead",
            "How far ahead the blip looks while the engine speed rises: the blip opening gives way to the hold opening when the "
                + "engine speed, climbing as fast as it is now, would be within 192 rpm of the target this long from now. "
                + "Longer ends the rise earlier, against an overshoot; 0 waits for the engine speed itself, as V10 did.",
            "ms", "10*X", 0, 300, 0, BlipCategory))).ToArray(),
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

    private static OverlayScalarDefinition Downshift(string shift, string description) =>
        new($"Blip on {shift}", description + "\n0: Off\n1: On", "-", "X", 0, 1, 0, BlipCategory);

    private static OverlayScalar Ratio(int offset, string gear, double low, double high) =>
        new($"blip_ratio_{offset + 1}", $"rpm per km/h, {gear}", offset, new($"Engine speed per km/h in {gear}",
            $"The engine rpm per km/h of road speed in {gear}. A blip into {gear} aims at the road speed times this; check it against a log in {gear}.",
            "rpm/(km/h)", "0.5*X", low, high, 1, BlipCategory));
}
