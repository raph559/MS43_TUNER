using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The page's behaviour the final review found missing (I1, I3, I4, I5, I7, I8 and the raised minor 3).</summary>
public sealed class LiveTuningReviewTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private LiveEditStore Store => new(Path.Combine(_folder, "live-edits.json"));

    private async Task<TuneViewModel> Tune()
    {
        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        return tune;
    }

    private async Task<(LiveTuningViewModel Page, FakeOverlayEcu Ecu, TuneViewModel Tune)> Attached(Action<FakeOverlayEcu>? before = null, byte[]? image = null)
    {
        var tune = await Tune();
        var ecu = new FakeOverlayEcu(image ?? File.ReadAllBytes(TuneScreen.Car!)) { Running = true };
        before?.Invoke(ecu);
        var page = new LiveTuningViewModel(new MainWindowViewModel(ports: () => ["COM1"]), tune, Store);
        await page.AttachLine(ecu);
        return (page, ecu, tune);
    }

    private static void Raise(LiveTuningViewModel page, int map, int steps)
    {
        page.Selected = page.Maps[map];
        var editor = page.Editor!;
        editor.Selection = new CellRange(0, 0, editor.Values.Rows - 1, editor.Values.Cols - 1);
        Assert.True(editor.Apply(new EditRequest(EditKind.Step, steps)), editor.Message);
    }

    private static byte[] EcuMap(int index) => File.ReadAllBytes(TuneScreen.Car!).AsSpan(OverlayContract.Maps[index].Address, OverlayContract.Maps[index].Bytes).ToArray();

    [AvaloniaFact]
    public async Task ARefusedSwitch_GoesBackOff_AndTheSwitchesWaitForTheEngine()   // I1
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        await page.SendSelectedAsync();
        await page.SetOnAsync(page.Maps[1], false);
        Assert.True(page.Maps[1].CanSwitch);
        ecu.Stall();
        await page.TickAsync();
        Assert.False(page.Maps[1].CanSwitch);

        var notified = 0;
        page.Maps[1].PropertyChanged += (_, e) => notified += e.PropertyName == nameof(LiveMapRow.IsOn) ? 1 : 0;
        await page.SetOnAsync(page.Maps[1], true);                           // what a press does, refused: not running

        Assert.False(page.Maps[1].IsOn);
        Assert.True(notified > 0);                                            // the switch is told to go back
    }

    [AvaloniaFact]
    public async Task EditsOfMapsThatDidNotChange_SurviveFlashingAnotherBin()          // I3
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, _, _) = await Attached();
        Raise(page, 0, 1);
        Raise(page, 2, -1);                                                    // richer
        var flashed = File.ReadAllBytes(TuneScreen.Car!);
        var map = OverlayContract.Maps[0];
        for (var i = 0; i < map.Bytes; i++)
        {
            flashed[map.Address + i]++;                                        // the timing edit, baked and flashed
        }

        var (again, _, _) = await Attached(image: flashed);

        Assert.True(again.Maps[2].HasUnsent);                                  // the λ edit is still there
        Assert.False(again.Maps[0].HasUnsent);                                 // the timing edit is not applied over the new flash
        Assert.Equal("Edits made against other flashed maps are kept, not applied: ign98_pl.", again.KeptForOtherFlash);
    }

    [AvaloniaFact]
    public async Task FreeSelected_FreesTheMapInTheEcu()                                // I4
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        await page.SendSelectedAsync();
        await page.SetOnAsync(page.Maps[1], false);

        await page.FreeSelectedAsync();

        Assert.Equal(OverlayState.Free, ecu.Peek().Tables[1].State);
        Assert.True(page.Maps[1].HasUnsent);
    }

    [AvaloniaFact]
    public async Task AMapThatCannotBePutBack_SaysWhy()                                 // I5
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        await page.SendSelectedAsync();
        ecu.KeyCycle();
        ecu.Running = true;
        ecu.DropWrites = 2;                                                    // the put-back's writes never arrive, twice

        await page.TickAsync();

        Assert.Equal("ign98_is could not be put back: Integrity.", page.Problem);
    }

    [AvaloniaFact]
    public async Task WhileLookingForTheOverlay_TheGridIsReadOnly()                      // I7
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        var gate = new SemaphoreSlim(0);
        var line = new GatedLine(ecu, gate);

        var attaching = page.AttachLine(line);
        Assert.True(page.IsReadOnly);
        Assert.True(page.Editor!.IsReadOnly);
        Assert.Contains((0, 0), page.Editor.ChangedCells);                     // the page's edits still shown

        gate.Release(1000);
        await attaching;
        Assert.False(page.IsReadOnly);
        Assert.True(page.Maps[1].HasUnsent);
    }

    [AvaloniaFact]
    public async Task CopyToTune_CopiesWhatWasSent_AndNamesTheUnsentMaps()              // I8
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, _, tune) = await Attached();
        Raise(page, 1, 2);
        await page.SendSelectedAsync();
        Raise(page, 1, 1);                                                     // a third step, not sent
        Raise(page, 3, -1);                                                    // never sent

        await page.CopyEditedToTuneAsync();

        var copied = tune.Document!.Image.Bytes.Slice(OverlayContract.Maps[1].Address, 36).ToArray();
        Assert.Equal(EcuMap(1).Select(b => (byte)(b + 2)), copied);           // what was driven
        Assert.Contains("Not copied, never sent: ", page.Problem);
        Assert.Contains(page.Maps[3].Title, page.Problem);
    }

    [AvaloniaFact]
    public async Task IfTheFirstStatusTimesOut_ThePageStillShowsWhatTheEcuHolds()        // raised minor 3
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var held = EcuMap(1).Select(b => (byte)(b + 1)).ToArray();
        var lost = true;
        var (page, ecu, _) = await Attached(ecu =>
        {
            ecu.Request(OverlayContract.Load(1, 0));
            ecu.Request(OverlayContract.Write(1, 0, held));
            ecu.Request(OverlayContract.On(1, OverlayContract.Crc32(held)));
            ecu.FailWhen = r => r is [0x6D, 0x00] && lost && !(lost = false) ? new TimeoutException() : null;
        });

        await page.TickAsync();

        Assert.Equal("LIVE", page.Maps[1].StateText);
        page.Selected = page.Maps[1];
        Assert.Equal(36, page.Editor!.ChangedCells.Count);                     // the ECU's copy, not the flash
    }

    /// <summary>A line whose answers wait for the test: detection can be caught half-way.</summary>
    private sealed class GatedLine(IDs2Line inner, SemaphoreSlim gate) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            gate.Wait();
            return inner.Request(payload, listenMilliseconds);
        }
    }
}
