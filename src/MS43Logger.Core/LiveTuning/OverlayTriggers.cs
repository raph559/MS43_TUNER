namespace MS43Logger.Core.LiveTuning;

public static class OverlayTriggers
{
    /// <summary>"A/C on": bit 7 of 615h byte 0 is 1 while the A/C button is on.</summary>
    public const byte AirConOn = 0x97;

    /// <summary>"ASC off": bit 2 of 153h byte 0 is 1 while ASC is switched off.</summary>
    public const byte AscOff = 0x82;

    /// <summary>Named pops and blip switch choices; the firmware also accepts other CAN bits in 80h..9Fh.</summary>
    public static IReadOnlyList<(byte Value, string Label, string Short)> Pops { get; } = Array.AsReadOnly(new (byte, string, string)[]
    {
        (0, "Off", "Off"), (1, "Always on", "On"), (2, "Cruise control armed", "Cruise armed"), (AscOff, "ASC off", "ASC off"),
        (AirConOn, "A/C on", "A/C on"),
    });

    private static readonly (byte, string, string)[] ClutchTriggers =
    [
        (0, "Off", "Off"), (1, "Clutch pressed", "Clutch"), (2, "Cruise light on + clutch", "Cruise + clutch"),
        (AscOff, "ASC off + clutch", "ASC off + clutch"), (AirConOn, "A/C on + clutch", "A/C + clutch"),
    ];

    // Anti-lag has no cruise-light trigger: stock RAL is blocked while the cruise main switch is on.
    private static readonly (byte, string, string)[] ButtonTriggers =
    [
        (0, "Off", "Off"), (1, "\u201c\u2212\u201d pressed", "\u2212"),
        (AscOff, "ASC off + \u201c\u2212\u201d", "ASC off + \u2212"), (AirConOn, "A/C on + \u201c\u2212\u201d", "A/C + \u2212"),
    ];

    /// <summary>Named LC/NLS/RAL triggers; empty for other IDs. The clutch or "\u2212" button still fires the feature.</summary>
    public static IReadOnlyList<(byte Value, string Label, string Short)> For(int table) => table switch
    {
        OverlaySettings.LaunchControl or OverlaySettings.NoLiftShift => ClutchTriggers,
        OverlaySettings.RollingAntiLag => ButtonTriggers,
        _ => [],
    };

    /// <summary>A switch value 80h..9Fh is a bit of the first byte of the ASC (153h) or A/C (615h) frame.</summary>
    public static bool IsCanBit(byte value) => (value & 0xE0) == 0x80;
}
