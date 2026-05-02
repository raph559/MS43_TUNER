using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class LiveCursorMathTests
{
    private static readonly long[] Rpm = { 800, 1000, 1250, 1500, 1750, 2200, 2500, 3000 };

    [Fact]
    public void BetweenBreakpointsGivesSixteenBitFraction() =>
        Assert.Equal(new AxisPosition(5, 61166 / 65536.0, 0), AxisSearch.Find(Rpm, 2480, true));

    [Fact]
    public void OnABreakpointFractionIsZero() => Assert.Equal(new AxisPosition(5, 0, 0), AxisSearch.Find(Rpm, 2200, true));

    [Fact]
    public void BelowFirstClampsLow() => Assert.Equal(new AxisPosition(0, 0, -1), AxisSearch.Find(Rpm, 500, true));

    [Fact]
    public void FirstBreakpointIsNotOffTheMap() => Assert.Equal(new AxisPosition(0, 0, 0), AxisSearch.Find(Rpm, 800, true));

    [Fact]
    public void AboveLastClampsHigh() => Assert.Equal(new AxisPosition(7, 0, 1), AxisSearch.Find(Rpm, 7200, true));

    [Fact]
    public void LastBreakpointIsTheLastIndex() => Assert.Equal(new AxisPosition(7, 0, 0), AxisSearch.Find(Rpm, 3000, true));

    [Fact]
    public void NotInterpolatingKeepsTheLowerIndex() =>
        Assert.Equal(new AxisPosition(3, 0, 0), AxisSearch.Find(new long[] { 28, 38, 55, 63, 78, 94 }, 77, false));

    [Fact]
    public void EqualNeighboursTakeTheUpperOne()
    {
        Assert.Equal(new AxisPosition(2, 0, 0), AxisSearch.Find(new long[] { 100, 200, 200, 300 }, 200, true));
        Assert.Equal(new AxisPosition(2, 0.5, 0), AxisSearch.Find(new long[] { 100, 200, 200, 300 }, 250, true));
    }

    [Fact]
    public void SinglePointAxis() => Assert.Equal(new AxisPosition(0, 0, 1), AxisSearch.Find(new long[] { 50 }, 60, true));

    [Fact]
    public void EmptyAxisIsRefused() => Assert.Throws<ArgumentException>(() => AxisSearch.Find(Array.Empty<long>(), 1, true));

    [Fact]
    public void PositionAddsTheFraction() => Assert.Equal(5.25, new AxisPosition(5, 0.25, 0).Position);

    [Fact]
    public void FourCellsWeightedBilinearly()
    {
        var cells = CellWeights.Around(new AxisPosition(5, 0.25, 0), new AxisPosition(1, 0.5, 0), 20, 16);
        Assert.Equal(new[] { new CellWeight(5, 1, 0.375), new CellWeight(5, 2, 0.375), new CellWeight(6, 1, 0.125), new CellWeight(6, 2, 0.125) }, cells);
    }

    [Fact]
    public void EdgeCellHasAllTheWeight() =>
        Assert.Equal(new[] { new CellWeight(19, 15, 1) }, CellWeights.Around(new AxisPosition(19, 0, 1), new AxisPosition(15, 0, 1), 20, 16));

    [Fact]
    public void CurveUsesRowsOnly() =>
        Assert.Equal(new[] { new CellWeight(1, 0, 0.75), new CellWeight(2, 0, 0.25) }, CellWeights.Around(new AxisPosition(1, 0.25, 0), null, 4, 1));

    [Fact]
    public void InterpolateIsTheWeightedSumAndNaNPropagates()
    {
        var v = new double[,] { { 0, 10 }, { 20, double.NaN } };
        Assert.Equal(5, CellWeights.Interpolate(v, CellWeights.Around(new AxisPosition(0, 0, 0), new AxisPosition(0, 0.5, 0), 2, 2)), 9);
        Assert.True(double.IsNaN(CellWeights.Interpolate(v, CellWeights.Around(new AxisPosition(0, 0.5, 0), new AxisPosition(0, 0.5, 0), 2, 2))));
    }

    [Fact]
    public void TrailFadesAndKeepsCapacity()
    {
        var trail = new CursorTrail(capacity: 3, life: TimeSpan.FromSeconds(1));
        for (var i = 0; i < 5; i++)
        {
            trail.Add(i, i, TimeSpan.FromMilliseconds(100 * i));
        }

        var points = trail.Points(TimeSpan.FromMilliseconds(500));
        Assert.Equal(new[] { 2.0, 3.0, 4.0 }, points.Select(p => p.Row));
        Assert.Equal(0.7, points[0].Opacity, 9);
        Assert.Equal(0.9, points[2].Opacity, 9);
        Assert.Empty(trail.Points(TimeSpan.FromSeconds(2)));
        trail.Add(1, 1, TimeSpan.FromSeconds(2));
        trail.Clear();
        Assert.Empty(trail.Points(TimeSpan.FromSeconds(2)));
    }
}
