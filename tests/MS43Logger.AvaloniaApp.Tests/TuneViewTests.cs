using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace MS43Logger.AvaloniaApp.Tests;

public class TuneViewTests
{
    [AvaloniaFact]
    public async Task OpenFileShowsPlateRailAndItem()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        shell.Tune.ToggleCategory("Ignition");

        Assert.Equal("CAR_current_43X001_SP95_STAGE1.bin", window.FindControl<TextBlock>("PlateFile")!.Text);
        Assert.Equal("Saved", window.FindControl<TextBlock>("PlateDirty")!.Text);
        Assert.Equal("ip_iga_ron98_pl__n__maf", Find.Named<TextBlock>(window, "ItemTitle").Text);
        Screens.Save(window, "tune-rail");
    }
}

public class VariantTests
{
    [AvaloniaFact]
    public async Task OpeningAnMs43x001BinSelectsTheMs43xDefinition()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(select: null);

        Assert.Equal("ms43x", shell.Live.SelectedVariant);
        Assert.Equal("ms43x", shell.Tune.Settings.Variant);
        Assert.Equal(7104, shell.Live.Gauges[0].RedFrom);
    }
}

public class VisualPassTests
{
    [AvaloniaFact]
    public async Task DayThemeAndEmptyState()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ms43-empty-" + Guid.NewGuid().ToString("N"));
        var shell = new ShellViewModel(new MainWindowViewModel(), new TuneViewModel(AppSettings.Load(folder)));
        var empty = new MainWindow(shell);
        shell.SelectedTab = ShellViewModel.TuneTab;
        empty.Show();
        Screens.Save(empty, "tune-empty");
        empty.Close();

        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, opened) = await TuneScreen.OpenAsync();
        opened.IsDay = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Screens.Save(window, "tune-day");
        opened.SelectedTab = ShellViewModel.LiveTab;
        opened.Live.SelectedVariant = "ms43x";
        Screens.Save(window, "live-day");
        opened.IsNight = true;
    }
}
