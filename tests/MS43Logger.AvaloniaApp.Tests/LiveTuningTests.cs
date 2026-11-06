using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Live tuning page's model over the car's bin and a fake ECU holding the overlay with that bin's maps.</summary>
public sealed class LiveTuningTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(LiveTuningViewModel Page, FakeOverlayEcu Ecu, TuneViewModel Tune)> Attached(bool hasOverlay = true, byte[]? ecuImage = null,
        int version = OverlayV10.Version, int minor = OverlayV10.Minor)
    {
        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        var ecu = new FakeOverlayEcu(ecuImage ?? File.ReadAllBytes(TuneScreen.Car!))
        {
            Running = true, HasOverlay = hasOverlay, HeaderVersion = version, Minor = minor,
        };
        var page = new LiveTuningViewModel(new MainWindowViewModel(ports: () => ["COM1"]), tune, new LiveEditStore(Path.Combine(_folder, "live-edits.json")));
        await page.AttachLine(ecu);
        return (page, ecu, tune);
    }

    /// <summary>Every cell of the selected map raised by <paramref name="steps"/> raw steps.</summary>
    private static void Raise(LiveTuningViewModel page, int map, int steps)
    {
        page.Selected = page.Maps[map];
        var editor = page.Editor!;
        editor.Selection = new CellRange(0, 0, editor.Values.Rows - 1, editor.Values.Cols - 1);
        Assert.True(editor.Apply(new EditRequest(EditKind.Step, steps)), editor.Message);
    }

    [AvaloniaFact]
    public void WithoutAConnection_ThePageIsReadOnlyAndSaysWhy()
    {
        var page = new LiveTuningViewModel(new MainWindowViewModel(ports: () => ["COM1"]), new TuneViewModel(AppSettings.Load(_folder)),
            new LiveEditStore(Path.Combine(_folder, "live-edits.json")));
        Assert.True(page.IsReadOnly);
        Assert.Equal("Not connected.", page.ReadOnlyReason);
        Assert.False(page.HasVerifiedCatalogue);
        Assert.Equal("Connect to check live-map support.", page.CatalogueText);
        Assert.Equal(new[] { "Timing", "Timing", "Fuel", "VANOS", "VANOS" }, page.Maps.Select(m => m.Group));
        Assert.All(page.Maps, m => Assert.Equal("flashed", m.StateText));
    }

    [AvaloniaFact]
    public async Task WithoutTheOverlay_No6DIsSent()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached(hasOverlay: false);
        Assert.True(page.IsReadOnly);
        Assert.Equal("No live-tuning overlay on this ECU.", page.ReadOnlyReason);
        Assert.False(page.HasVerifiedCatalogue);
        Assert.Equal("No live-tuning overlay on this ECU.", page.CatalogueText);
        Assert.All(ecu.Requests, r => Assert.Equal(0x06, r[0]));
    }

    [AvaloniaFact]
    public async Task AnOlderOverlay_IsReadOnlyWithAnExplicitUpgradeReason()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        foreach (var (version, minor, label) in new[] { (2, 0, "2"), (9, 0, "9"), (10, 2, "10.2") })
        {
            var (page, ecu, _) = await Attached(version: version, minor: minor);
            Assert.True(page.IsReadOnly);
            Assert.Equal($"The ECU runs live-tuning overlay V{label}; this app works with V10.4 only. "
                + "Flash the V10.4 bin to tune live. No live commands will be sent.", page.ReadOnlyReason);
            Assert.False(page.HasVerifiedCatalogue);
            Assert.Equal($"Overlay V{label} not supported: flash the V10.4 bin.", page.CatalogueText);
            Assert.All(ecu.Requests, r => Assert.Equal(0x06, r[0]));
        }
    }

    [CarAvaloniaFact]
    public async Task FourOfTheFirstMapsAndFullMapEditsRemainLive()
    {
        Assert.True(TuneScreen.Available, "This test needs the local car bin and XDF.");
        var (page, ecu, _) = await Attached();
        Assert.False(page.IsReadOnly);
        Assert.Contains("Overlay 10.4 found", page.LinkText);
        Assert.True(page.HasVerifiedCatalogue);
        int[] maps = [1, 2, 3, 4];                                           // the five together need 932 bytes, the ECU has 896
        foreach (var i in maps) Raise(page, i, i == 2 ? 1 : 9);              // whole maps, beyond protocol 2's old +/-8 steps
        await page.SendAllAsync();
        Assert.All(maps, i => Assert.Equal(OverlayState.On, ecu.Peek().Tables[i].State));
        Assert.All(page.Maps, map => Assert.False(map.HasUnsent));
        var start = ecu.Requests.Count;
        foreach (var i in maps) Raise(page, i, 1);
        await page.SendAllAsync();
        var writes = ecu.Requests.Skip(start).Where(p => p[0] == 0x6D && p[1] != 0).ToArray();
        Assert.NotEmpty(writes);
        Assert.All(writes, p => Assert.Equal(9, p[1]));
        Assert.All(maps, i => Assert.Equal(OverlayState.On, ecu.Peek().Tables[i].State));
        Assert.All(page.Maps, map => Assert.False(map.HasUnsent));
    }

    [CarAvaloniaFact]
    public async Task AnInterruptedEditStaysMarkedUnsentUntilTheRemainingGroupsAreSent()
    {
        Assert.True(TuneScreen.Available, "This test needs the local car bin and XDF.");
        var (page, ecu, _) = await Attached();
        Raise(page, 0, 1);
        await page.SendSelectedAsync();
        Raise(page, 0, 1);
        var count = 0;
        ecu.FailWhen = request => request is [0x6D, 9, ..] && ++count >= 2 ? new TimeoutException() : null;
        await page.SendSelectedAsync();
        ecu.FailWhen = null;
        await page.TickAsync();
        Assert.True(page.Maps[0].HasUnsent);
        Assert.True(page.Maps[0].IsOn);
        await page.SendSelectedAsync();
        Assert.False(page.Maps[0].HasUnsent);
        Assert.True(page.Maps[0].IsOn);
    }

    [AvaloniaFact]
    public async Task AnEditIsUnsentUntilSend_ThenLive()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Assert.False(page.IsReadOnly);
        Assert.Contains("Overlay 10.4 found · engine running", page.LinkText);
        Assert.True(page.HasVerifiedCatalogue);
        Assert.Equal("135 supported maps + 7 settings groups · load while RAM fits", page.CatalogueText);

        Raise(page, 1, 2);
        Assert.True(page.Maps[1].HasUnsent);
        Assert.Equal("flashed · unsent changes", page.Maps[1].StateText);
        Assert.DoesNotContain(ecu.Requests, r => r is [0x6D, 0x01, ..]);

        await page.SendSelectedAsync();

        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        Assert.Equal("LIVE", page.Maps[1].StateText);
        Assert.False(page.Maps[1].HasUnsent);
        Assert.True(page.Maps[1].IsOn);
    }

    [AvaloniaFact]
    public async Task TheBigSwitch_FlipsEveryMapInTheEcu()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        Raise(page, 3, -1);
        await page.SendAllAsync();
        Assert.True(page.AllEdited);

        await page.SetAllEditedAsync(false);
        Assert.Equal(new[] { OverlayState.Ready, OverlayState.Ready }, new[] { ecu.Peek().Tables[1].State, ecu.Peek().Tables[3].State });
        Assert.False(page.AllEdited);
        Assert.Equal("sent, off", page.Maps[1].StateText);

        await page.SetAllEditedAsync(true);
        Assert.Equal(new[] { OverlayState.On, OverlayState.On }, new[] { ecu.Peek().Tables[1].State, ecu.Peek().Tables[3].State });
    }

    [AvaloniaFact]
    public async Task RevertAll_ClearsTheEcu_AndKeepsTheEditsUnsent()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        Raise(page, 1, 2);
        await page.SendSelectedAsync();

        await page.RevertAllAsync();

        Assert.All(ecu.Peek().Tables, t => Assert.Equal(OverlayState.Free, t.State));
        Assert.Equal("flashed · unsent changes", page.Maps[1].StateText);
    }

    [AvaloniaFact]
    public async Task ALeanerLambdaCell_IsSent()                                // protocol 2's richer-only rule is gone
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, ecu, _) = await Attached();
        page.Selected = page.Maps[2];
        var values = page.Editor!.Values;
        var (row, col) = (from r in Enumerable.Range(0, values.Rows) from c in Enumerable.Range(0, values.Cols)
                          where values.Raw[r, c] > 128 select (r, c)).First();
        page.Editor.Selection = new CellRange(row, col, row, col);
        // A step moves λ as shown: +1 raises it (leaner), the raw byte going down.
        Assert.True(page.Editor.Apply(new EditRequest(EditKind.Step, 1)), page.Editor.Message);
        var sent = ecu.Requests.Count;

        await page.SendSelectedAsync();

        Assert.Null(page.Problem);
        Assert.True(ecu.Requests.Count > sent);
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[2].State);
    }

    [AvaloniaFact]
    public async Task EditsSurviveARestartOfThePage()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, _, _) = await Attached();
        Raise(page, 1, 2);
        var (again, _, _) = await Attached();
        Assert.True(again.Maps[1].HasUnsent);
        again.Selected = again.Maps[1];
        Assert.Equal(again.Editor!.Values.Rows * again.Editor.Values.Cols, again.Editor.ChangedCells.Count);
    }

    [AvaloniaFact]
    public async Task TheEcusOtherMaps_AreSaid()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var other = File.ReadAllBytes(TuneScreen.Car!);
        other[OverlayContract.Maps[0].Address] ^= 0x01;
        var (page, _, _) = await Attached(ecuImage: other);
        Assert.Equal("The ECU's maps differ from the open bin: edits start from the ECU's.", page.BinDiffers);
    }

    [AvaloniaFact]
    public async Task TheFlamesRow_StaysEditable()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, _, _) = await Attached();
        page.Selected = page.Maps[0];
        var editor = page.Editor!;
        var last = editor.Values.Rows - 1;                                  // the 6,990 rpm row
        editor.Selection = new CellRange(last, 0, last, 0);
        Assert.True(editor.Apply(new EditRequest(EditKind.Step, 1)), editor.Message);
        Assert.Contains((last, 0), editor.ChangedCells);
    }
}
