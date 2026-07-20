using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

// Thresholds and texts are performance spec 5.
public class WarmUpMonitorTests
{
    private const string ColdCoolant =
        "Coolant 64 °C: engine not at operating temperature (80 °C). Do not use full throttle; results will not be valid.";

    private readonly WarmUpMonitor m = new();

    private static PerfFrame F(double seconds, double? coolant = 92, double? oil = 88, bool? overheat = null) =>
        new(TimeSpan.FromSeconds(seconds), Rpm: null, SpeedKmh: null, Gear: null, Pedal: null, Throttle: null,
            CoolantC: coolant, OilC: oil, IntakeC: null, PressureKpa: null, BatteryV: null,
            Ignition: [], IgnitionAvg: null, KnockRetard: [],
            VanosIn: null, VanosEx: null, MafKgh: null, LoadMgStk: null, AfrTarget: null,
            RonAdaptation: null, LongTermTrim1: null, LongTermTrim2: null,
            Clutch: null, Brake: null, OverheatLamp: overheat, CatalystHeating: null);

    private string[] Codes() => m.Banners.Select(b => b.Code).ToArray();

    [Fact]
    public void ColdCoolantIsDanger()
    {
        m.Push(F(0, coolant: 64, oil: 60));

        var b = Assert.Single(m.Banners, x => x.Code == "coolant-cold");
        Assert.Equal(WarmUpLevel.Danger, b.Level);
        Assert.Equal(ColdCoolant, b.Text);
        var oil = Assert.Single(m.Banners, x => x.Code == "oil-cold");
        Assert.Equal("Oil 60 °C: not at working temperature yet (80 °C).", oil.Text);
        Assert.Equal(WarmUpLevel.Caution, oil.Level);
        Assert.Equal("coolant-cold", m.Banners[0].Code);
        Assert.True(m.CoolantCold);
        Assert.True(m.OilCold);
        Assert.False(m.OilUnreported);
    }

    [Fact]
    public void WarmAtTheFirstReadingNeedsNoWait()
    {
        m.Push(F(0, coolant: 92, oil: 88));

        Assert.Empty(m.Banners);
        Assert.False(m.CoolantCold);
        Assert.False(m.OilCold);
    }

    [Fact]
    public void ColdClearsAfterTenSecondsAtEighty()
    {
        m.Push(F(0, coolant: 79, oil: 90));
        for (var tenths = 10; tenths <= 109; tenths++)
        {
            m.Push(F(tenths / 10.0, coolant: 80, oil: 90));
            Assert.True(m.CoolantCold, $"cold at {tenths / 10.0} s");
        }

        Assert.Equal("Coolant 80 °C: nearly at operating temperature. Do not use full throttle yet.", Assert.Single(m.Banners).Text);

        m.Push(F(11, coolant: 80, oil: 90));

        Assert.False(m.CoolantCold);
        Assert.Empty(m.Banners);
    }

    // While a reading at 80 °C or more is held for its 10 s, the banner keeps its level but no longer says "not at temperature"
    // under a reading above the target.
    [Fact]
    public void AReadingBeingHeldReadsAsNearlyThere()
    {
        m.Push(F(0, coolant: 79, oil: 70));
        m.Push(F(1, coolant: 81, oil: 88));

        var coolant = Assert.Single(m.Banners, b => b.Code == "coolant-cold");
        Assert.Equal((WarmUpLevel.Danger, "Coolant 81 °C: nearly at operating temperature. Do not use full throttle yet."), (coolant.Level, coolant.Text));
        var oil = Assert.Single(m.Banners, b => b.Code == "oil-cold");
        Assert.Equal((WarmUpLevel.Caution, "Oil 88 °C: nearly at working temperature, holding 10 s."), (oil.Level, oil.Text));
        Assert.True(m.CoolantCold);
        Assert.True(m.OilCold);

        // A dip below 80 °C ends the hold: the reading is below the target again.
        m.Push(F(2, coolant: 79.4, oil: 79.4));

        Assert.Equal(ColdCoolant.Replace("64", "79", StringComparison.Ordinal), Assert.Single(m.Banners, b => b.Code == "coolant-cold").Text);
        Assert.Equal("Oil 79 °C: not at working temperature yet (80 °C).", Assert.Single(m.Banners, b => b.Code == "oil-cold").Text);
    }

