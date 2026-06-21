using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class LiveStateTests : IDisposable
{
    private const string Log = @"C:\logs\ms43-20261019-2225.csv";

    private static readonly IReadOnlyList<AdxValueDefinition> Channels = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-live-" + Guid.NewGuid().ToString("N"));

    public LiveStateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void Sample(MainWindowViewModel live, params (string Id, double Value)[] values) =>
        live.ShowSample(values.Select(value => new DecodedValue(Channels.Single(channel => channel.Id == value.Id), value.Value)).ToList());

    // A session logging to the mockups' file on COM1, on a clock the test moves.
    private static (MainWindowViewModel Live, ManualTime Time) Logging()
    {
        var time = new ManualTime();
        var live = new MainWindowViewModel(time, () => ["COM1"]) { SelectedVariant = "ms43x", SelectedPort = "COM1", EnableCsvLogging = true, CsvPath = Log };
        live.BeginSession();
        return (live, time);
    }

    [AvaloniaFact]
    public void NoPortSaysPlugInTheCable()
    {
        var live = new MainWindowViewModel(new ManualTime(), () => []);

        Assert.Equal("No COM port found. Plug in the K-line cable, then rescan ports.", live.Banner);
        Assert.Equal(LiveAction.RescanPorts, live.BannerAction);
        Assert.Equal("Rescan ports", live.BannerActionText);
    }

    [AvaloniaFact]
    public void LastPortIsPreselected()
    {
        var settings = AppSettings.Load(_folder);
        settings.LastPort = "COM3";
        var shell = new ShellViewModel(new MainWindowViewModel(new ManualTime(), () => ["COM1", "COM3"]), new TuneViewModel(settings));
        Assert.Equal("COM3", shell.Live.SelectedPort);

        settings.LastPort = "COM9";
        var unplugged = new ShellViewModel(new MainWindowViewModel(new ManualTime(), () => ["COM1", "COM3"]), new TuneViewModel(settings));
        Assert.Equal("COM1", unplugged.Live.SelectedPort);
    }

    [AvaloniaFact]
    public void StartCsvOnConnectSetsTheSwitch()
    {
        var settings = AppSettings.Load(_folder);
        settings.StartCsvOnConnect = true;
        Assert.True(new ShellViewModel(new MainWindowViewModel(new ManualTime(), () => ["COM1"]), new TuneViewModel(settings)).Live.EnableCsvLogging);

        settings.StartCsvOnConnect = false;
        Assert.False(new ShellViewModel(new MainWindowViewModel(new ManualTime(), () => ["COM1"]), new TuneViewModel(settings)).Live.EnableCsvLogging);
    }

    // Connect opens the log before the port; a port name no machine has fails the connect there, without touching a device.
    private const string NoSuchPort = "MS43-TEST-NO-PORT";

    private static MainWindowViewModel Connecting(string folder) =>
        new(new ManualTime(), () => [NoSuchPort]) { SelectedVariant = "ms43x", SelectedPort = NoSuchPort, LogFolder = folder, EnableCsvLogging = true };

    [AvaloniaFact]
    public async Task ReconnectNeverOverwritesALog()
    {
        // The clock holds still, so the next log would take the same second's name.
        var live = Connecting(_folder);
        var first = live.CsvPath;
        await live.ConnectAsync();
        File.AppendAllText(first, "2026-10-19T22:25:00.000Z,0" + Environment.NewLine);
        var kept = File.ReadAllText(first);

        await live.ConnectAsync();

        Assert.Equal(kept, File.ReadAllText(first));
        Assert.True(File.Exists(Path.Combine(_folder, Path.GetFileNameWithoutExtension(first) + "-2.csv")));
    }

    [AvaloniaFact]
    public async Task MissingLogFolderIsCreated()
    {
        var folder = Path.Combine(_folder, "MS43 Logs", "2026");
        var live = Connecting(folder);
        var path = live.CsvPath;

        await live.ConnectAsync();

        Assert.True(File.Exists(path));
        Assert.Equal(folder, Path.GetDirectoryName(path));
        Assert.Matches(@"^ms43-\d{8}-\d{6}\.csv$", Path.GetFileName(path));
        Assert.StartsWith("timestamp_utc,sample_index", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void ConnectFailureShowsBannerNotAWindow()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder));
        window.Show();
        var requested = false;
        shell.Live.ShowConnectionLogRequested += (_, _) => requested = true;
        shell.Live.SelectedPort = "COM1";

        shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Not connected: COM1 is in use by another program. Close it, then connect.", shell.Live.Banner);
        Assert.True(Find.Named<Border>(window, "LiveBanner").IsEffectivelyVisible);
        Assert.False(requested);
        Assert.Empty(window.OwnedWindows);
        Assert.NotNull(shell.Live.ConnectionLog);
        Screens.Save(window, "restyle-live-night-1440-connect-failed");
    }

    [AvaloniaFact]
    public void ThreeTimeoutsShowTheLoggingStoppedBanner()
    {
        var (live, time) = Logging();
        Sample(live, ("N", 2480));
        live.SampleCount = 9576;
        time.Advance(TimeSpan.FromSeconds(8));

        live.ReportLoggingStopped(new TimeoutException("No reply."));

        Assert.Equal("Logging stopped: the ECU stopped answering on COM1 (3 timeouts in a row). ms43-20261019-2225.csv is closed with 9576 rows.", live.Banner);
        Assert.Equal("The values below are from the last sample, 8 s ago.", live.BannerDetail);
        Assert.False(live.IsConnected);
        Assert.True(live.IsStale);
        Assert.Equal("2480", live.Gauges[0].Display);

        time.Advance(TimeSpan.FromSeconds(1));
        live.RefreshStaleLine();
        Assert.Equal("The values below are from the last sample, 9 s ago.", live.BannerDetail);
    }

    [AvaloniaFact]
    public void NoSampleBeforeTheTimeoutsMeansBaudRefused()
    {
        var (live, _) = Logging();

        live.ReportLoggingStopped(new TimeoutException("No reply."));

        Assert.Equal("Not connected: the ECU refused the faster baud rate. Turn the engine off, connect, then start the engine.", live.Banner);
        Assert.Null(live.BannerDetail);
        Assert.False(live.IsStale);
    }

    [AvaloniaFact]
    public void CsvWriteFailureKeepsTheRows()
    {
        var (live, time) = Logging();
        Sample(live, ("N", 2480));
        time.Advance(new TimeSpan(0, 4, 12));

        live.ReportLoggingStopped(new CsvWriteException(Log, new IOException("There is not enough space on the disk.")));

        Assert.Equal("Logging stopped: writing ms43-20261019-2225.csv failed (There is not enough space on the disk). The rows written before 00:04:12 are kept.", live.Banner);
        Assert.Equal(LiveAction.Browse, live.BannerAction);
        Assert.Equal("Browse…", live.BannerActionText);
    }

    [AvaloniaFact]
    public void RetryShowsInTheStatusLine()
    {
        var (live, _) = Logging();

        live.ShowRetrying(1);
        Assert.Equal("The ECU did not answer (1 of 3). Trying again.", string.Concat(live.StatusParts.Select(part => part.Text)));

        live.ShowResumed();
        Assert.StartsWith("Connected to COM1", string.Concat(live.StatusParts.Select(part => part.Text)));
    }
}
