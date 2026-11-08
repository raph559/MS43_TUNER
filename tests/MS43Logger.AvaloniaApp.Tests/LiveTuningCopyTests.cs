using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Copy to Tune (live tuning spec 5.6): the page's maps into the Tune tab's open bin.</summary>
public sealed class LiveTuningCopyTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(LiveTuningViewModel Page, TuneViewModel Tune)> Attached(string? bin = null)
    {
        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(bin ?? TuneScreen.Car!, TuneScreen.Xdf);
        var ecu = new FakeOverlayEcu(File.ReadAllBytes(TuneScreen.Car!)) { Running = true };
        var page = new LiveTuningViewModel(new MainWindowViewModel(ports: () => ["COM1"]), tune, new LiveEditStore(Path.Combine(_folder, "live-edits.json")));
        await page.AttachLine(ecu);
        page.Selected = page.Maps[1];
        var editor = page.Editor!;
        editor.Selection = new CellRange(0, 0, editor.Values.Rows - 1, editor.Values.Cols - 1);
        Assert.True(editor.Apply(new EditRequest(EditKind.Step, 2)), editor.Message);
        await page.SendSelectedAsync();
        return (page, tune);
    }

    private static byte[] Map(BinImage image, int index) =>
        image.Bytes.Slice(OverlayContract.Maps[index].Address, OverlayContract.Maps[index].Bytes).ToArray();

    [AvaloniaFact]
    public async Task CopyToTune_WritesTheLiveValues_AsOneUndoableStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, tune) = await Attached();
        var flashed = Map(tune.Document!.Image, 1);
        var undos = tune.History!.UndoCount;

        await page.CopyToTuneAsync([page.Maps[1]]);

        Assert.Equal(flashed.Select(b => (byte)(b + 2)), Map(tune.Document.Image, 1));
        Assert.Equal(undos + 1, tune.History.UndoCount);
        Assert.True(tune.History.Undo());
        Assert.Equal(flashed, Map(tune.Document.Image, 1));
    }

    [AvaloniaFact]
    public async Task CopyToTune_IntoThePlainCarFile_Warns()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, _) = await Attached();
        Assert.Equal("This bin is not the overlay bin: flashing it removes live tuning from the car.", await page.CopyToTuneAsync([page.Maps[1]]));
    }

    [AvaloniaFact]
    public async Task CopyToTune_IntoTheOverlayBin_DoesNotWarn()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        var overlay = File.ReadAllBytes(TuneScreen.Car!);
        "LTOV"u8.CopyTo(overlay.AsSpan(0x4B544));
        var bin = Path.Combine(_folder, "overlay.bin");
        File.WriteAllBytes(bin, overlay);
        var (page, _) = await Attached(bin);
        Assert.Null(await page.CopyToTuneAsync([page.Maps[1]]));
    }

    [AvaloniaFact]
    public async Task CopyEditedToTune_CopiesEveryEditedMap_AndSaysWhatToDoNext()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, tune) = await Attached();
        var flashed = Map(tune.Document!.Image, 1);

        await page.CopyEditedToTuneAsync();

        Assert.Equal(flashed.Select(b => (byte)(b + 2)), Map(tune.Document.Image, 1));
        Assert.Equal("Copied 1 map to the Tune tab: save the bin there. This bin is not the overlay bin: flashing it removes live tuning from the car.", page.Problem);
    }

    [AvaloniaFact]
    public async Task CopyToTune_TouchesNoChecksum()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (page, tune) = await Attached();
        var before = tune.Document!.Image.ToArray();

        await page.CopyToTuneAsync([page.Maps[1]]);

        var after = tune.Document.Image.ToArray();
        var map = OverlayContract.Maps[1];
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.InRange(i, map.Address, map.Address + map.Bytes - 1));
    }
}
