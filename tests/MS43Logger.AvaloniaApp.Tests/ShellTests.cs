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

    // Performance is the fourth tab, after Compare, with no shortcut of its own (performance spec 9.1).
    [AvaloniaFact]
    public void TabsAreLiveTuneLiveTuningFunCompareAndPerformance()
    {
        var (window, shell) = Show();
        var tabs = window.FindControl<StackPanel>("Tabs")!.Children.OfType<ToggleButton>().ToList();

        Assert.Equal(["Live", "Tune", "Live tuning", "Fun", "Compare", "Performance"], tabs.Select(tab => tab.Content as string));
        Assert.Equal("TabPerformance", tabs[^1].Name);

        tabs[^1].IsChecked = true;

        Assert.Equal(ShellViewModel.PerformanceTab, shell.SelectedTab);
        Assert.True(window.GetVisualDescendants().OfType<PerformanceView>().Single().IsVisible);
        Assert.False(window.GetVisualDescendants().OfType<LiveView>().Single().IsVisible);
        Assert.False(tabs[0].IsChecked);
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

    // The theme switch shows a moon and a sun; the words stay as tooltips and accessible names.
    [AvaloniaFact]
    public void TheThemeSwitchIsAMoonAndASun()
    {
        var (window, _) = Show();
        foreach (var (name, icon, label) in new[] { ("NightButton", "Icon.Moon", "Night"), ("DayButton", "Icon.Sun", "Day") })
        {
            var button = window.FindControl<ToggleButton>(name)!;
            var glyph = Assert.IsType<MS43Logger.AvaloniaApp.Controls.StrokeIcon>(button.Content);
            Assert.Same(window.FindResource(icon), glyph.Data);
            Assert.Equal(label, ToolTip.GetTip(button));
            Assert.Equal(label, Avalonia.Automation.AutomationProperties.GetName(button));
        }

        Screens.Save(window, "theme-switch");
    }

    // Switching Night and Day cross-fades: a picture of the old theme lies over the new one and fades out.
    [AvaloniaFact]
    public void SwitchingTheThemeCrossFades()
    {
        var animations = MotionSettings.AnimationsEnabled;
        MotionSettings.AnimationsEnabled = true;
        try
        {
            var (window, shell) = Show();
            var fade = window.FindControl<Image>("ThemeFade")!;
            Assert.False(fade.IsVisible);

            shell.IsDay = true;
            Assert.True(fade.IsVisible);
            Assert.NotNull(fade.Source);

            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(10);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            Assert.False(fade.IsVisible);
            Assert.Null(fade.Source);
        }
        finally
        {
            MotionSettings.AnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public void WithAnimationsOffTheThemeJustSwitches()
    {
        var (window, shell) = Show();
        shell.IsDay = true;

        Assert.False(window.FindControl<Image>("ThemeFade")!.IsVisible);
    }
}
