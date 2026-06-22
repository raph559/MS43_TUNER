using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class ReadoutBandTests
{
    private static (MainWindow Window, ShellViewModel Shell) ShowLive(string variant = "ms43x", int width = 1440, int height = 900)
    {
        var (window, shell) = TestShell.Create();
        shell.Live.SelectedVariant = variant;
        shell.Live.PreviewChannels();
        window.Width = width;
        window.Height = height;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    // One sample that holds these channels only, as the logger shows it.
    private static void Sample(MainWindowViewModel live, params (string Id, double Value)[] values)
    {
        var channels = BundledDefinitions.Load(live.SelectedVariant).MatchChannels(Array.Empty<string>());
        live.ShowSample(values.Select(value => new DecodedValue(channels.Single(channel => channel.Id == value.Id), value.Value)).ToList());
        Dispatcher.UIThread.RunJobs();
    }

    // A reading as shown: a ReadingText's value (its unit is a run of its own), or a TextBlock's text.
    private static string Text(Window window, string name) => Find.Named<Control>(window, name) switch
    {
        ReadingText reading => reading.Value ?? string.Empty,
        TextBlock text => text.Text ?? string.Empty,
        _ => string.Empty,
    };

    // Pixels of the band close to Now (system 1.4: orange means now), in Night or Day.
    private static int OrangePixels(Window window)
    {
        var band = Find.Named<Control>(window, "ReadoutBand");
        var origin = band.TranslatePoint(default, window)!.Value;
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var count = 0;
        for (var y = (int)origin.Y; y < (int)(origin.Y + band.Bounds.Height); y++)
        {
            for (var x = (int)origin.X; x < (int)(origin.X + band.Bounds.Width); x++)
            {
                var pixel = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                var colour = buffer.Format == PixelFormat.Rgba8888
                    ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
                    : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
                if (colour.R > 200 && colour.G is > 80 and < 150 && colour.B < 80)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [AvaloniaFact]
    public void TilesModeShowsThreeReadoutsGearAndSpeed()
    {
        var (window, shell) = ShowLive();

        Assert.True(shell.IsTiles);
        foreach (var name in new[] { "ReadoutRpm", "ReadoutLoad", "ReadoutAdvance", "GearSlots", "SpeedReadout", "RpmBar", "LoadBar", "AdvanceBar" })
        {
            Assert.True(Find.Named<Control>(window, name).IsEffectivelyVisible, name);
        }

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Gauge>(), gauge => gauge.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void DialsModeShowsThreeGauges()
    {
        var (window, shell) = ShowLive();

        shell.IsDials = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "N", "MAFIGA", "IGAAVG" }, shell.Live.Gauges.Select(gauge => gauge.ChannelId));
        Assert.Equal(3, window.GetVisualDescendants().OfType<Gauge>().Count(gauge => gauge.IsEffectivelyVisible));
        Assert.False(Find.Named<Control>(window, "ReadoutRpm").IsEffectivelyVisible);
        Assert.True(Find.Named<Control>(window, "GearSlots").IsEffectivelyVisible);
        Assert.True(Find.Named<Control>(window, "SpeedReadout").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void DialsKeepTheirSizes()
    {
        var (window, shell) = ShowLive();
        shell.IsDials = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((220, 184), (Find.Named<Gauge>(window, "RpmDial").Bounds.Width, Find.Named<Gauge>(window, "LoadDial").Bounds.Width));

        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((160, 134), (Find.Named<Gauge>(window, "RpmDial").Bounds.Width, Find.Named<Gauge>(window, "LoadDial").Bounds.Width));
    }

    [AvaloniaFact]
    public void BandReadingsAreRounded()
    {
        var (window, shell) = ShowLive();
        shell.Live.IsConnected = true;

        Sample(shell.Live, ("N", 2480.4), ("MAFIGA", 112.41), ("IGAAVG", 31.13), ("GEAR", 4), ("VS", 77.3));

        Assert.Equal(("2480", "112.4", "31.1"), (Text(window, "ReadoutRpm"), Text(window, "ReadoutLoad"), Text(window, "ReadoutAdvance")));
        Assert.Equal("77", Text(window, "SpeedReadout"));
        Assert.Equal(4, shell.Live.Gear);
        Assert.Equal(4, Find.Named<GearSlots>(window, "GearSlots").Gear);
        Assert.Equal(77.3, shell.Live.VehicleSpeed);
    }

    [AvaloniaFact]
    public void MissingValueShowsNa()
    {
        var (window, shell) = ShowLive();
        shell.Live.IsConnected = true;
        Sample(shell.Live, ("N", 2480), ("MAFIGA", 112.4), ("IGAAVG", 31.1), ("GEAR", 4), ("VS", 77));

        // Load is missing from the next reply, and the gear reads 0 (neutral or clutch in).
        Sample(shell.Live, ("N", 2500), ("IGAAVG", 30.0), ("GEAR", 0), ("VS", 77));

        Assert.Equal("n/a", Text(window, "ReadoutLoad"));
        Assert.Null(shell.Live.Gear);
        Assert.Equal("n/a", shell.Live.GearText);
        Assert.Null(Find.Named<GearSlots>(window, "GearSlots").Gear);
    }

    [AvaloniaFact]
    public void OutOfScaleValuePrintsTrueValue()
    {
        var (window, shell) = ShowLive();
        shell.Live.IsConnected = true;

        Sample(shell.Live, ("N", 9000), ("MAFIGA", 112.4), ("IGAAVG", -40));

        Assert.Equal("9000", Text(window, "ReadoutRpm"));
        Assert.Equal(1, Find.Named<ReadoutBar>(window, "RpmBar").FillFraction);
        Assert.Equal("-40.0", Text(window, "ReadoutAdvance"));
        Assert.Equal(0, Find.Named<ReadoutBar>(window, "AdvanceBar").FillFraction);
    }

    [AvaloniaFact]
    public void RevLimitCaptionFollowsTheBin()
    {
        var (window, shell) = ShowLive();

        shell.Live.SetRedline(7104);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Rev limit 7104 rpm, read from the open bin", shell.Live.RevLimitCaption);
        Assert.Equal(7104, Find.Named<ReadoutBar>(window, "RpmBar").LimitFrom);

        shell.Live.SetRedline(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Rev limit: open a bin to read it", shell.Live.RevLimitCaption);
        Assert.Null(Find.Named<ReadoutBar>(window, "RpmBar").LimitFrom);
    }

    [AvaloniaFact]
    public void IdleBandHasNoOrange()
    {
        var (window, shell) = ShowLive();
        shell.Live.SetRedline(7104);

        Assert.Equal(0, OrangePixels(window));
        shell.IsDials = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, OrangePixels(window));

        // The scan does find the fill once samples arrive, so the zero above is not blindness.
        shell.Live.IsConnected = true;
        Sample(shell.Live, ("N", 2480), ("MAFIGA", 112.4), ("IGAAVG", 31.1), ("GEAR", 4), ("VS", 77));
        Assert.True(OrangePixels(window) > 100);
    }

    [AvaloniaFact]
    public void SwitchingReadoutsKeepsTheRest()
    {
        var (window, shell) = ShowLive();
        var pinned = Find.Named<ItemsControl>(window, "PinnedTiles");
        var tiles = pinned.Items.Count;
        var channels = shell.Live.LiveValues.Count;
        Assert.True(tiles > 0);
        Assert.True(pinned.IsEffectivelyVisible);

        shell.IsDials = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(pinned, Find.Named<ItemsControl>(window, "PinnedTiles"));
        Assert.True(pinned.IsEffectivelyVisible);
        Assert.Equal(tiles, pinned.Items.Count);
        Assert.Equal(channels, shell.Live.LiveValues.Count);
    }

    [AvaloniaFact]
    public void BandFitsBothThemesWidthsAndModes()
    {
        var (window, shell) = ShowLive();
        shell.Live.SetRedline(7104);
        foreach (var connected in new[] { false, true })
        {
            if (connected)
            {
                // The mockups' sample values (system 11.1).
                shell.Live.IsConnected = true;
                Sample(shell.Live, ("N", 2480), ("MAFIGA", 112.4), ("IGAAVG", 31.1), ("GEAR", 4), ("VS", 77), ("AMP", 101.2), ("TCO", 88.5));
            }

            foreach (var mode in new[] { "tiles", "dials" })
            {
                shell.IsTiles = mode == "tiles";
                shell.IsDials = mode == "dials";
                foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
                {
                    shell.IsNight = night;
                    shell.IsDay = !night;
                    foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
                    {
                        window.Width = width;
                        window.Height = height;
                        Dispatcher.UIThread.RunJobs();
                        var band = Find.Named<Control>(window, "ReadoutBand");
                        var right = band.TranslatePoint(new Point(band.Bounds.Width, 0), window)!.Value.X;
                        Assert.True(right <= width - 16 + 0.5, $"{mode} at {width}: the band ends at {right}");
                        Screens.Save(window, $"restyle-live-{mode}-{(connected ? "live" : "idle")}-{theme}-{width}");
                    }
                }
            }
        }
    }

    [AvaloniaFact]
    public void TilesShowReadingsThatChangedWhileHidden()
    {
        // Shown with n/a, hidden by a narrow window, updated, then shown again.
        var (window, shell) = ShowLive();
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();
        shell.Live.IsConnected = true;
        Sample(shell.Live, ("VB", 14.12));

        window.Width = 1440;
        window.Height = 900;
        Dispatcher.UIThread.RunJobs();

        // The reading changed from n/a while the tiles were hidden; it must not keep the width of "n/a".
        var reading = Find.Named<ItemsControl>(window, "PinnedTiles").GetVisualDescendants().OfType<ReadingText>().First(text => text.Value == "14.12");
        Assert.True(reading.Bounds.Width >= reading.DesiredSize.Width - 0.5, $"arranged {reading.Bounds.Width} for {reading.DesiredSize.Width}");
    }

    [AvaloniaFact]
    public void PinnedTilesHideWhenCompact()
    {
        var (window, _) = ShowLive(width: 1024, height: 680);

        // Below 1200 px wide (and 760 px tall) the list holds the pins; a hidden section never builds its tiles.
        Assert.False(Find.Named<StackPanel>(window, "PinsSection").IsEffectivelyVisible);
    }
}
