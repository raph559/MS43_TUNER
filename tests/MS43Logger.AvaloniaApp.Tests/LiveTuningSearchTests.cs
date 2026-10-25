using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The live map list as the Tune rail has it: each map with its XDF categories and description, grouped by category,
/// and a search over the title, the XDF name, the categories, the description, the units and the address.
/// </summary>
public sealed class LiveTuningSearchTests
{
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell, LiveTuningViewModel Page, XdfCatalog Catalog)> V7()
    {
        var (window, shell) = await TuneScreen.OpenAsync(time: new ManualTicker());
        shell.SelectedTab = ShellViewModel.LiveTuningTab;
        var page = shell.LiveTuning;
        await page.AttachLine(new FakeOverlayV4Ecu(shell.Tune.Document!.Image.ToArray(), 7));
        return (window, shell, page, new XdfCatalog(shell.Tune.Document.Xdf));
    }

    private static XdfItem ItemOf(XdfCatalog catalog, LiveMapRow row) => catalog.All.First(item => item.Title == row.Title);

    [CarAvaloniaFact]
    public async Task EachMap_CarriesItsXdfCategoryAndDescription()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, catalog) = await V7();
        try
        {
            var row = page.Maps.First(row => row.Map.ScalarGroup is null);
            var item = ItemOf(catalog, row);
            Assert.Equal(catalog.CategoriesOf(item), row.Categories);
            Assert.Equal(item.Description, row.Description);
            Assert.Contains($"0x{row.Map.Address:X}", row.Detail);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task TheSearch_FindsAMapByAWordOfItsDescription()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, _) = await V7();
        try
        {
            var row = page.Maps.First(row => row.Map.ScalarGroup is null && row.HasDescription
                && DescriptionOnlyWord(row) is not null);
            var word = DescriptionOnlyWord(row)!;
            page.MapSearch = word;
            Assert.Contains(row, page.FilteredMaps);
            Assert.DoesNotContain(page.Rail, entry => entry is LiveRailCategory);    // a search lists the matches, flat
        }
        finally { page.Dispose(); window.Close(); }

        static string? DescriptionOnlyWord(LiveMapRow row) => row.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('.', ',', ';', ':', '(', ')'))
            .FirstOrDefault(word => word.Length >= 6 && word.All(char.IsLetter)
                && !row.Title.Contains(word, StringComparison.OrdinalIgnoreCase) && !row.Map.Key.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    [CarAvaloniaFact]
    public async Task TheSearch_FindsTheMapsOfACategory_AndEveryWordMustMatch()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, _) = await V7();
        try
        {
            var category = page.Maps.SelectMany(row => row.Categories).First(name => name != XdfCatalog.OtherCategory);
            page.MapSearch = category;
            Assert.All(page.Maps.Where(row => row.Categories.Contains(category)), row => Assert.Contains(row, page.FilteredMaps));
            page.MapSearch = category + " zzqqxx";
            Assert.Empty(page.FilteredMaps);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task WithoutASearch_TheListIsGroupedByCategory_AndACategoryOpensAndCloses()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, _) = await V7();
        try
        {
            var categories = page.Rail.OfType<LiveRailCategory>().ToList();
            Assert.NotEmpty(categories);
            Assert.Equal(page.Maps.SelectMany(row => row.Categories).Distinct().Count(), categories.Count);

            var closed = categories.First(category => !category.IsExpanded);
            page.ToggleCategory(closed.Name);
            var at = page.Rail.ToList().FindIndex(entry => entry is LiveRailCategory { Name: var name } && name == closed.Name);
            Assert.True(((LiveRailCategory)page.Rail[at]).IsExpanded);
            var shown = page.Rail.Skip(at + 1).TakeWhile(entry => entry is LiveMapRow).Cast<LiveMapRow>().ToList();
            Assert.Equal(closed.Count, shown.Count);
            Assert.All(shown, row => Assert.Contains(closed.Name, row.Categories));

            page.ToggleCategory(closed.Name);
            Assert.False(page.Rail.OfType<LiveRailCategory>().Single(category => category.Name == closed.Name).IsExpanded);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task SelectingAMap_OpensItsCategory_SoTheListShowsIt()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, _) = await V7();
        try
        {
            var other = page.Maps.Last(row => !row.Categories.Intersect(page.Selected!.Categories).Any());
            page.Selected = other;
            Assert.Contains(other, page.Rail);
        }
        finally { page.Dispose(); window.Close(); }
    }

    private sealed class ManualTicker : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
