using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The warm-up banners at the top of Live (performance spec 5, 9.3).</summary>
public sealed class WarmUpBannerTests : IDisposable
{
    private const string ColdCoolant = "Coolant 64 °C: engine not at operating temperature (80 °C). Do not use full throttle; results will not be valid.";

    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private (MainWindow Window, ShellViewModel Shell) Open()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder), new MainWindowViewModel(new ManualTime(), () => ["COM1"]));
        shell.Live.SelectedVariant = "ms43x";
        window.Show();
        return (window, shell);
    }

    // Connected on ms43x, the samples shown as the logger delivers them, so SampleShown fires.
    private static void Feed(ShellViewModel shell, IEnumerable<LiveSample> samples)
    {
        shell.Live.BeginSession();
        shell.Live.CurrentDefinition = Ms43x;
        foreach (var sample in samples)
        {
            shell.Live.ShowSample(sample);
        }

        Dispatcher.UIThread.RunJobs();
    }

    // The banners Live shows, by their stripe classes and text.
    private static IReadOnlyList<(string Classes, string? Text)> Banners(MainWindow window)
    {
        var view = window.GetVisualDescendants().OfType<LiveView>().Single();
        return view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Name == "WarmUpBanner" && border.IsEffectivelyVisible)
            .Select(border => (string.Join(" ", border.Classes.Where(name => !name.StartsWith(':'))),
                border.GetVisualDescendants().OfType<NumberText>().Single(text => text.Name == "WarmUpBannerText").Value))
            .ToList();
    }

    [AvaloniaFact]
    public void ColdCoolantShowsARedBannerOnLive()
    {
        var (window, shell) = Open();
        Feed(shell, SyntheticRuns.Idle(Ms43x, 1, coolantC: 64, oilC: 88));

        var banner = Assert.Single(Banners(window), b => b.Text == ColdCoolant);
        Assert.Equal("banner warn", banner.Classes);
        Assert.True(shell.Live.WarmUp.CoolantCold);
        Assert.True(shell.Live.WarmUpBanners[0].IsDanger);
    }

    [AvaloniaFact]
    public void ColdOilShowsAnAmberBanner()
    {
        var (window, shell) = Open();
        Feed(shell, SyntheticRuns.Idle(Ms43x, 1, coolantC: 92, oilC: 60));

        var banner = Assert.Single(Banners(window));
        Assert.Equal("banner caution", banner.Classes);
        Assert.Equal("Oil 60 °C: not at working temperature yet (80 °C).", banner.Text);
        Assert.True(Assert.Single(shell.Live.WarmUpBanners).IsCaution);
    }

    [AvaloniaFact]
    public void ANewReadingChangesTheBannerInPlace()
    {
        var (window, shell) = Open();
        Feed(shell, SyntheticRuns.Idle(Ms43x, 1, coolantC: 64, oilC: 88));
        var shown = Assert.Single(shell.Live.WarmUpBanners);

        foreach (var sample in SyntheticRuns.Idle(Ms43x, 1, coolantC: 70, oilC: 88))
        {
            shell.Live.ShowSample(sample);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Same(shown, Assert.Single(shell.Live.WarmUpBanners));
        Assert.StartsWith("Coolant 70 °C: ", Assert.Single(Banners(window)).Text);
    }

    [AvaloniaFact]
    public void WarmEngineShowsNoBanner()
    {
        var (window, shell) = Open();
        Feed(shell, SyntheticRuns.Idle(Ms43x, 1));

        Assert.Empty(Banners(window));
        Assert.Empty(shell.Live.WarmUpBanners);
    }

    [AvaloniaFact]
    public void BannersClearOnDisconnect()
    {
        var (window, shell) = Open();
        Feed(shell, SyntheticRuns.Idle(Ms43x, 1, coolantC: 64, oilC: 60));
        Assert.Equal(2, Banners(window).Count);

        shell.Live.Disconnect();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Banners(window));
        Assert.Empty(shell.Live.WarmUpBanners);
        Assert.False(shell.Live.WarmUp.CoolantCold);
    }

    [AvaloniaFact]
    public void OilIsPinnedByDefault()
    {
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"]) { SelectedVariant = "ms43x" };
        Assert.Contains(live.TileValues, row => row.Id == "TOIL");
        Assert.Contains(live.TileValues, row => row.Id == "TCO");

        // The stock definitions name it OILTEMP.
        live.SelectedVariant = "b25";
        Assert.Contains(live.TileValues, row => row.Id == "OILTEMP");
    }
}
