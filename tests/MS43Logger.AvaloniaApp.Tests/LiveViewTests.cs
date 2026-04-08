using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class LiveViewTests
{
    private static string? Find(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "testdata", "local", name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static (MainWindow Window, MainWindowViewModel Live) ShowLive(string variant)
    {
        var window = new MainWindow();
        var shell = (ShellViewModel)window.DataContext!;
        shell.Live.SelectedVariant = variant;
        shell.Live.PreviewChannels();
        window.Show();
        return (window, shell.Live);
    }

    [AvaloniaFact]
    public void LiveViewShowsThreeDialsForMs43x()
    {
        var (window, live) = ShowLive("ms43x");

        Assert.Equal(new[] { "N", "MAFIGA", "IGAAVG" }, live.Gauges.Select(gauge => gauge.ChannelId));
        Assert.Equal(3, window.GetVisualDescendants().OfType<Gauge>().Count(gauge => gauge.IsEffectivelyVisible));
        Assert.True(live.LiveValues.Count > 50);
    }

    [AvaloniaFact]
    public void StockDefinitionUsesStockChannelNames()
    {
        var (_, live) = ShowLive("b25");

        Assert.Equal("RPM", live.Gauges[0].ChannelId);
        Assert.Equal("IGNITIONAVG", live.Gauges[2].ChannelId);
    }

    [AvaloniaFact]
    public void RedlineFollowsOpenTune()
    {
        var bin = Find("CAR_current_43X001_SP95_STAGE1.bin");
        var xdf = Find("Siemens_MS43_MS43X001_512K.xdf");
        if (bin is null || xdf is null)
        {
            return;   // real files are local only; the Core tests report them as skipped
        }

        var (_, live) = ShowLive("ms43x");
        live.SetRedline(GaugeViewModel.RedlineFrom(CalibrationDocument.Open(bin, xdf)));
        Assert.Equal(7104, live.Gauges[0].RedFrom);

        live.SetRedline(GaugeViewModel.RedlineFrom(null));
        Assert.Null(live.Gauges[0].RedFrom);
    }

    [AvaloniaFact]
    public void DrivingSamplesFillDialsAndSparklines()
    {
        var (window, live) = ShowLive("ms43x");
        live.SetRedline(7104);
        var definition = BundledDefinitions.Load("ms43x");
        var channels = definition.MatchChannels(Array.Empty<string>());
        live.IsConnected = true;

        for (var i = 0; i < 200; i++)
        {
            var payload = new byte[73];
            var rpm = 1800 + i * 20;
            payload[0x04] = (byte)(rpm & 0xFF);
            payload[0x05] = (byte)(rpm >> 8);
            payload[0x14] = (byte)(i * 3);
            payload[0x2C] = (byte)(140 - i / 4);
            live.ShowSample(definition.DecodePayload(payload, channels));
        }

        Assert.All(live.Gauges, gauge => Assert.NotNull(gauge.Value));
        Assert.Contains(live.LiveValues, value => value.History.Count == 200);
        Screens.Save(window, "live-night");
    }
}
