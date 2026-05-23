using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class CopyFromTests
{
    private static CalibrationDocument Car() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static BinImage Release() => BinImage.Load(TestData.Find(TestData.ReleaseBin)!);

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void CopyTableFromReleaseMatchesReleaseAndUndoes()
    {
        var d = Car();
        var release = Release();
        var history = new EditHistory(d);
        var map = (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;
        var original = d.Image.ToArray();

        history.Apply(TableEdits.CopyFrom(d, map, release), "Copy from release");

        Assert.Equal(release.ReadElementsOf(map.Z.Data), d.Image.ReadElementsOf(map.Z.Data));
        Assert.DoesNotContain(BinComparer.Compare(d.Xdf, d.Image, release).Items, item => item.Item == map);
        Assert.Equal(new[] { map }, d.ModifiedItems());

        Assert.True(history.Undo());
        Assert.Equal(original, d.Image.ToArray());
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void CopyConstantAndSwitch()
    {
        var d = Car();
        var release = Release();
        var history = new EditHistory(d);

        foreach (var name in new[] { "c_inj_flow", "c_conf_n_max" })
        {
            var constant = (XdfConstant)d.Xdf.FindByTitle(name)!;
            history.Apply(TableEdits.CopyFrom(d, constant, release), "Copy " + name);
            Assert.Equal(release.ReadElement(constant.Data, 0), d.Image.ReadElement(constant.Data, 0));
        }

        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void CopyFlagChangesOnlyItsBit()
    {
        var xdf = BinComparerTests.Synthetic();
        var d = CalibrationDocument.FromParts(xdf, BinComparerTests.With(BinComparerTests.Blank(), (0x70320, 0x01)), null);
        var source = BinComparerTests.With(BinComparerTests.Blank(), (0x70320, 0x02));
        var flagB = xdf.Flags.Single(flag => flag.Title == "flag_b");

        var change = Assert.Single(TableEdits.CopyFrom(d, flagB, source).Changes);

        Assert.Equal(new CellChange(0, 0, 0x01, 0x03), change);
    }

    [Fact]
    public void CopyUnsupportedIsRefused()
    {
        var xdf = BinComparerTests.Synthetic();
        var d = CalibrationDocument.FromParts(xdf, BinComparerTests.Blank(), null);
        var odd = xdf.Constants.Single(constant => constant.Title == "c_odd");

        Assert.Throws<EditRefusedException>(() => TableEdits.CopyFrom(d, odd, BinComparerTests.With(BinComparerTests.Blank(), (0x70400, 7))));
        Assert.False(d.IsModified);
    }

    [Fact]
    public void CopyIdenticalItemIsEmptyPlan()
    {
        var xdf = BinComparerTests.Synthetic();
        var d = CalibrationDocument.FromParts(xdf, BinComparerTests.Blank(), null);
        var history = new EditHistory(d);

        history.Apply(TableEdits.CopyFrom(d, xdf.Tables[0], BinComparerTests.Blank()), "Copy");

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void CopyTableWarnsWhenItsAxisDiffers()
    {
        var xdf = BinComparerTests.Synthetic();
        var d = CalibrationDocument.FromParts(xdf, BinComparerTests.With(BinComparerTests.Blank(), (0x70201, 1)), null);
        var source = BinComparerTests.With(BinComparerTests.Blank(), (0x70201, 3), (0x70100, 4));

        var plan = TableEdits.CopyFrom(d, xdf.Tables[0], source);

        var warning = Assert.Single(plan.Warnings);
        Assert.Contains("row axis (axis_rows)", warning);
        Assert.Contains("A's breakpoints", warning);
        Assert.Empty(TableEdits.CopyFrom(d, xdf.Tables[0], BinComparerTests.With(BinComparerTests.Blank(), (0x70201, 1), (0x70100, 4))).Warnings);
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void CopyIgnitionFromReleaseWarnsAboutTheResampledRpmAxis()
    {
        var d = Car();
        var map = (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

        Assert.Contains(TableEdits.CopyFrom(d, map, Release()).Warnings, warning => warning.Contains("ldpm_n_14"));
    }

    [Fact]
    public void CopyTableWritesEveryDifferingCell()
    {
        var xdf = BinComparerTests.Synthetic();
        var d = CalibrationDocument.FromParts(xdf, BinComparerTests.Blank(), null);
        var source = BinComparerTests.With(BinComparerTests.Blank(), (0x70101, 4), (0x70105, 9));

        var plan = TableEdits.CopyFrom(d, xdf.Tables[0], source);

        Assert.Equal(new[] { new CellChange(0, 1, 0, 4), new CellChange(1, 2, 0, 9) }, plan.Changes);
    }
}

internal static class ImageReading
{
    public static long[] ReadElementsOf(this BinImage image, XdfData data) =>
        Enumerable.Range(0, data.Count).Select(i => image.ReadElement(data, i)).ToArray();
}
