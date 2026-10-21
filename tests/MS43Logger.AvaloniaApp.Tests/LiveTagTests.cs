using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Logs that say which version of each live map was in use.</summary>
public sealed class LiveTagTests : IDisposable
{
    private static readonly string[] Flashed = ["flashed", "flashed", "flashed", "flashed", "flashed"];

    private readonly string _folder = TestShell.NewFolder();

    public LiveTagTests() => Directory.CreateDirectory(_folder);

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

    [AvaloniaFact]
    public async Task ThePageTags_AreFlashedOrTheLiveCopysVersion()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var tune = new TuneViewModel(AppSettings.Load(_folder));
        await tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        var page = new LiveTuningViewModel(new MainWindowViewModel(ports: () => ["COM1"]), tune, new LiveEditStore(Path.Combine(_folder, "e.json")));
        Assert.Equal(Flashed, page.Tags);
        Assert.Null(page.LiveTagsIfAnyLive);
        Assert.Equal("flashed", page.CsvCell);

        await page.AttachLine(new FakeOverlayEcu(File.ReadAllBytes(TuneScreen.Car!)) { Running = true });
        page.Selected = page.Maps[1];
        page.Editor!.Selection = new CellRange(0, 0, 0, 0);
        page.Editor.Apply(new EditRequest(EditKind.Step, 2));
        await page.SendSelectedAsync();

        var copy = tune.Document!.Image.Bytes.Slice(OverlayContract.Maps[1].Address, 36).ToArray();
        copy[0] = (byte)(copy[0] + 2);
        var version = "v" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(copy))[..8].ToLowerInvariant();
        Assert.Equal(page.Maps.Count, page.Tags.Count);
        Assert.Equal(page.Tags.Select((_, i) => i == 1 ? version : "flashed"), page.Tags);
        Assert.Equal(page.Tags, page.LiveTagsIfAnyLive);
        Assert.Equal(OverlayContract.Maps[1].Key + "=" + version, page.CsvCell);
    }

    [AvaloniaFact]
    public async Task TheCsv_TagsEachRowWithTheLiveMaps()
    {
        var port = new EcuPort(BundledDefinitions.Load("ms43x"));
        var csv = Path.Combine(_folder, "tags.csv");
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x", SelectedPort = "COM1", EnableCsvLogging = true, CsvPath = csv,
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
            LiveMaps = () => "ign98_is=v1a2b3c4d",
        };

        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 2, "two samples");
        live.Disconnect();

        var lines = File.ReadAllLines(csv);
        Assert.EndsWith(",live_maps", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.EndsWith(",ign98_is=v1a2b3c4d", line));
    }

    // The 29 Oct car log: with a V8 catalogue (139 maps and settings groups) every row carried 139 tags under a
    // five-column header. One cell, whatever the catalogue, keeps every row as wide as the header.
    [AvaloniaFact]
    public async Task TheCsv_KeepsEveryRowAsWideAsTheHeader_WhateverTheCatalogue()
    {
        var port = new EcuPort(BundledDefinitions.Load("ms43x"));
        var csv = Path.Combine(_folder, "wide.csv");
        var cell = "flashed";
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x", SelectedPort = "COM1", EnableCsvLogging = true, CsvPath = csv,
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
            LiveMaps = () => cell,
        };

        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 2, "two samples");
        cell = string.Join(";", Enumerable.Range(0, 139).Select(i => $"map{i}=v0000000{i % 10}"));
        await Until(() => live.SampleCount >= 4, "two more samples");
        live.Disconnect();

        var lines = File.ReadAllLines(csv);
        var width = lines[0].Split(',').Length;
        Assert.All(lines.Skip(1), line => Assert.Equal(width, line.Split(',').Length));
    }

    [AvaloniaFact]
    public async Task APerformanceRun_RecordsTheLiveMaps_AndAChangeIsFlagged()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = PerfFeed.Open(_folder);
        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        string[]? tags = ["v1a2b3c4d", "flashed", "flashed", "flashed", "flashed"];
        shell.Performance.LiveTags = () => tags;
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.IsGear2 = true;                                                   // 2nd ends at 6400 rpm under the car's limit
        run.Arm();
        var samples = SyntheticRuns.Pull(PerfFeed.Ms43x, 2, 1200, 6500, 6.0).ToList();

        PerfFeed.Feed(shell, samples.Take(samples.Count / 2));
        tags = null;                                                          // a revert in the middle of the pull
        PerfFeed.Feed(shell, samples.Skip(samples.Count / 2));

        Assert.True(run.HasResult);
        Assert.Equal(["v1a2b3c4d", "flashed", "flashed", "flashed", "flashed"], run.Tune!.LiveMaps);
        Assert.True(run.Tune.LiveMapsChanged);
        Assert.Contains("live: ign98_pl v1a2b3c4d", run.TuneText);
        Assert.Contains("The live maps changed during this run.", run.TuneText);
    }

    [Fact]
    public void TheLiveMaps_RoundTripThroughTheStore_AndAnOldResultHasNone()
    {
        var withTags = SampleTune() with { LiveMaps = ["v1a2b3c4d", "flashed", "flashed", "flashed", "flashed"], LiveMapsChanged = true };
        PerfFeed.SaveResult(_folder, "tagged", 6.0, 1, tune: withTags);
        PerfFeed.SaveResult(_folder, "plain", 6.0, 2, tune: SampleTune());

        var store = new PerfResultStore(Path.Combine(_folder, "results"));
        var results = store.List().Select(header => store.Load(header.FileName)!).ToDictionary(result => result.Name);

        Assert.Equal(withTags.LiveMaps, results["tagged"].Tune!.LiveMaps);
        Assert.True(results["tagged"].Tune!.LiveMapsChanged);
        Assert.Null(results["plain"].Tune!.LiveMaps);
        Assert.False(results["plain"].Tune!.LiveMapsChanged);
    }

    private static TuneIdentity SampleTune() =>
        new("CAR.bin", @"C:\bins\CAR.bin", "43X001", "cal", "MS43X001", "x.xdf", false, [], "aa", "aa", 7000, false,
            TuneIdentity.SnapshotOf(BinImage.FromBytes(new byte[BinImage.Size])));
}
