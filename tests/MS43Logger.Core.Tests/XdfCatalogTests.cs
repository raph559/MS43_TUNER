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
    public void SearchFindsEveryWordAnywhere()
    {
        var catalog = Real.Value.Catalog;

        // "ron98" is in the title, "part" only in the description ("... for RON98 during part and full load").
        Assert.Contains(catalog.Search("ron98 part"), item => item.Title == "ip_iga_ron98_pl__n__maf");
        Assert.DoesNotContain(catalog.Search("ron98 part"), item => item.Title == "ip_iga_ron91_pl__n__maf");

        // A category name finds what is in it.
        var conditioner = catalog.InCategory("Air Conditioner").Select(item => item.Title).ToHashSet();
        Assert.Superset(conditioner, catalog.Search("air conditioner").Select(item => item.Title).ToHashSet());

        // The address, with or without 0x, and the units.
        Assert.Equal("ip_iga_ron98_pl__n__maf", catalog.Search("0x77315")[0].Title);
        Assert.Equal("ip_iga_ron98_pl__n__maf", catalog.Search("77315")[0].Title);
        Assert.Contains(catalog.Search("ron98 °CRK"), item => item.Title == "ip_iga_ron98_pl__n__maf");

        // Title matches still come first.
        Assert.Equal("ip_iga_ron98_is__n__maf", catalog.Search("IP_IGA_RON98")[0].Title);
    }

    [RealFileFact(TestData.Xdf)]
    public void ItemsSortByNameAddressKindOrSize()
    {
        var catalog = Real.Value.Catalog;
        var ignition = catalog.InCategory("Ignition");

        var byName = catalog.Sort(ignition, ItemSort.Name, descending: true).Select(item => item.Title).ToList();
        Assert.Equal(byName.OrderByDescending(title => title, StringComparer.OrdinalIgnoreCase), byName);

        var byAddress = catalog.Sort(ignition, ItemSort.Address, descending: false).Select(XdfCatalog.Address).ToList();
        Assert.Equal(byAddress.OrderBy(address => address ?? long.MaxValue), byAddress);

        var byKind = catalog.Sort(ignition, ItemSort.Kind, descending: false).Select(catalog.Kind).ToList();
        Assert.Equal(byKind.OrderBy(kind => kind), byKind);

        var bySize = catalog.Sort(ignition, ItemSort.Size, descending: true).Select(XdfCatalog.Size).ToList();
        Assert.Equal(bySize.OrderByDescending(size => size), bySize);
        Assert.Equal(ignition.Count, bySize.Count);
    }

    [RealFileFact(TestData.Xdf)]
    public void CategoriesSortByNameOrSize()
    {
        var catalog = Real.Value.Catalog;

        var byName = catalog.Categories(ItemSort.Name, descending: true).Select(pair => pair.Category).ToList();
        Assert.Equal(byName.OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase), byName);

        var bySize = catalog.Categories(ItemSort.Size, descending: true).Select(pair => pair.Count).ToList();
        Assert.Equal(bySize.OrderByDescending(count => count), bySize);

        // Address and kind have no meaning for a category: they keep it alphabetical.
        Assert.Equal(catalog.Categories(ItemSort.Name, false), catalog.Categories(ItemSort.Address, false));
    }

    [RealFileFact(TestData.Xdf)]
    public void ACategorySummarySaysWhatIsInIt()
    {
        var catalog = Real.Value.Catalog;
        var items = catalog.InCategory("Air Conditioner");
        var maps = items.Count(item => catalog.Kind(item) == ItemKind.Map);

        var summary = catalog.Summary("Air Conditioner");

        Assert.StartsWith($"{items.Count} items: ", summary);
        if (maps > 0)
        {
            Assert.Contains($"{maps} map", summary);
        }
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
