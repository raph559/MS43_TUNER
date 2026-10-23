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