    [Fact]
    public void ADipBelowEightyRestartsTheTenSeconds()
    {
        m.Push(F(0, coolant: 79, oil: 70));
        m.Push(F(1, coolant: 80, oil: 80));
        m.Push(F(6, coolant: 79.5, oil: 79.5));
        m.Push(F(7, coolant: 81, oil: 81));
        m.Push(F(16.9, coolant: 81, oil: 81));

        Assert.True(m.CoolantCold);
        Assert.True(m.OilCold);

        m.Push(F(17, coolant: 81, oil: 81));

        Assert.False(m.CoolantCold);
        Assert.False(m.OilCold);
        Assert.Empty(m.Banners);
    }

    [Fact]
    public void HysteresisHoldsBetweenSeventyFiveAndEighty()
    {
        m.Push(F(0, coolant: 92, oil: 90));
        m.Push(F(1, coolant: 77, oil: 77));

        Assert.False(m.CoolantCold);
        Assert.False(m.OilCold);
        Assert.Empty(m.Banners);

        m.Push(F(2, coolant: 74, oil: 77));

        Assert.True(m.CoolantCold);
        Assert.False(m.OilCold);
        Assert.Equal(new[] { "coolant-cold" }, Codes());

        m.Push(F(3, coolant: 74, oil: 74));

        Assert.True(m.OilCold);
        Assert.Equal("Oil 74 °C: not at working temperature yet (80 °C).", Assert.Single(m.Banners, b => b.Code == "oil-cold").Text);
    }

    [Fact]
    public void OilThatNeverMovesIsNotReported()
    {
        for (var s = 0; s <= 600; s++)
        {
            m.Push(F(s, coolant: 50 + 22.0 * s / 600, oil: 40));
            if (s == 0)
            {
                Assert.Contains("oil-cold", Codes());
                Assert.False(m.OilUnreported);
            }
        }

        var b = Assert.Single(m.Banners, x => x.Code == "oil-unreported");
        Assert.Equal(WarmUpLevel.Info, b.Level);
        // A reading that never moves may be modelled, not measured.
        Assert.Equal("Oil temperature does not change: the ECU may not measure it.", b.Text);
        Assert.DoesNotContain("oil-cold", Codes());
        Assert.True(m.OilUnreported);
        Assert.False(m.OilCold);
        Assert.Equal(new[] { "coolant-cold", "oil-unreported" }, Codes());
    }

    [Fact]
    public void OilThatMovesIsReportedAgain()
    {
        // Oil drifts 1.5 °C while coolant climbs 20 °C: still frozen.
        for (var s = 0; s <= 400; s++)
        {
            m.Push(F(s, coolant: 50 + 20.0 * s / 400, oil: 40 + 1.5 * s / 400));
        }

        Assert.True(m.OilUnreported);

        m.Push(F(401, coolant: 70, oil: 43));

        Assert.False(m.OilUnreported);
        Assert.True(m.OilCold);
        Assert.Equal("Oil 43 °C: not at working temperature yet (80 °C).", Assert.Single(m.Banners, b => b.Code == "oil-cold").Text);
    }

    [Fact]
    public void OilRisingWithTheCoolantIsReported()
    {
        for (var s = 0; s <= 600; s++)
        {
            m.Push(F(s, coolant: 50 + 30.0 * s / 600, oil: 40 + 20.0 * s / 600));
        }

        Assert.False(m.OilUnreported);
        Assert.True(m.OilCold);
    }

    // On a warm engine the map thermostat swings the coolant 20 °C while the oil holds steady: that is no frozen reading. The
    // rise only counts below operating temperature, where it is a warm-up.
    [Fact]
    public void AWarmCoolantSwingDoesNotFreezeTheOil()
    {
        var s = 0;
        foreach (var coolant in new[] { 105.0, 96, 88, 88, 95, 102, 108, 108 })
        {
            for (var i = 0; i < 60; i++, s++)
            {
                m.Push(F(s, coolant: coolant, oil: 100 + 0.5 * Math.Sin(s / 10.0)));
            }
        }

        Assert.False(m.OilUnreported);
        Assert.DoesNotContain("oil-unreported", Codes());
    }

    [Fact]
    public void ImplausibleOilIsNotReported()
    {
        m.Push(F(0, oil: -48));

        Assert.True(m.OilUnreported);
        Assert.Equal(new[] { "oil-unreported" }, Codes());

        m.Push(F(1, oil: 150));
        Assert.True(m.OilUnreported);
        m.Push(F(2, oil: -40));
        Assert.True(m.OilUnreported);
        Assert.False(m.OilCold);

        m.Push(F(3, oil: 60));

        Assert.False(m.OilUnreported);
        Assert.True(m.OilCold);
        Assert.Equal(new[] { "oil-cold" }, Codes());
    }

