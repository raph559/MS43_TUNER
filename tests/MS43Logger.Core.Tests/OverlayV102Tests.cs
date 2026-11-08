using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// Protocol 10.2 (header minor 2): V10.1 with the pops AFR target as the real AFR × 10 in petrol AFR (138/3, 50..147, so
/// 5.0..14.7) and the blip's cap angle above its target (141/7, 0..143, so −23.625..+30.0 °BTDC). A V10.1 ECU (minor 1)
/// and a V10 ECU (minor 0) keep their own rules. The rules are the firmware's (sim OverlayV10Tests).
/// </summary>
public sealed class OverlayV102Tests
{
    private static FakeOverlayV4Ecu Ecu(int minor = 2)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        (minor >= 2 ? V10Groups.DefaultsV102 : V10Groups.DefaultsV101).CopyTo(image, 0x4FF80);
        return new(image, OverlayV10.Version) { Minor = minor };
    }

    private static byte[] With(int table, byte[] defaults, params (int Index, byte Value)[] changes)
    {
        var copy = defaults.AsSpan(8 * (table - OverlayV8.Pops), 8).ToArray();
        foreach (var (index, value) in changes) copy[index] = value;
        return copy;
    }

    private static OverlayScalar Field(IReadOnlyList<OverlayMap> maps, int table, int offset) =>
        maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset);

    private static double Shown(OverlayScalarDefinition definition, byte raw) =>
        ExpressionEvaluator.Evaluate(definition.Equation, new Dictionary<string, double> { ["X"] = raw });

    [Fact]
    public void AV102EcuGetsV102sCatalogue()
    {
        var detection = new OverlayClient(Ecu()).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal("10.2", detection.Header!.Label);
        Assert.Equal(OverlayV10.MapsFor(2), detection.Catalogue);
        Assert.NotEqual(OverlayV10.MapsFor(1), detection.Catalogue);
        Assert.Equal(OverlayV10.MapsFor(2), OverlayV9.MapsFor(10, 2));
    }

    [Fact]
    public void MinorTwoShowsTheRealAfrFrom5To14_7()
    {
        var field = Field(OverlayV10.MapsFor(2), OverlayV8.Pops, 3);
        var definition = field.Definition!;
        Assert.Equal("AFR target (petrol)", field.Label);
        Assert.Equal(("AFR", 5.0, 14.7, 1), (definition.Units, definition.RangeLow, definition.RangeHigh, definition.Decimals));
        Assert.Equal(7.0, Shown(definition, 70), 9);
        Assert.Equal(5.0, Shown(definition, 50), 9);
        Assert.Equal(14.7, Shown(definition, 147), 9);
    }

    [Fact]
    public void MinorTwoShowsTheCapSpark()
    {
        var field = Field(OverlayV10.MapsFor(2), OverlayV10.BlipGears, 7);
        var definition = field.Definition!;
        Assert.Equal("Spark above the target", field.Label);
        Assert.Equal(("°BTDC", -23.625, 30.0, OverlayV10.BlipCategory), (definition.Units, definition.RangeLow, definition.RangeHigh, definition.Category));
        Assert.Equal(-23.625, Shown(definition, 0), 9);
        Assert.Equal(30.0, Shown(definition, 143), 9);
    }

    [Theory] // V10.1's firmware bytes are not real AFRs: 210 (shown 9.99 there) is refused on V10.2, as are 49 and 148
    [InlineData((byte)210, false)] [InlineData((byte)49, false)] [InlineData((byte)148, false)]
    [InlineData((byte)50, true)] [InlineData((byte)70, true)] [InlineData((byte)147, true)]
    public void MinorTwoRefusesAV101AfrByte(byte afr, bool accepted)
    {
        var copy = With(OverlayV8.Pops, V10Groups.DefaultsV102, (3, afr));
        var flash = With(OverlayV8.Pops, V10Groups.DefaultsV102);
        Assert.Equal(accepted ? null : OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV8.Pops, copy, flash, 2));
    }

    [Theory]
    [InlineData((byte)0, true)] [InlineData((byte)143, true)] [InlineData((byte)144, false)]
    public void MinorTwoTakesTheCapAngleWithinItsLimits(byte cap, bool accepted)
    {
        var copy = With(OverlayV10.BlipGears, V10Groups.DefaultsV102, (7, cap));
        var flash = With(OverlayV10.BlipGears, V10Groups.DefaultsV102);
        Assert.Equal(accepted ? null : OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.BlipGears, copy, flash, 2));
    }

    [Fact] // a V10.1 ECU: the firmware's AFR encoding down to 7.40, 141/7 still reserved
    public void MinorOneKeepsTheFirmwareEncoding()
    {
        var afr = Field(OverlayV10.MapsFor(1), OverlayV8.Pops, 3).Definition!;
        Assert.Equal((7.40, 14.7), (afr.RangeLow, afr.RangeHigh));
        Assert.Equal(9.99, Shown(afr, 210), 2);
        Assert.DoesNotContain(OverlayV10.MapsFor(1)[OverlayV10.BlipGears].ScalarGroup!.Fields, f => f.Offset == 7);
        var pops = With(OverlayV8.Pops, V10Groups.DefaultsV101, (3, 210));
        Assert.Null(OverlayV10.ValidateCopy(OverlayV8.Pops, pops, With(OverlayV8.Pops, V10Groups.DefaultsV101), 1));
        var gears = With(OverlayV10.BlipGears, V10Groups.DefaultsV101, (7, 1));
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.BlipGears, gears, With(OverlayV10.BlipGears, V10Groups.DefaultsV101), 1));
    }
}
