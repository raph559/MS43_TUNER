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

    private async Task<(MainWindowViewModel Live, LiveTuningViewModel Page, FakeOverlayEcu Ecu, EcuPort Port)> Connected()
    {
        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        var ecu = new FakeOverlayEcu(File.ReadAllBytes(TuneScreen.Car!)) { Running = true };
        var port = new EcuPort(BundledDefinitions.Load("ms43x"))
        {
            Other = frame =>
            {
                try
                {
                    return ecu.Request(frame[2..^1]);
                }
                catch (TimeoutException)
                {
                    return null;
                }
            },
        };
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x",
            SelectedPort = "COM1",
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
            ReconnectInterval = TimeSpan.FromMilliseconds(20),
        };
        var page = new LiveTuningViewModel(live, tune, new LiveEditStore(Path.Combine(_folder, "live-edits.json")));
        await live.ConnectAsync();
        await Until(() => !page.IsReadOnly, "the overlay is found");
        return (live, page, ecu, port);
    }

    [AvaloniaFact]
    public async Task AKeyCycle_PutsBackWhatWasSent_AndLiveGoesBackOn()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (live, page, ecu, port) = await Connected();
        page.Selected = page.Maps[1];
        page.Editor!.Selection = new CellRange(0, 0, page.Editor.Values.Rows - 1, page.Editor.Values.Cols - 1);
        Assert.True(page.Editor.Apply(new EditRequest(EditKind.Step, 2)), page.Editor.Message);
        await page.SendSelectedAsync();
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);

        port.Silent = true;                                                   // the key goes off
        await Until(() => live.IsReconnecting, "the app is reconnecting");
        Assert.True(page.IsReadOnly);
        Assert.Equal("Reconnecting to the ECU…", page.ReadOnlyReason);
        ecu.KeyCycle();                                                       // the ECU's RAM is wiped
        port.Silent = false;                                                  // the key comes back on
        await Until(() => live.IsConnected && !page.IsReadOnly, "reconnected and the overlay found again");
        ecu.Running = true;                                                   // the engine starts
        await page.TickAsync();

        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        Assert.Equal("LIVE", page.Maps[1].StateText);
        Assert.False(page.Maps[1].HasUnsent);
        live.Disconnect();
    }
}
