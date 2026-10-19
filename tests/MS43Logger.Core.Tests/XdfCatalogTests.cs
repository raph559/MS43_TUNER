using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class XdfCatalogTests
{
    private static readonly Lazy<(XdfDefinition Xdf, XdfCatalog Catalog)> Real = new(() =>
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        return (xdf, new XdfCatalog(xdf));
    });

    private static XdfItem Item(string title) => Real.Value.Xdf.FindByTitle(title)!;

    [RealFileFact(TestData.Xdf)]
    public void ConfigSwitchChoices()
    {
        var choices = Real.Value.Catalog.Choices((XdfConstant)Item("c_conf_load"));

        Assert.Equal(new[] { (0L, "Map sensor"), (1L, "Maf sensor"), (2L, "Alpha-N") }, choices);
        Assert.Null(Real.Value.Catalog.Choices((XdfConstant)Item("c_lc_n_max")));
    }

    [RealFileFact(TestData.Xdf)]
    public void KindsOfKnownItems()
    {
        var catalog = Real.Value.Catalog;

        Assert.Equal(ItemKind.Map, catalog.Kind(Item("ip_iga_ron98_pl__n__maf")));
        Assert.Equal(ItemKind.Axis, catalog.Kind(Item("ldpm_n_14")));
        Assert.Equal(ItemKind.Curve, catalog.Kind(Item("ip_iga_st__n")));
        Assert.Equal(ItemKind.Value, catalog.Kind(Item("c_lc_n_max")));
        Assert.Equal(ItemKind.Switch, catalog.Kind(Item("c_conf_n_max")));
        Assert.Equal(ItemKind.Flag, catalog.Kind(Item("lv_mil_ind_knk_lv_1")));
    }

    [RealFileFact(TestData.Xdf)]
    public void AxisUsersOfIgnitionRpmAxis()
    {
        var users = Real.Value.Catalog.AxisUsers((XdfTable)Item("ldpm_n_14")).Select(table => table.Title).OrderBy(title => title);

        Assert.Equal(new[] { "ip_iga_e85_pl__n__maf", "ip_iga_ron91_pl__n__maf", "ip_iga_ron98_pl__n__maf" }, users);
    }

    [RealFileFact(TestData.Xdf)]
    public void SearchRanksTitlePrefixFirst()
    {
        var results = Real.Value.Catalog.Search("IP_IGA_RON98");

        Assert.Equal(new[] { "ip_iga_ron98_is__n__maf", "ip_iga_ron98_pl__n__maf" }, results.Take(2).Select(item => item.Title));
        Assert.Contains(Real.Value.Catalog.Search("load input"), item => item.Title == "c_conf_load");
        Assert.Equal(5, Real.Value.Catalog.Search("ip_", max: 5).Count);
        Assert.Empty(Real.Value.Catalog.Search("   "));
    }

    [RealFileFact(TestData.Xdf)]
    public void CategoriesHaveCounts()
    {
        var categories = Real.Value.Catalog.Categories().ToDictionary(c => c.Category, c => c.Count);

        Assert.Equal(156, categories["Ignition"]);
        Assert.Equal(616, categories["Axis"]);
        Assert.Equal(156, Real.Value.Catalog.InCategory("Ignition").Count);
        Assert.Contains(Real.Value.Catalog.InCategory("Other"), item => item.Title == "-----Softwareversion-----");
    }
}
