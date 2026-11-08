using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace MS43Logger.AvaloniaApp.Tests;

public class RailClickTests
{
    // Reported crash: with the car's bin and XDF open, clicking the Air Conditioner header threw from inside the list's
    // own selection change (the rail was rebuilt while the ListBox was still handling it). Every header is clicked here.
    [AvaloniaFact]
    public async Task ClickingEveryCategoryHeaderOpensAndClosesIt()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var list = Find.Named<ListBox>(window, "RailList");
        var names = shell.Tune.Rail.OfType<RailCategory>().Select(category => category.Name).ToList();
        Assert.Contains("Air Conditioner", names);

        foreach (var name in names)
        {
            list.SelectedItem = shell.Tune.Rail.OfType<RailCategory>().Single(category => category.Name == name);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Tune.Rail.OfType<RailCategory>().Single(category => category.Name == name).IsExpanded, name);

            list.SelectedItem = shell.Tune.Rail.OfType<RailCategory>().Single(category => category.Name == name);
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Tune.Rail.OfType<RailCategory>().Single(category => category.Name == name).IsExpanded, name);
        }
    }

    // The owner: clicking in the rail moved its rows. Picking an item adds it to Recent at the top and rebuilds the
    // rail, so every row under Recent slid down; the clicked row now stays where it was on screen.
    [AvaloniaFact]
    public async Task TheClickedRowStaysWhereItWas()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null);
        var tune = shell.Tune;
        var list = Find.Named<ListBox>(window, "RailList");
        const string title = "ip_iga_ron98_pl__n__maf";
        tune.ToggleCategory("Ignition");
        Dispatcher.UIThread.RunJobs();
        int Index() => tune.Rail.Select((entry, i) => (entry, i)).Last(pair => pair.entry is RailItem { Item.Title: title }).i;
        double Top() => list.ContainerFromIndex(Index())!.TranslatePoint(default, list)!.Value.Y;
        list.ScrollIntoView(Index());
        Dispatcher.UIThread.RunJobs();
        var before = Top();

        list.SelectedItem = tune.Rail[Index()];
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(title, tune.SelectedItem?.Title);
        Assert.Contains(tune.Rail, entry => entry is RailGroup { Title: "Recent" });
        Assert.Equal(before, Top(), 1);

        // Opening a category from its header keeps the header in place too.
        var header = tune.Rail.OfType<RailCategory>().Last();
        list.ScrollIntoView(tune.Rail.IndexOf(header));
        Dispatcher.UIThread.RunJobs();
        double HeaderTop() => list.ContainerFromIndex(tune.Rail.Select((entry, i) => (entry, i)).Single(pair => pair.entry is RailCategory c && c.Name == header.Name).i)!
            .TranslatePoint(default, list)!.Value.Y;
        var headerBefore = HeaderTop();
        list.SelectedItem = header;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(headerBefore, HeaderTop(), 1);
    }

    [AvaloniaFact]
    public async Task ClickingAnItemAfterOpeningACategorySelectsIt()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var list = Find.Named<ListBox>(window, "RailList");
        list.SelectedItem = shell.Tune.Rail.OfType<RailCategory>().Single(category => category.Name == "Air Conditioner");
        Dispatcher.UIThread.RunJobs();

        var item = shell.Tune.Rail.OfType<RailItem>().First();
        list.SelectedItem = item;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(item.Item, shell.Tune.SelectedItem);
    }
}
