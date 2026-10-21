using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

public class RunMathTests
{
    private static double[] Times(int count, double rate = 30, double from = 0) =>
        Enumerable.Range(0, count).Select(i => from + i / rate).ToArray();

    // Every "time at X" is interpolated between the two samples around X (performance spec 4.6).
    [Fact]
    public void CrossingIsInterpolated()
    {
        Assert.Equal(1.5, RunMath.CrossingTime([0, 1, 2], [0, 10, 20], 15)!.Value, 12);
        Assert.Null(RunMath.CrossingTime([0, 1, 2], [0, 10, 20], 25));
        Assert.Equal(1.0, RunMath.CrossingTime([0, 1, 2, 3], [0, 10, 10, 20], 10)!.Value, 12);
    }

    [Fact]
    public void CrossingIsTheFirstUpwardOne()
    {
        // Starts above the level, falls below, then rises through it at 2.5.
        Assert.Equal(2.5, RunMath.CrossingTime([0, 1, 2, 3], [12, 5, 5, 15], 10)!.Value, 12);
        // A level only touched from above is never crossed upward.
        Assert.Null(RunMath.CrossingTime([0, 1], [20, 10], 10));
        Assert.Null(RunMath.CrossingTime([], [], 10));
        // A missing reading is stepped over: the crossing is between the samples either side of it.
        Assert.Equal(1.5, RunMath.CrossingTime([0, 1, 2], [0, double.NaN, 20], 15)!.Value, 12);
        // Two samples at the same instant give that instant, never a division by zero.
        Assert.Equal(1.0, RunMath.CrossingTime([0, 1, 1, 2], [0, 5, 15, 20], 10)!.Value, 12);
        Assert.Throws<ArgumentException>(() => RunMath.CrossingTime([0, 1], [0], 1));
    }

    // Time zero of a standing start: a straight line through 3 to 20 km/h, taken at 0 km/h (performance spec 6.4).
    [Fact]
    public void StartIsBackExtrapolated()
    {
        var t = Enumerable.Range(5, 26).Select(i => i / 10.0).ToArray();
        var v = t.Select(x => 10 * (x - 0.4)).ToArray();

        Assert.Equal(0.4, RunMath.BackExtrapolatedStart(t, v)!.Value, 9);
        Assert.Null(RunMath.BackExtrapolatedStart([1, 2], [5, 10]));
        Assert.Null(RunMath.BackExtrapolatedStart([1, 2, 3], [15, 10, 5]));
        Assert.Null(RunMath.BackExtrapolatedStart([1, 1, 1], [5, 10, 15]));
    }

    [Fact]
    public void StartUsesOnlyTheFirstClimbThroughTheBand()
    {
        // The car reaches 30 km/h, slows back through the band later: those late points must not bend the line.
        var t = new List<double>();
        var v = new List<double>();
        for (var i = 5; i <= 34; i++)
        {
            t.Add(i / 10.0);
            v.Add(10 * (i / 10.0 - 0.4));
        }

        for (var i = 1; i <= 10; i++)
        {
            t.Add(3.4 + i);
            v.Add(30 - 2.5 * i);
        }

        Assert.Equal(0.4, RunMath.BackExtrapolatedStart(t, v)!.Value, 9);
    }

    [Fact]
    public void DerivativeOfAQuadraticIsExact()
    {
        var t = Times(61);
        var v = t.Select(x => 3 * x * x + 2 * x).ToArray();

        var d = RunMath.Derivative(t, v);

        Assert.Equal(61, d.Length);
        Assert.Equal(8, d[30], 6);
        // Near the ends the window is one-sided but still fits the quadratic exactly.
        Assert.All(Enumerable.Range(0, 61), i => Assert.Equal(6 * t[i] + 2, d[i], 6));
    }

