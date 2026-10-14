using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// Live tuning across the app's own reconnect: the ECU on a port that goes silent at a key-off and comes
/// back, the app reconnecting by itself, and the page putting back what had been sent.
/// </summary>
public sealed class LiveTuningReconnectTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting until " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
