using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class MapColourRangeTests
{
    /// <summary>One row of 8-bit cells whose physical value equals the raw value.</summary>
    private static MapColourRange Row(params long[] raw)
    {
        var rawCells = new long[1, raw.Length];
        var physical = new double[1, raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            rawCells[0, i] = raw[i];
            physical[0, i] = raw[i];
        }

        return MapColourRange.Compute(physical, rawCells, 0, 255);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void IgnitionMapLeavesItsLimitRowOut()
    {
        var d = CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);
        var values = d.Reader.ReadTable((XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!);

        var range = MapColourRange.Compute(values);

        Assert.Equal(Enumerable.Range(0, 16).Select(col => (19, col)), range.Hatched.Order());
        Assert.Equal(3.0, range.Min, 9);
        Assert.Equal(39.0, range.Max, 9);
    }

    [Fact]
    public void ConstantMapHasNoHatch()
    {
        var range = Row(0, 0, 0, 0);

        Assert.Empty(range.Hatched);
        Assert.True(range.IsConstant);
        Assert.Equal(0, range.Min);
    }

    [Fact]
    public void HalfTheCellsAtTheLimitAreOrdinaryCells()
    {
        var range = Row(0, 0, 10, 20);

        Assert.Empty(range.Hatched);
        Assert.Equal(0, range.Min);
        Assert.Equal(20, range.Max);
    }

    [Fact]
    public void LimitCloseToTheOtherCellsIsAnOrdinaryCell()
    {
        // 0 sits 2 below the 2 to 20 range: under a quarter of its 18.
        var range = Row(0, 2, 5, 10, 20);

        Assert.Empty(range.Hatched);
        Assert.Equal(0, range.Min);
        Assert.False(range.IsConstant);
    }

    [Fact]
    public void FarSentinelsAreHatchedAndLeftOutOfTheRange()
    {
        var range = Row(Enumerable.Range(10, 18).Select(value => (long)value).Concat(new long[] { 255, 255 }).ToArray());

        Assert.Equal(new[] { (0, 18), (0, 19) }, range.Hatched.Order());
        Assert.Equal(10, range.Min);
        Assert.Equal(27, range.Max);
    }
}