    // Bursty timing (a retry gap, two samples at one instant, a stamp out of order) gives finite values or NaN, never a throw
    // or an infinity (performance plan, review focus 4).
    [Fact]
    public void DerivativeSurvivesDuplicateTimes()
    {
        var t = new List<double>(Times(30));
        t.Add(t[^1]);
        t.AddRange(Times(30, from: t[^1] + 0.4));
        t.Insert(10, t[12]);
        var v = t.Select(x => 500 * x).ToArray();

        var d = RunMath.Derivative(t, v);

        Assert.Equal(t.Count, d.Length);
        Assert.All(d, x => Assert.False(double.IsInfinity(x)));
        Assert.All(d.Where(double.IsFinite), x => Assert.Equal(500, x, 6));
        Assert.True(double.IsFinite(d[5]));
        Assert.True(double.IsFinite(d[^5]));

        // Under five distinct times in the window: NaN.
        var sparse = RunMath.Derivative([0, 1, 2, 3, 4, 4, 4], [0, 1, 2, 3, 4, 4, 4]);
        Assert.All(sparse, x => Assert.True(double.IsNaN(x)));
        Assert.True(double.IsNaN(RunMath.Derivative([0, 0.1, 0.2, 0.3, 0.4], [0, double.NaN, 2, 3, 4])[1]));
        Assert.Empty(RunMath.Derivative([], []));
    }

    // Rolling circumference is 0.97 × the free circumference (plan Task 5): 205/55 R16 is 631.9 mm across, 225/45 R17 634.3 mm.
    [Fact]
    public void TyreCircumference()
    {
        Assert.Equal(1.926, RunMath.TyreCircumference("205/55 R16")!.Value, 0.005);
        Assert.Equal(1.933, RunMath.TyreCircumference("225/45 R17")!.Value, 0.005);
        Assert.Equal(RunMath.TyreCircumference("205/55 R16"), RunMath.TyreCircumference(" 205/55R16 "));
        Assert.Equal(RunMath.TyreCircumference("225/45 R17"), RunMath.TyreCircumference("225/45 zr17 91W"));
        Assert.Null(RunMath.TyreCircumference("junk"));
        Assert.Null(RunMath.TyreCircumference(""));
        Assert.Null(RunMath.TyreCircumference("205/55 R"));
        Assert.Null(RunMath.TyreCircumference("0/55 R16"));
    }

    [Fact]
    public void AirDensityAtSeaLevel() => Assert.Equal(1.225, RunMath.AirDensity(101.325, 15), 0.001);

    private static readonly CarValues Bare = new(1000, "205/55 R16", false, WheelInertia: 0, EngineInertia: 0, CdA: 0, Crr: 0);

    private static (double[] T, double[] Rpm) Ramp() // 2000 to 6000 rpm over 8 s at 30/s
    {
        var t = Times(241);
        return (t, t.Select(x => 2000 + 500 * x).ToArray());
    }

    [Fact]
    public void PowerWithoutLossesIsMassTimesAccelTimesSpeed()
    {
        const double k = 0.0223;
        var (t, rpm) = Ramp();
        const int mid = 120;
        Assert.Equal(4000, rpm[mid], 9);
        var a = k / 3.6 * 500;
        var v = k * 4000 / 3.6;

        var p = RunMath.Power(t, rpm, k, Bare, 1.2);

        Assert.Equal(1000 * a * v / 1000, p.PowerKw[mid], 2);
        Assert.Equal(p.PowerKw[mid] * 1000 * 60 / (2 * Math.PI * 4000), p.TorqueNm[mid], 6);
        Assert.Equal(rpm.Length, p.PowerKw.Length);
        Assert.Equal(rpm.Length, p.TorqueNm.Length);
    }

    [Fact]
    public void SteadySpeedPowerIsDragAndRolling()
    {
        const double k = 0.0298;
        var t = Times(61);
        var rpm = t.Select(_ => 3500.0).ToArray();
        var car = Bare with { CdA = 0.60, Crr = 0.011 };
        var v = k * 3500 / 3.6;

        var p = RunMath.Power(t, rpm, k, car, 1.2);

        var force = 0.5 * 1.2 * 0.60 * v * v + 0.011 * 1000 * 9.81;
        Assert.Equal(force * v / 1000, p.PowerKw[30], 6);
    }

