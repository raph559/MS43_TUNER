using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// The overlay the app talks to, V10.4: V9 plus the downshift blip (140, its gears and tap 141, its gear ratios 142), each
/// 8 bytes after pops' flashed defaults; 139 is reserved. The pops AFR target is the real AFR × 10; 141/6 is the blip lead
/// and 141/7 the spark above the blip's target. Any other version is refused.
/// </summary>
public sealed class CurrentOverlayTests
{

    private static OverlayScalar Field(int table, int offset) =>
        OverlayV10.Maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset);

    private static double Shown(OverlayScalarDefinition definition, byte raw) =>
        ExpressionEvaluator.Evaluate(definition.Equation, new Dictionary<string, double> { ["X"] = raw });

    public static TheoryData<int, int> OlderOverlays => new()
    {
        { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }, { 6, 0 }, { 7, 0 }, { 8, 0 }, { 8, 5 }, { 9, 0 }, { 10, 0 }, { 10, 1 }, { 10, 2 }, { 10, 3 }, { 11, 0 }, { 10, 5 },
    };

    [Fact]
    public void NoSettingMentionsTheStabs()
    {
        var fields = OverlayV10.Maps.Where(m => m.ScalarGroup is not null).SelectMany(m => m.ScalarGroup!.Fields);
        Assert.DoesNotContain(fields, f => f.Label.Contains("stab", StringComparison.OrdinalIgnoreCase)
            || f.Definition?.Description.Contains("stab", StringComparison.OrdinalIgnoreCase) == true);
    }

    public static TheoryData<int, int, string, double, double> Ranges => new()
    {
        // table, byte, units, lowest and highest physical value: the firmware's limits through each equation
        { 138, 3, "AFR", 5.0, 14.7 }, { 138, 6, "°TPS", 0, 20.0 },
        { 140, 2, "km/h", 10, 255 }, { 140, 3, "rpm", 2016, 5984 }, { 140, 4, "s", 0.3, 3.0 }, { 140, 5, "°TPS", 0, 30.0 },
        { 140, 6, "°TPS", 0, 30.0 }, { 140, 7, "°BTDC", 0.0, 30.0 },
        { 141, 4, "°PVS", 5.15625, 39.84375 }, { 141, 5, "ms", 100, 600 }, { 141, 6, "ms", 0, 300 }, { 141, 7, "°BTDC", -23.625, 30.0 },
        { 142, 0, "rpm/(km/h)", 90, 127.5 }, { 142, 1, "rpm/(km/h)", 50, 80 }, { 142, 2, "rpm/(km/h)", 35, 55 },
        { 142, 3, "rpm/(km/h)", 25, 42.5 },
    };

    [Theory, MemberData(nameof(Ranges))]
    public void EachSettingShowsTheFirmwaresLimitsInItsUnits(int table, int offset, string units, double low, double high)
    {
        var definition = Field(table, offset).Definition!;
        Assert.Equal((units, low, high), (definition.Units, definition.RangeLow, definition.RangeHigh));
    }

    [Fact]
    public void TheBlipsChoicesAreNamed()
    {
        static string Description(int table, int offset) => Field(table, offset).Definition!.Description;
        foreach (var (value, label, _) in OverlayV8.Triggers) Assert.Contains($"\n{value}: {label}", Description(140, 0));
        Assert.EndsWith("1: While braking\n2: On a pedal tap\n3: Braking or tap", Description(140, 1));
        for (var gear = 0; gear < 4; gear++) Assert.EndsWith("\n0: Off\n1: On", Description(141, gear));
        Assert.Contains("tap only", Description(141, 3));
    }

    public static TheoryData<int, int, byte> OutOfRange => new()
    {
        { 138, 3, 49 }, { 138, 3, 148 }, { 138, 3, 210 }, { 138, 6, 201 },
        { 139, 0, 1 }, { 139, 1, 25 }, { 139, 4, 15 },
        { 140, 0, 3 }, { 140, 0, 0x7F }, { 140, 0, 0xA0 }, { 140, 0, 0xFF }, { 140, 1, 0 }, { 140, 1, 4 },
        { 140, 2, 9 }, { 140, 3, 62 }, { 140, 3, 188 }, { 140, 4, 2 }, { 140, 4, 31 }, { 140, 5, 151 },
        { 140, 6, 151 }, { 140, 7, 62 }, { 140, 7, 144 },
        { 141, 0, 2 }, { 141, 1, 2 }, { 141, 2, 2 }, { 141, 3, 2 }, { 141, 4, 10 }, { 141, 4, 86 }, { 141, 5, 9 },
        { 141, 5, 61 }, { 141, 6, 31 }, { 141, 7, 144 },
        { 142, 0, 179 }, { 142, 1, 99 }, { 142, 1, 161 }, { 142, 2, 69 }, { 142, 2, 111 }, { 142, 3, 49 }, { 142, 3, 86 },
        { 142, 4, 1 }, { 142, 7, 1 },
    };

    public static TheoryData<int, int, byte> AtTheLimits => new()
    {
        { 138, 3, 50 }, { 138, 3, 70 }, { 138, 3, 147 }, { 138, 6, 200 }, { 138, 0, 0x97 },
        { 139, 0, 0 },
        { 140, 0, 0 }, { 140, 0, 2 }, { 140, 0, 0x80 }, { 140, 0, 0x97 }, { 140, 0, 0x9F }, { 140, 1, 1 }, { 140, 1, 3 },
        { 140, 2, 10 }, { 140, 2, 255 }, { 140, 3, 63 }, { 140, 3, 187 }, { 140, 4, 3 }, { 140, 4, 30 }, { 140, 5, 150 },
        { 140, 6, 0 }, { 140, 7, 63 }, { 140, 7, 143 },
        { 141, 0, 0 }, { 141, 3, 1 }, { 141, 4, 11 }, { 141, 4, 85 }, { 141, 5, 10 }, { 141, 5, 60 },
        { 141, 6, 0 }, { 141, 6, 30 }, { 141, 7, 0 }, { 141, 7, 143 },
        { 142, 0, 180 }, { 142, 0, 255 }, { 142, 1, 100 }, { 142, 1, 160 }, { 142, 2, 70 }, { 142, 2, 110 },
        { 142, 3, 50 }, { 142, 3, 85 },
    };

    public static TheoryData<int[]> FittingSets => new() { new[] { 1, 2, 3, 4 }, new[] { 0, 1, 3, 4 }, new[] { 0, 1, 2, 4 }, new[] { 0, 1, 2, 3 } };
}
