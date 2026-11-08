using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// Knock retard per cylinder on the Live tab (owner, 2026-10-28): the Performance tab's six bars plus an average bar, all the
/// time while driving, with the warning line naming each cylinder at or past 1.5 °CRK.
/// </summary>
public sealed class LiveKnockTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheAverageBarFollowsTheSixCylinders()
    {
        var bars = new CylinderBars { ShowsAverage = true, Values = [0.0, 0.75, null, 1.5, 0.0, 0.75] };

        Assert.Equal(["1", "2", "n/a", "4", "5", "6", "Avg"], bars.Labels);
        Assert.Equal(0.6, bars.Average!.Value, 6);

        bars.Measure(new Avalonia.Size(420, 100));
        bars.Arrange(new Avalonia.Rect(0, 0, 420, 78));
        var gapBefore = bars.BarRect(6).Left - bars.BarRect(5).Right;
        var gapBetween = bars.BarRect(1).Left - bars.BarRect(0).Right;
        Assert.True(gapBefore > gapBetween, "the average bar stands apart from the six");

        bars.Values = [null, null, null, null, null, null];
        Assert.Null(bars.Average);
        Assert.Equal("n/a", bars.Labels[6]);
        Assert.Equal(6, new CylinderBars { Values = [1.0, 1.0, 1.0, 1.0, 1.0, 1.0] }.Labels.Count);
    }

    [AvaloniaFact]
    public void TheLiveTabShowsKnockPerCylinderWhileDriving()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Width = 1440;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var bars = Find.Named<CylinderBars>(window, "LiveKnockBars");
        Assert.True(bars.ShowsAverage);
        Assert.False(bars.IsLive);
        Assert.Equal("n/a", bars.Labels[0]);

        PerfFeed.Connect(shell);
        // Cylinder 4 knocks from 5000 to 5600 rpm: stop in that band.
        foreach (var sample in SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, knockCyl4: 1.6))
        {
            shell.Live.ShowSample(sample);
            if (shell.Live.Knock is [.., { } fourth, _, _] && fourth >= 1.5) break;
        }

        Dispatcher.UIThread.RunJobs();

        Assert.True(Find.Named<Control>(window, "LiveKnockSection").IsEffectivelyVisible);
        Assert.Contains(Find.Named<Control>(window, "ReadoutBand"), bars.GetVisualAncestors());
        Assert.True(bars.IsLive);
        Assert.Equal(shell.Live.LastFrame!.KnockRetard, bars.Values);
        // The frame's step is 0.375 °CRK, so 1.6 arrives as 1.5.
        Assert.Equal("Knock retard: cylinder 4 at 1.5 °CRK.", shell.Live.KnockWarning);
        Assert.Equal("cyl 4 · 1.5°", shell.Live.KnockShort);
        Assert.DoesNotContain(shell.Live.TileValues, value => value.Id is "N" or "MAFIGA" or "IGAAVG");
        var speedTitle = Find.Named<Control>(window, "SpeedReadout").GetVisualAncestors().OfType<Grid>().First()
            .Children.OfType<TextBlock>().Single(text => text.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Any(run => run.Text == "Speed") == true);
        Assert.True(speedTitle.DesiredSize.Width <= speedTitle.Bounds.Width + 0.5, $"the Speed title is cut: {speedTitle.DesiredSize} in {speedTitle.Bounds}, slots {Find.Named<Control>(window, "GearSlots").Bounds}, cell {speedTitle.GetVisualAncestors().OfType<Border>().First().Bounds}");
        Screens.Save(window, "live-knock-bars");

        shell.Live.Disconnect();
        Dispatcher.UIThread.RunJobs();
        Assert.False(bars.IsLive);
        Assert.All(bars.Labels, label => Assert.Equal("n/a", label));
        Assert.Null(shell.Live.KnockWarning);
        window.Close();
    }

    // Knock has the band's fifth cell: gear and speed share one, and the bars stand tall in Tiles, Dials and a narrow window.
    [AvaloniaTheory]
    [InlineData(1440, 900, false, 48)]
    [InlineData(1440, 900, true, 48)]
    [InlineData(1024, 680, false, 32)]
    public void TheKnockCellHasTallBars(int width, int height, bool dials, double tallest)
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Width = width;
        window.Height = height;
        shell.IsDials = dials;
        shell.IsTiles = !dials;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bars = Find.Named<CylinderBars>(window, "LiveKnockBars");
        Assert.True(bars.IsEffectivelyVisible);
        Assert.True(bars.BarRect(0).Height >= tallest, $"bars {bars.BarRect(0).Height} px tall");
        var gearCell = Find.Named<Control>(window, "GearSlots").GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("cell"));
        Assert.Contains(gearCell, Find.Named<Control>(window, "SpeedReadout").GetVisualAncestors());
        Screens.Save(window, $"live-knock-cell-{(dials ? "dials" : "tiles")}-{width}");
        window.Close();
    }

    // A channel the band shows is not repeated as a tile; it stays pinned in the list (owner, 2026-10-28).
    [AvaloniaFact]
    public void ABandChannelIsNotRepeatedAsATile()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        shell.Live.PreviewChannels();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(shell.Live.PinnedValues, value => value.Id == "N");
        Assert.DoesNotContain(shell.Live.TileValues, value => value.Id is "N" or "MAFIGA" or "IGAAVG" or "GEAR" or "VS");
        Assert.Contains(shell.Live.TileValues, value => value.Id == "VB");

        foreach (var row in shell.Live.PinnedValues.Where(value => value.Id is not ("N" or "MAFIGA" or "IGAAVG")).ToList())
        {
            shell.Live.TogglePinned(row);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.Live.TileValues);
        Assert.False(shell.Live.HasPinned);
        Assert.False(shell.Live.ShowsNoPins);
        window.Close();
    }
}
