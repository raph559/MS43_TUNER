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

public sealed class ChannelListTests
{
    private static readonly IReadOnlyList<AdxValueDefinition> Channels = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());

    private static (MainWindow Window, ShellViewModel Shell, ManualTime Time) ShowLive(int width = 1440, int height = 900)
    {
        var time = new ManualTime();
        var (window, shell) = TestShell.Create(live: new MainWindowViewModel(time, () => ["COM1"]));
        shell.Live.SelectedVariant = "ms43x";
        window.Width = width;
        window.Height = height;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell, time);
    }

    private static void Sample(MainWindowViewModel live, params (string Id, double Value)[] values)
    {
        live.ShowSample(values.Select(value => new DecodedValue(Channels.Single(channel => channel.Id == value.Id), value.Value)).ToList());
        Dispatcher.UIThread.RunJobs();
    }

    private static DecodedValueViewModel Row(MainWindowViewModel live, string id) => live.PinnedValues.Concat(live.OtherValues).Single(row => row.Id == id);

    private static int OrangePixels(Window window, params Control[] areas)
    {
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var count = 0;
        foreach (var area in areas)
        {
            var origin = area.TranslatePoint(default, window)!.Value;
            for (var y = Math.Max(0, (int)origin.Y); y < Math.Min(buffer.Size.Height, (int)(origin.Y + area.Bounds.Height)); y++)
            {
                for (var x = Math.Max(0, (int)origin.X); x < Math.Min(buffer.Size.Width, (int)(origin.X + area.Bounds.Width)); x++)
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
        }

        return count;
    }

    [AvaloniaFact]
    public void PinnedChannelsSitInTheLeftColumn()
    {
        var (window, shell, _) = ShowLive();
        var pinned = Find.Named<ListBox>(window, "PinnedList");
        var other = Find.Named<ListBox>(window, "OtherList");

        Assert.NotEmpty(shell.Live.PinnedValues);
        Assert.All(shell.Live.PinnedValues, row => Assert.True(row.IsPinned));
        Assert.All(shell.Live.OtherValues, row => Assert.False(row.IsPinned));
        Assert.Equal(shell.Live.PinnedValues.Count, pinned.ItemCount);
        var left = pinned.TranslatePoint(default, window)!.Value;
        var right = other.TranslatePoint(default, window)!.Value;
        Assert.True(left.X < right.X);
        Assert.Equal(left.Y, right.Y, 0.5);
        Screens.Save(window, "restyle-live-night-1440-list");
    }

    [AvaloniaFact]
    public void OneColumnBelow1200()
    {
        var (window, _, _) = ShowLive(1024, 680);
        var pinned = Find.Named<ListBox>(window, "PinnedList");
        var other = Find.Named<ListBox>(window, "OtherList");

        var top = pinned.TranslatePoint(default, window)!.Value;
        var below = other.TranslatePoint(default, window)!.Value;
        Assert.Equal(top.X, below.X, 0.5);
        Assert.True(below.Y >= top.Y + pinned.Bounds.Height - 0.5, "Not pinned follows Pinned");
    }

    [AvaloniaFact]
    public void TrendLengthKeepsSixtySeconds()
    {
        var time = new ManualTime();
        var live = new MainWindowViewModel(time, () => ["COM1"]) { SelectedVariant = "ms43x", TrendSeconds = 60 };
        for (var i = 0; i < 70 * 38; i++)
        {
            Sample(live, ("N", 2000 + i % 100));
            time.Advance(TimeSpan.FromSeconds(1.0 / 38));
        }

        var window = Row(live, "N").History.Window(time.GetUtcNow());

        Assert.InRange((time.GetUtcNow() - window[0].Time).TotalSeconds, 59, 60);
        Assert.Equal("Last 60 s", live.TrendLabel);
    }

    [AvaloniaFact]
    public void SparklineIsNeverOrange()
    {
        var (window, shell, time) = ShowLive();
        shell.Live.IsConnected = true;
        for (var i = 0; i < 60; i++)
        {
            Sample(shell.Live, ("N", 2000 + 40 * Math.Sin(i / 5.0)), ("AMP", 101.2), ("TCO", 88.5), ("VB", 14.1));
            time.Advance(TimeSpan.FromSeconds(1.0 / 38));
        }

        var lines = window.GetVisualDescendants().OfType<Sparkline>().Where(line => line.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(lines);
        Assert.Equal(0, OrangePixels(window, lines.Cast<Control>().ToArray()));
    }

    [AvaloniaFact]
    public void FilterWithNoMatchSaysSo()
    {
        var (window, shell, _) = ShowLive();

        shell.Live.SearchText = "xyz";
        Dispatcher.UIThread.RunJobs();

        var line = Find.Named<TextBlock>(window, "NoMatchText");
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("No channel matches \"xyz\".", line.Text);
    }

    [AvaloniaFact]
    public void PinnedOnlyWithNoPinsSaysSo()
    {
        var (window, shell, _) = ShowLive();
        foreach (var row in shell.Live.PinnedValues.ToList())
        {
            shell.Live.TogglePinned(row);
        }

        shell.Live.ShowPinnedOnly = true;
        Dispatcher.UIThread.RunJobs();

        var line = Find.Named<TextBlock>(window, "NoPinsText");
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("No pinned channels. Use the pin on a row to keep it at the top.", line.Text);
    }

    [AvaloniaFact]
    public void MissingChannelShowsNaWithTooltip()
    {
        var (window, shell, _) = ShowLive();
        shell.Live.IsConnected = true;
        Sample(shell.Live, ("N", 2480), ("MAFIGA", 112.41));
        Assert.Equal("112.41", Row(shell.Live, "MAFIGA").Value);

        Sample(shell.Live, ("N", 2500));

        var row = Row(shell.Live, "MAFIGA");
        Assert.Equal("n/a", row.Value);
        Assert.False(row.HasValue);
        var shown = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "RowValue" && ReferenceEquals(text.DataContext, row));
        Assert.Equal("No value in the last sample.", ToolTip.GetTip(shown));

        // The unit goes with the value: n/a stands alone, as the not-connected mockup shows it.
        var unit = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "RowUnit" && ReferenceEquals(text.DataContext, row));
        Assert.False(unit.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void UnitlessChannelsShowNoUnit()
    {
        var (_, shell, _) = ShowLive();

        // These ADX entries hold a name, not a unit, in their units field ("Gear", "Active VE Table").
        Assert.Equal(string.Empty, Row(shell.Live, "GEAR").Units);
        Assert.Equal(string.Empty, Row(shell.Live, "ACTIVEVE").Units);
        Assert.Equal("mg/stk", Row(shell.Live, "MAFIGA").Units);
    }
}
