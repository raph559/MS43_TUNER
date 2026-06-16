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
    private static (MainWindow Window, ShellViewModel Shell) Show(AppSettings? settings = null)
    {
        var (window, shell) = TestShell.Create(settings);
        window.Show();
        return (window, shell);
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

        Assert.Equal(Color.Parse("#0A0B0D"), Pane());
        shell.IsDay = true;
        Assert.Equal(Color.Parse("#FFFFFF"), Pane());
        Screens.Save(window, "shell-day");
        shell.IsNight = true;
        Assert.Equal(Color.Parse("#0A0B0D"), Pane());
    }

    [AvaloniaFact]
    public void NightDaySwitchIsSaved()
    {
        var folder = TestShell.NewFolder();
        var (_, shell) = Show(AppSettings.Load(folder));

        shell.IsDay = true;

        Assert.Equal(ThemeMode.Day, AppSettings.Load(folder).Theme);
        shell.IsNight = true;
        Assert.Equal(ThemeMode.Night, AppSettings.Load(folder).Theme);
    }

    [AvaloniaFact]
    public void ThemeIsRestoredFromSettings()
    {
        var settings = AppSettings.Load(TestShell.NewFolder());
        settings.Theme = ThemeMode.Day;

        var (_, shell) = Show(settings);

        Assert.True(shell.IsDay);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Application.Current!.ActualThemeVariant);
        shell.IsNight = true;
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
        string Family(string key) =>
            FontManager.Current.TryGetGlyphTypeface(new Typeface((FontFamily)window.FindResource(key)!), out var glyph) ? glyph.FamilyName : "(none)";

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("Bahnschrift", Family("DataFont"));
            Assert.Equal("Cascadia Mono", Family("MonoFont"));
            Assert.Equal("Segoe UI", Family("UiFont"));
        }
    }
}
