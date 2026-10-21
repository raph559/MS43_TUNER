using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Rail rows say what they are on hover, and the rail can be sorted by name, address, kind or size, either way.</summary>
public class RailSortTests
{
    private static Border Row(Window window, Func<object?, bool> holds) =>
        window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row") && holds(border.DataContext));

    private static IEnumerable<string> Texts(object? tip) =>
        tip is Control control
            ? control.GetLogicalDescendants().Prepend(control).OfType<TextBlock>().Select(block => block.Text ?? string.Empty)
            : [tip?.ToString() ?? string.Empty];

    private static void ClickMenu(Window window, string name)
    {
        var button = Find.Named<Button>(window, "RailSortButton");
        var item = ((MenuFlyout)button.Flyout!).Items.OfType<MenuItem>().Single(menu => menu.Name == name);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task HoveringAnItemShowsItsDescriptionAndFacts()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        shell.Tune.ToggleCategory("Ignition");
        Dispatcher.UIThread.RunJobs();

        var entry = shell.Tune.Rail.OfType<RailItem>().First(item => item.Title == "ip_iga_ron98_pl__n__maf" && item.IsIndented);
        Assert.Equal("Target ignition angle for RON98 during part and full load", entry.Description);
        Assert.Equal("Map, 20 × 16, °CRK, at 0x77315", entry.Detail);

        // The rail is virtualized: bring the row on screen as scrolling to it would.
        Find.Named<ListBox>(window, "RailList").ScrollIntoView(entry);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // Hovering opens the tooltip, which is when its text binds to the row.
        var row = Row(window, context => context is RailItem { Title: "ip_iga_ron98_pl__n__maf", IsIndented: true });
        ToolTip.SetIsOpen(row, true);
        Dispatcher.UIThread.RunJobs();
        var tip = ToolTip.GetTip(row);
        Assert.Contains("ip_iga_ron98_pl__n__maf", Texts(tip));
        Assert.Contains(entry.Description, Texts(tip));
        Assert.Contains(entry.Detail, Texts(tip));
    }

    [AvaloniaFact]
    public async Task HoveringACategoryShowsWhatIsInIt()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var category = shell.Tune.Rail.OfType<RailCategory>().Single(entry => entry.Name == "Air Conditioner");

        Assert.StartsWith("11 items: ", category.Summary);
        Assert.Equal(category.Summary, ToolTip.GetTip(Row(window, context => context is RailCategory { Name: "Air Conditioner" })));
    }

    [AvaloniaFact]
    public async Task SortingReordersCategoriesAndItemsAndIsRemembered()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;

        tune.SetSort(ItemSort.Size, descending: true);
        tune.ToggleCategory("Ignition");

        var counts = tune.Rail.OfType<RailCategory>().Select(category => category.Count).ToList();
        Assert.Equal(counts.OrderByDescending(count => count), counts);
        var sizes = tune.Rail.OfType<RailItem>().Where(item => item.IsIndented).Select(item => XdfCatalog.Size(item.Item)).ToList();
        Assert.Equal(sizes.OrderByDescending(size => size), sizes);
        Assert.Equal("Sorted by size, largest first", tune.SortTip);

        var saved = AppSettings.Load(tune.Settings.Folder);
        Assert.Equal((ItemSort.Size, true), (saved.RailSort, saved.RailSortDescending));
    }

    [AvaloniaFact]
    public async Task TheSortMenuSetsKeyAndDirection()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;
        Assert.Equal((ItemSort.Name, false), (tune.Sort, tune.SortDescending));

        ClickMenu(window, "SortByAddress");
        Assert.Equal(ItemSort.Address, tune.Sort);
        ClickMenu(window, "SortDescending");
        Assert.True(tune.SortDescending);
        Assert.Equal("Sorted by address, highest first", tune.SortTip);

        // The checks are what the open menu shows.
        var sortButton = Find.Named<Button>(window, "RailSortButton");
        var menu = (MenuFlyout)sortButton.Flyout!;
        menu.ShowAt(sortButton);
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.Items.OfType<MenuItem>().Single(item => item.Name == "SortByAddress").IsChecked);
        Assert.False(menu.Items.OfType<MenuItem>().Single(item => item.Name == "SortByName").IsChecked);
        Assert.True(menu.Items.OfType<MenuItem>().Single(item => item.Name == "SortDescending").IsChecked);

        menu.Hide();
        ClickMenu(window, "SortByName");
        ClickMenu(window, "SortAscending");
        Assert.Equal("Sorted by name, A to Z", tune.SortTip);
    }

    [AvaloniaFact]
    public async Task SearchResultsKeepBestMatchOrder()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;
        tune.SetSort(ItemSort.Name, descending: true);

        tune.SearchText = "ip_iga_ron98";

        Assert.Equal("ip_iga_ron98_is__n__maf", tune.Rail.OfType<RailItem>().First().Title);
    }
}
