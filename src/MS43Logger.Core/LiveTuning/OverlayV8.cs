namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility catalogue and limits for the original pops encoding.</summary>
public static class OverlayV8
{
    public const int Pops = OverlaySettings.Pops, DefaultsAddress = OverlaySettings.DefaultsAddress;
    public const string Category = OverlaySettings.PopsCategory;
    public const byte AirConOn = OverlayTriggers.AirConOn, AscOff = OverlayTriggers.AscOff;
    public static IReadOnlyList<(byte Value, string Label, string Short)> Triggers => OverlayTriggers.Pops;

    /// <summary>Each byte's lowest and highest value, as the firmware enforces them.</summary>
    public static IReadOnlyList<(byte Low, byte High)> Limits { get; } = Array.AsReadOnly(new (byte, byte)[]
    {
        (0, 2), (1, 50), (0, 255), (128, 210), (50, 219), (144, 255), (0, 50), (0, 255),
    });

    public static OverlayScalarGroup PopsGroup { get; } = OverlaySettings.PopsGroup with
    {
        Fields = OverlaySettings.PopsGroup.Fields.Select(field => field.Offset switch
        {
            2 => field with
            {
                Definition = field.Definition! with
                {
                    Description = "The fixed spark angle during pops, whatever the engine speed and load. Negative is after top dead centre; -23.6 is the latest the firmware allows.",
                    RangeHigh = 72.0,
                },
            },
            3 => field with
            {
                Label = "AFR target",
                Definition = new("Pops AFR target",
                    "The mixture during pops (the firmware's target, lambda = 1.5 - X/256): the richer of this and the stock target is used, and from 1,600 rpm the lambda control is off.",
                    "AFR", "22.05-0.057421875*X", 9.99, 14.7, 1, Category),
            },
            6 => field with
            {
                Definition = field.Definition! with
                {
                    Description = "Added to the stock throttle setpoint during pops, at most 5.0 °TPS. Pops stops as soon as the engine speed rises 160 rpm, so the car cannot keep pulling.",
                    RangeHigh = 5.0,
                },
            },
            _ => field,
        }).ToArray(),
    };

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV7.Maps
        .Append(new OverlayMap(Pops, "pops", "Pops & bangs", DefaultsAddress, 8, false, PopsGroup)).ToArray());

    public static bool IsCanBit(byte value) => OverlayTriggers.IsCanBit(value);
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash) => table == Pops
        ? OverlaySettings.ValidatePops(copy, Limits) : OverlaySettings.ValidateStockLaunchCopy(table, copy, flash);
}
