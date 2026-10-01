using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V9 widens the pops throttle opening to 20.0 °TPS (owner, 2026-10-29); V8 keeps 5.0.</summary>
public sealed class OverlayV9PopsTests
{
    private static readonly byte[] On = [1, 20, 23, 184, 50, 158, 0, 20];

    private static byte[] WithThrottle(byte value)
    {
        var copy = On.ToArray();
        copy[6] = value;
        return copy;
    }

    [Theory]
    [InlineData(200, true)] [InlineData(201, false)] [InlineData(51, true)] [InlineData(0, true)]
    public void V9TakesAThrottleOpeningUpTo200(byte value, bool accepted)
    {
        var reason = OverlayV9.ValidateFor(OverlayV9.Version, OverlayV8.Pops, WithThrottle(value), On);
        Assert.Equal(accepted, reason is null);
    }

    [Fact]
    public void V8StillRefusesAboveFifty()
    {
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(OverlayV8.Version, OverlayV8.Pops, WithThrottle(51), On));
    }

    [Fact]
    public void V9sPopsDefinitionGoesTo20DegreesTps()
    {
        var throttle = OverlayV9.Maps.Single(map => map.Index == OverlayV8.Pops).ScalarGroup!.Fields.Single(field => field.Offset == 6);
        Assert.Equal(20.0, throttle.Definition!.RangeHigh);
        Assert.Contains("at most 20.0 °TPS", throttle.Definition.Description);
        Assert.Equal(5.0, OverlayV8.PopsGroup.Fields.Single(field => field.Offset == 6).Definition!.RangeHigh);
    }

    [Fact]
    public void V9sCatalogueDiffersFromV8sOnlyInThePopsGroup()
    {
        Assert.Equal(OverlayV8.Maps.Count, OverlayV9.Maps.Count);
        foreach (var (v8, v9) in OverlayV8.Maps.Zip(OverlayV9.Maps))
            if (v8.Index == OverlayV8.Pops) Assert.Equal(v8 with { ScalarGroup = v9.ScalarGroup }, v9);
            else Assert.Equal(v8, v9);
    }

    [Fact]
    public void V9KeepsV8sOtherPopsLimits()
    {
        Assert.Equal(OverlayV8.Limits.Select((limit, i) => i == 6 ? ((byte)0, (byte)200) : limit), OverlayV9.PopsLimits);
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(OverlayV9.Version, OverlayV8.Pops, [1, 51, 23, 184, 50, 158, 0, 20], On));
    }
}
