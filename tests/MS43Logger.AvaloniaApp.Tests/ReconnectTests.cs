using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class ReconnectTests : IDisposable
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-reconnect-" + Guid.NewGuid().ToString("N"));

    public ReconnectTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting until " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private MainWindowViewModel Logging(EcuPort port, string csv) =>
        new(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x",
            SelectedPort = "COM1",
            EnableCsvLogging = true,
            CsvPath = csv,
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
            ReconnectInterval = TimeSpan.FromMilliseconds(20),
        };

    [AvaloniaFact]
    public async Task AKeyOff_KeepsTheLogOpen_AndReconnects()
    {
        var port = new EcuPort(Ms43x);
        var csv = Path.Combine(_folder, "ms43-reconnect.csv");
        var live = Logging(port, csv);
        var lineChanges = 0;
        live.LineChanged += (_, _) => lineChanges++;

        await live.ConnectAsync();
        Assert.NotNull(live.Line);
        await Until(() => live.SampleCount >= 3, "three samples");

        port.Silent = true;
        await Until(() => live.IsReconnecting, "reconnecting");
        Assert.False(live.IsConnected);
        Assert.Null(live.Line);
        Assert.Equal("Disconnect", live.ConnectionButtonText);
        var before = live.SampleCount;

        port.Silent = false;
        await Until(() => live.IsConnected && live.SampleCount >= before + 3, "samples after the reconnect");
        Assert.False(live.IsReconnecting);
        Assert.NotNull(live.Line);
        Assert.Equal(csv, live.CsvPath);
        Assert.True(port.Connects >= 2);
        Assert.Equal(3, lineChanges);                                       // up, down, up again

        live.Disconnect();
        var lines = File.ReadAllLines(csv);
        Assert.Single(lines, l => l.StartsWith("timestamp_utc", StringComparison.Ordinal));
        Assert.True(lines.Length - 1 >= before + 3, $"{lines.Length - 1} rows");
    }

    [AvaloniaFact]
    public void ThreeTimeouts_ShowTheReconnectingBanner()
    {
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x", SelectedPort = "COM1", EnableCsvLogging = true, CsvPath = @"C:\logs\ms43-20261019-2225.csv",
            SessionFactory = (_, _) => throw new TimeoutException("No reply."),
            ReconnectInterval = TimeSpan.FromHours(1),
        };
        live.BeginSession();
        live.SampleCount = 9576;

        live.ReportLoggingStopped(new TimeoutException("No reply."));

        Assert.Equal("Reconnecting: the ECU stopped answering on COM1. Trying again every second; ms43-20261019-2225.csv stays open with 9576 rows.", live.Banner);
        Assert.True(live.IsReconnecting);
        Assert.False(live.IsConnected);
        Assert.Equal("Disconnect", live.ConnectionButtonText);
        live.Disconnect();
    }

    [AvaloniaFact]
    public async Task Disconnect_StopsTheRetries()
    {
        var port = new EcuPort(Ms43x);
        var live = Logging(port, Path.Combine(_folder, "ms43-stop.csv"));
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 1, "a sample");
        port.Silent = true;
        await Until(() => live.IsReconnecting, "reconnecting");

        live.Disconnect();
        port.Silent = false;
        var connects = port.Connects;
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        Assert.False(live.IsReconnecting);
        Assert.False(live.IsConnected);
        Assert.Equal(connects, port.Connects);
        Assert.Equal("Connect", live.ConnectionButtonText);
    }

    [AvaloniaFact]
    public void NoSampleBeforeTheTimeouts_StillStops()
    {
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"]) { SelectedVariant = "ms43x", SelectedPort = "COM1" };
        live.BeginSession();

        live.ReportLoggingStopped(new TimeoutException("No reply."));

        Assert.Equal("Not connected: the ECU refused the faster baud rate. Turn the engine off, connect, then start the engine.", live.Banner);
        Assert.False(live.IsReconnecting);
    }
}