    [Fact]
    public void InertiaAddsToTheMass()
    {
        const double k = 0.0223;
        var (t, rpm) = Ramp();
        var car = Bare with { WheelInertia = 4.5, EngineInertia = 0.20 };
        var r = RunMath.TyreCircumference(car.TyreSize)!.Value / (2 * Math.PI);
        var engine = 2 * Math.PI / 60 * 3.6 / k;
        var effective = 1000 + 4.5 / (r * r) + 0.20 * engine * engine;

        var bare = RunMath.Power(t, rpm, k, Bare, 1.2);
        var full = RunMath.Power(t, rpm, k, car, 1.2);

        Assert.Equal(effective / 1000, full.PowerKw[120] / bare.PowerKw[120], 9);
    }

    // An unreadable tyre or ratio gives no power at all, never zeros.
    [Fact]
    public void PowerIsUnknownWithoutATyreOrARatio()
    {
        var (t, rpm) = Ramp();

        Assert.All(RunMath.Power(t, rpm, 0.0223, Bare with { TyreSize = "junk" }, 1.2).PowerKw, x => Assert.True(double.IsNaN(x)));
        Assert.All(RunMath.Power(t, rpm, 0, Bare, 1.2).TorqueNm, x => Assert.True(double.IsNaN(x)));
        var zero = RunMath.Power([0, 0.1, 0.2, 0.3, 0.4, 0.5], [0, 0, 0, 0, 0, 0], 0.0223, Bare, 1.2);
        Assert.All(zero.TorqueNm, x => Assert.True(double.IsNaN(x)));
    }

    [Fact]
    public void BinsAverageOrTakeTheMax()
    {
        double[] rpm = [1990, 2010, 2240, 2260];
        double[] values = [1, 3, 5, 9];

        Assert.Equal(new[] { (2000d, 2d), (2250d, 7d) }, RunMath.Bin(rpm, values, 2000, 2250));
        Assert.Equal(new[] { (2000d, 3d), (2250d, 9d) }, RunMath.Bin(rpm, values, 2000, 2250, max: true));
    }

    [Fact]
    public void BinsSkipEmptyAndOutsideAndMissing()
    {
        double[] rpm = [1700, 1990, 2124.9, 2125, 2760, double.NaN, 2010];
        double[] values = [100, 1, 3, 5, 7, 50, double.NaN];

        // Bins are centred on 2000, 2250, 2500, 2750: 2125 opens the second, 2500 is empty, 1700 lies outside.
        Assert.Equal(new[] { (2000d, 2d), (2250d, 5d), (2750d, 7d) }, RunMath.Bin(rpm, values, 2000, 2750));
        Assert.Empty(RunMath.Bin(rpm, values, 3000, 2000));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunMath.Bin(rpm, values, 2000, 3000, width: 0));
    }

    // A band that is not a whole number of bins wide keeps its top: readings past the last bin's upper edge, up to the band's
    // end, fold into the last bin rather than being dropped (minor review, finding 1).
    [Fact]
    public void TheTopOfTheBandFoldsIntoTheLastBin()
    {
        // 2000 to 2400 rpm: bins centred on 2000 and 2250, the second reaching 2375; 2380 and 2400 belong to it, 2410 does not.
        double[] rpm = [2250, 2380, 2400, 2410];
        double[] values = [1, 3, 5, 100];

        Assert.Equal(new[] { (2250d, 3d) }, RunMath.Bin(rpm, values, 2000, 2400));
        Assert.Equal(new[] { (2250d, 5d) }, RunMath.Bin(rpm, values, 2000, 2400, max: true));
    }

    [Fact]
    public void MedianOfOddEvenAndEmpty()
    {
        Assert.Equal(3, RunMath.Median([5, 1, 3]));
        Assert.Equal(2.5, RunMath.Median([4, 1, 3, 2]));
        Assert.Equal(2, RunMath.Median([double.NaN, 2]));
        Assert.True(double.IsNaN(RunMath.Median([])));
    }
}
