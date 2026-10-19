using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class RealXdfTests
{
    private static XdfDefinition Load() => XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);

    [RealFileFact(TestData.Xdf)]
    public void CountsMatch()
    {
        var xdf = Load();

        Assert.Equal(1479, xdf.Tables.Count);
        Assert.Equal(2293, xdf.Constants.Count);
        Assert.Equal(4, xdf.Flags.Count);
        Assert.Equal("MS43X001", xdf.Title);
        Assert.Equal(0x70000, xdf.BaseOffset);
    }

    [RealFileFact(TestData.Xdf)]
    public void EveryAxisLinkResolves()
    {
        var xdf = Load();
        var links = xdf.Tables
            .SelectMany(table => new[] { table.X, table.Y })
            .Where(axis => axis.LinkedTableId is not null)
            .ToList();

        Assert.Equal(1253, links.Count);
        Assert.All(links, axis => Assert.NotNull(xdf.FindTableById(axis.LinkedTableId!.Value)));
    }

    [RealFileFact(TestData.Xdf)]
    public void KnownQuirksAreReported()
    {
        var xdf = Load();

        Assert.Single(xdf.Warnings, warning => warning.Contains("ip_ectpwm_i__tco_dif", StringComparison.Ordinal));
        Assert.Contains(xdf.Warnings, warning => warning.Contains("0x2738", StringComparison.Ordinal));
        Assert.Equal("ip_n_ecf_temp_cat__temp_cat", xdf.FindTableById(0x2738)!.Title);
    }

    [RealFileFact(TestData.Xdf)]
    public void CategoriesResolve()
    {
        var xdf = Load();
        var table = (XdfTable)xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

        Assert.Contains("Ignition", table.CategoryIds.Select(id => xdf.Categories[id]));
    }
}
