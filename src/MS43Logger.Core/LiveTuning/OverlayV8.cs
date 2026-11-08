namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// Protocol 8: V7 plus pops on lift-off, settings group 138. Its flashed defaults sit in the overlay's own flash
/// (0C:FF80), not in the stock calibration, and pool A keeps its last four bytes for the pops state.
/// </summary>
public static class OverlayV8
{
    public const int Version = 8, Pops = 138, DefaultsAddress = 0xCFF80, PoolABytes = 700, CopyBytes = 928;
    public const string Category = "Pops & Bangs";

    /// <summary>"A/C on": bit 7 of 615h byte 0 is 1 while the A/C button is on (owner's car check, 2026-10-29).</summary>
    public const byte AirConOn = 0x97;

    /// <summary>"ASC off": bit 2 of 153h byte 0 is 1 while ASC is switched off (owner's car check, 2026-11-03).</summary>
    public const byte AscOff = 0x82;

    /// <summary>
    /// The triggers the switch offers by name, with their short form for lists; declared before <see cref="PopsGroup"/>,
    /// which reads them. The app offers only these; the firmware would take any 80h..9Fh bit.
    /// </summary>
    public static IReadOnlyList<(byte Value, string Label, string Short)> Triggers { get; } = Array.AsReadOnly(new (byte, string, string)[]
    {
        (0, "Off", "Off"), (1, "Always on", "On"), (2, "Cruise control armed", "Cruise armed"), (AscOff, "ASC off", "ASC off"),
        (AirConOn, "A/C on", "A/C on"),
    });

    /// <summary>Each byte's lowest and highest value, as the firmware enforces them.</summary>
    public static IReadOnlyList<(byte Low, byte High)> Limits { get; } = Array.AsReadOnly(new (byte, byte)[]
    {
        (0, 2), (1, 50), (0, 255), (128, 210), (50, 219), (144, 255), (0, 50), (0, 255),
    });

    public static OverlayScalarGroup PopsGroup { get; } = new("Pops & bangs", new OverlayScalar[]
    {
        new("pops_switch", "Switch", 0, new("Switch", SwitchDescription(), "-", "X", 0, 0x9F, 0, Category)),
        new("pops_duration", "Duration", 1, new("Pops duration",
            "How long the overrun fuel cut is held off after the moment the ECU would cut the fuel. At most 5.0 s.",
            "s", "0.1*X", 0.1, 5.0, 1, Category)),
        new("pops_ignition", "Ignition angle", 2, new("Pops ignition angle",
            "The fixed spark angle during pops, whatever the engine speed and load. Negative is after top dead centre; -23.6 is the latest the firmware allows.",
            "°BTDC", "0.375*X-23.625", -23.625, 72.0, 3, Category)),
        new("pops_afr", "AFR target", 3, new("Pops AFR target",
            "The mixture during pops (the firmware's target, lambda = 1.5 - X/256): the richer of this and the stock target is used, and from 1,600 rpm the lambda control is off.",
            "AFR", "22.05-0.057421875*X", 9.99, 14.7, 1, Category)),
        new("pops_rpm_min", "Minimum engine speed", 4, new("Pops minimum engine speed",
            "No pops below this engine speed; never below 1,600 rpm, where the lambda control would fight the rich mixture.",
            "rpm", "32*X", 1600, 7008, 0, Category)),
        new("pops_coolant_min", "Minimum coolant temperature", 5, new("Pops minimum coolant temperature",
            "No pops with a colder engine; never below 60 °C.",
            "°C", "0.75*X-48", 60, 143.25, 2, Category)),
        new("pops_throttle", "Throttle opening", 6, new("Pops throttle opening",
            "Added to the stock throttle setpoint during pops, at most 5.0 °TPS. Pops stops as soon as the engine speed rises 160 rpm, so the car cannot keep pulling.",
            "°TPS", "0.1*X", 0, 5.0, 1, Category)),
        new("pops_speed_min", "Minimum vehicle speed", 7, new("Pops minimum vehicle speed",
            "No pops below this road speed. The clutch, the pedal, LC, NLS and rolling anti-lag also end pops.",
            "km/h", "X", 0, 255, 0, Category)),
    }, "A change takes effect at the next lift-off. The live settings fall back to the flashed ones 3 s after the app goes quiet.");

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV7.Maps
        .Append(new OverlayMap(Pops, "pops", "Pops & bangs", DefaultsAddress, 8, false, PopsGroup)).ToArray());

    /// <summary>A switch value 80h..9Fh is a bit of the first byte of the ASC (153h) or A/C (615h) frame.</summary>
    public static bool IsCanBit(byte value) => (value & 0xE0) == 0x80;

    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table != Pops) return OverlayV6.ValidateCopy(table, copy, flash);
        if (copy.Length != 8) return OverlayReason.Range;
        for (var i = 0; i < 8; i++)
            if ((i != 0 || !IsCanBit(copy[i])) && (copy[i] < Limits[i].Low || copy[i] > Limits[i].High)) return OverlayReason.Range;
        return null;
    }

    /// <summary>The switch's choices, one "n: label" line per trigger, which the editor shows as a list.</summary>
    private static string SwitchDescription() => string.Join("\n",
        new[] { "When pops may happen after a lift-off. Cruise control armed: the main switch is on and no speed is held." }
            .Concat(Triggers.Select(trigger => $"{trigger.Value}: {trigger.Label}")));
}
