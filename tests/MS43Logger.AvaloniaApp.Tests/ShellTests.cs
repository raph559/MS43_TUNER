using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp.Tests;

public class ShellTests
{
    private static (MainWindow Window, ShellViewModel Shell) Show()
    {
        var window = new MainWindow();
        window.Show();
        return (window, (ShellViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public void ShellShowsLiveAndTuneTabs()
    {
        var (window, shell) = Show();
        var live = window.GetVisualDescendants().OfType<LiveView>().Single();
        var tune = window.GetVisualDescendants().OfType<TuneView>().Single();

        Assert.Equal("MS43 Tuner", window.Title);
        Assert.True(live.IsVisible);
        Assert.False(tune.IsVisible);

        window.FindControl<ToggleButton>("TabTune")!.IsChecked = true;

        Assert.Equal(ShellViewModel.TuneTab, shell.SelectedTab);
        Assert.False(live.IsVisible);
        Assert.True(tune.IsVisible);
        Screens.Save(window, "shell-night");
    }

    [AvaloniaFact]
    public void ThemeSwitchChangesPaneBrush()
    {
        var (window, shell) = Show();
        Color Pane() => window.TryFindResource("PaneBrush", window.ActualThemeVariant, out var brush) ? ((ISolidColorBrush)brush!).Color : default;

        Assert.Equal(Color.Parse("#141920"), Pane());
        shell.IsDay = true;
        Assert.Equal(Color.Parse("#F5F6F8"), Pane());
        Screens.Save(window, "shell-day");
        shell.IsNight = true;
        Assert.Equal(Color.Parse("#141920"), Pane());
    }

    [AvaloniaFact]
    public void ClosingWhileConnectedDisconnects()
    {
        var (window, shell) = Show();
        shell.Live.IsConnected = true;

        window.Close();

        Assert.False(shell.Live.IsConnected);
    }

    [AvaloniaFact]
    public void FontsResolve()
    {
        var (window, _) = Show();
        var display = (FontFamily)window.FindResource("DisplayFont")!;

        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(display), out var glyph));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("Bahnschrift", glyph!.FamilyName);
        }
    }
}