    // A frame without an oil reading (the definition lacks it, or it did not decode) is "not reported", as Conditions keeps it.
    [Fact]
    public void MissingOilIsNotReported()
    {
        m.Push(F(0, coolant: 92, oil: null));

        Assert.True(m.OilUnreported);
        Assert.Equal(new[] { "oil-unreported" }, Codes());
        Assert.Equal("Oil temperature not reported by this ECU.", Assert.Single(m.Banners).Text);
    }

    // A missing coolant reading changes nothing: the state and its text stay as the last reading left them.
    [Fact]
    public void MissingCoolantKeepsTheLastState()
    {
        m.Push(F(0, coolant: 64, oil: 90));
        m.Push(F(1, coolant: null, oil: 90));

        Assert.True(m.CoolantCold);
        Assert.Equal(ColdCoolant, Assert.Single(m.Banners).Text);

        var fresh = new WarmUpMonitor();
        fresh.Push(F(0, coolant: null, oil: 90));
        Assert.False(fresh.CoolantCold);
        Assert.Empty(fresh.Banners);
    }

    [Fact]
    public void HotAndOverheat()
    {
        m.Push(F(0, coolant: 117, oil: 90));

        var hot = Assert.Single(m.Banners);
        // What happened, then what to do.
        Assert.Equal(("coolant-hot", WarmUpLevel.Caution, "Coolant 117 °C: running hot. Ease off and let it idle to cool."), (hot.Code, hot.Level, hot.Text));

        m.Push(F(1, coolant: 112, oil: 90));
        Assert.Equal(new[] { "coolant-hot" }, Codes());
        m.Push(F(2, coolant: 109.9, oil: 90));
        Assert.Empty(m.Banners);

        m.Push(F(3, oil: 132));
        Assert.Equal("Oil 132 °C: running hot. Ease off and let it idle to cool.", Assert.Single(m.Banners, b => b.Code == "oil-hot").Text);
        m.Push(F(4, oil: 126));
        Assert.Equal(new[] { "oil-hot" }, Codes());
        m.Push(F(5, oil: 124));
        Assert.Empty(m.Banners);

        m.Push(F(6, coolant: 117, overheat: true));
        var overheat = m.Banners[0];
        Assert.Equal(("overheat", WarmUpLevel.Danger, "The ECU reports engine overheating."), (overheat.Code, overheat.Level, overheat.Text));
        Assert.Equal(new[] { "overheat", "coolant-hot" }, Codes());

        // An unknown bit keeps the lamp as it was; only a cleared bit clears it.
        m.Push(F(7, coolant: 117, overheat: null));
        Assert.Contains("overheat", Codes());
        m.Push(F(8, coolant: 117, overheat: false));
        Assert.DoesNotContain("overheat", Codes());
    }

    [Fact]
    public void DangerComesFirstThenCautionThenInfo()
    {
        m.Push(F(0, coolant: 64, oil: -48, overheat: true));

        Assert.Equal(new[] { WarmUpLevel.Danger, WarmUpLevel.Danger, WarmUpLevel.Info }, m.Banners.Select(b => b.Level));
        Assert.Equal(new[] { "overheat", "coolant-cold", "oil-unreported" }, Codes());

        m.Push(F(1, coolant: 64, oil: 140, overheat: false));

        Assert.Equal(new[] { "coolant-cold", "oil-hot" }, Codes());
    }

    [Fact]
    public void ChangedFiresOnlyOnChange()
    {
        var raised = 0;
        m.Changed += (_, _) => raised++;

        m.Push(F(0, coolant: 64, oil: 90));
        m.Push(F(0.1, coolant: 64, oil: 90));
        Assert.Equal(1, raised);

        // The text carries the reading in whole degrees: 64.2 reads as 64, 65 is a new text.
        m.Push(F(0.2, coolant: 64.2, oil: 90));
        Assert.Equal(1, raised);
        m.Push(F(0.3, coolant: 65, oil: 90));
        Assert.Equal(2, raised);

        m.Push(F(0.4, coolant: 65.3, oil: 90));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var raised = 0;
        m.Push(F(0, coolant: 64, oil: -48, overheat: true));
        m.Changed += (_, _) => raised++;

        m.Reset();

        Assert.Equal(1, raised);
        Assert.Empty(m.Banners);
        Assert.False(m.CoolantCold);
        Assert.False(m.OilUnreported);

        m.Reset();
        Assert.Equal(1, raised);

        // After a reset the next reading is a first reading again: warm needs no wait, even with the clock back at zero.
        m.Push(F(0, coolant: 92, oil: 88));
        Assert.Empty(m.Banners);
        Assert.Equal(1, raised);
    }
}
