using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The CSV log switched on and off while connected: it starts and stops at once, each start a new file.</summary>
public sealed class LogSwitchTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public LogSwitchTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting until " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private MainWindowViewModel Live(bool logging) => new(new ManualTime(), () => ["COM1"])
    {
        SelectedVariant = "ms43x", SelectedPort = "COM1", LogFolder = _folder, EnableCsvLogging = logging,
        SessionFactory = (definition, _) => new Ds2Session(definition, new EcuPort(BundledDefinitions.Load("ms43x"))),
    };

    [AvaloniaFact]
    public async Task TurningTheLogOn_WhileConnected_StartsWritingAtOnce()
    {
        var live = Live(logging: false);
        live.CsvPath = live.NextCsvPath;
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 2, "two samples");

        live.EnableCsvLogging = true;
        var path = live.CsvPath;
        var from = live.SampleCount;
        await Until(() => live.SampleCount >= from + 3, "three more samples");
        live.Disconnect();

        var lines = File.ReadAllLines(path);
        Assert.StartsWith("timestamp_utc,sample_index,", lines[0]);
        Assert.True(lines.Length >= 3, $"{lines.Length - 1} rows");
    }

    [AvaloniaFact]
    public async Task TurningTheLogOff_StopsIt_AndTurningItOnAgain_StartsANewFile()
    {
        var live = Live(logging: true);
        live.CsvPath = live.NextCsvPath;
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 2, "two samples");

        var first = live.CsvPath;
        live.EnableCsvLogging = false;
        var rows = File.ReadAllLines(first).Length;
        var from = live.SampleCount;
        await Until(() => live.SampleCount >= from + 3, "three more samples");
        Assert.Equal(rows, File.ReadAllLines(first).Length);

        live.EnableCsvLogging = true;
        var second = live.CsvPath;
        Assert.NotEqual(first, second);
        from = live.SampleCount;
        await Until(() => live.SampleCount >= from + 3, "three more samples");
        live.Disconnect();

        Assert.True(File.ReadAllLines(second).Length >= 3);
    }

    [AvaloniaFact]
    public void TheLogButton_SaysWhatItWillDo()
    {
        var live = Live(logging: false);
        Assert.Equal("Start log", live.LogButtonText);
        live.EnableCsvLogging = true;
        Assert.Equal("Stop log", live.LogButtonText);
    }
}
