using System.Text.Json.Nodes;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

/// <summary>A clock standing still, reading its time as local (UTC as the local zone), for file names from the time.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

// The saved result and its library are performance spec 8; the shapes are the plan's.
public sealed class PerfResultStoreTests : IDisposable
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 14, 32, 12, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-results-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
        else if (File.Exists(_folder))
        {
            File.Delete(_folder);
        }
    }

    internal static AnalysisInputs Inputs(double massKg = 1480) =>
        new(new CarValues(massKg, "205/55 R16", false), new Dictionary<int, double> { [3] = 0.0223, [4] = 0.0298 }, 100);

    private static PerfResult Result(string name = "3rd pull 14:32", TuneIdentity? tune = null)
    {
        var figures = new RunFigures(
            [new Figure("time", "2000 to 6400 rpm", 5.5, "s", 2), new Figure("peak-power", "Peak power, wheels (estimate)", double.NaN, "kW", 0)],
            [new Trace("power", "Power, wheels (estimate)", "rpm", "kW", [2000, 2250], [80, double.NaN])],
            [new RunFlag("gap", FlagSeverity.Suspect, "A 450 ms gap in the samples.")],
            "5.50 s, 118 kW");
        var start = new Conditions(92, 88, 25, 101.3, 14, 0.5, 1.02, 0.98);
        var outcome = new RunOutcome(PerfTestSpec.Pull(3), true, figures, start, start with { IntakeC = 27, OilC = null },
            [new RecordedSample(TimeSpan.FromSeconds(1), [1, 2, 3]), new RecordedSample(TimeSpan.FromSeconds(1.033), [4, 5, 6])], 1, 1);
        return PerfResult.From(outcome, name, "Before the intake change", Inputs(), tune, "ms43x", Ms43x, Created, "1.0.0");
    }

    private PerfResultStore Store(TimeProvider? time = null) => new(_folder, time ?? new FixedTime(Created));

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var store = Store();
        var result = Result();

        var file = store.Save(result);

        Assert.NotNull(file);
        Assert.Null(store.LastError);
        var text = File.ReadAllText(Path.Combine(_folder, file));
        Assert.Equal("Schema", JsonNode.Parse(text)!.AsObject().First().Key);
        Assert.Contains("\"Kind\": \"Pull\"", text);
        Assert.Contains("\"Severity\": \"Suspect\"", text);
        Assert.Contains("\"NaN\"", text);
        Assert.DoesNotContain(Directory.GetFiles(_folder), path => path.EndsWith(".tmp", StringComparison.Ordinal));

        var loaded = store.Load(file)!;
        Assert.Equal(1, loaded.Schema);
        Assert.Matches("^[0-9a-f]{32}$", loaded.Id);
        Assert.Equal(result.Id, loaded.Id);
        Assert.Equal("3rd pull 14:32", loaded.Name);
        Assert.Equal("Before the intake change", loaded.Description);
        Assert.Null(loaded.Direction);
        Assert.Equal(Created, loaded.Created);
        Assert.Equal("1.0.0", loaded.AppVersion);
        Assert.Equal(PerfTestSpec.Pull(3), loaded.Spec);
        Assert.True(loaded.Complete);
        Assert.Equal(result.Figures.Figures, loaded.Figures.Figures);
        Assert.True(double.IsNaN(loaded.Figures.Figures[1].Value!.Value));
        Assert.Equal(new double[] { 2000, 2250 }, loaded.Figures.Traces[0].X);
        Assert.True(double.IsNaN(loaded.Figures.Traces[0].Y[1]));
        Assert.Equal(result.Figures.Flags, loaded.Figures.Flags);
        Assert.Equal("5.50 s, 118 kW", loaded.Figures.Headline);
        Assert.Equal(result.Start, loaded.Start);
        Assert.Equal(result.End, loaded.End);
        Assert.Null(loaded.End.OilC);
        Assert.Equal(result.Inputs.Car, loaded.Inputs.Car);
        Assert.Equal(result.Inputs.KmhPerRpm.OrderBy(p => p.Key), loaded.Inputs.KmhPerRpm.OrderBy(p => p.Key));
        Assert.Equal(100, loaded.Inputs.FullPedal);
        Assert.Null(loaded.Tune);
        Assert.Equal("ms43x", loaded.Variant);
        Assert.Equal("Siemens_MS43_MS43X001_Logging.adx", loaded.DefinitionFile);
        Assert.Equal(BundledDefinitions.Sha256("ms43x"), loaded.DefinitionSha256);
        Assert.Equal(new[] { 1.0, 1.033 }, loaded.SampleAt);
        Assert.Equal(new[] { Convert.ToBase64String(new byte[] { 1, 2, 3 }), Convert.ToBase64String(new byte[] { 4, 5, 6 }) }, loaded.Payloads);
        Assert.Equal((1, 1), (loaded.WindowStart, loaded.WindowEnd));
        // The first sample's wall time: the result is made when its last sample arrived.
        Assert.Equal(Created - TimeSpan.FromSeconds(0.033), loaded.SamplesStart);
    }

    [Fact]
    public void FileNamesComeFromTheClock()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 10, 21, 14, 32, 12, TimeSpan.Zero));
        var store = Store(time);

        Assert.Equal("perf-20261021-143212.json", store.Save(Result()));
        Assert.Equal("perf-20261021-143212-2.json", store.Save(Result()));
        Assert.Equal("perf-20261021-143212-3.json", store.Save(Result()));
        time.Now = time.Now.AddSeconds(1);
        Assert.Equal("perf-20261021-143213.json", store.Save(Result()));
    }

    [Fact]
    public void RenameKeepsTheFileName()
    {
        var store = Store();
        var file = store.Save(Result())!;

        Assert.True(store.Update(file, "new", "desc", "A"));

        var header = Assert.Single(store.List());
        Assert.Equal(file, header.FileName);
        Assert.Equal(("new", "desc"), (header.Name, header.Description));
        Assert.Equal("A", store.Load(file)!.Direction);
        Assert.Equal(new[] { file }, Directory.GetFiles(_folder).Select(Path.GetFileName));

        Assert.True(store.Update(file, "new", "desc", null));
        Assert.Null(store.Load(file)!.Direction);
        Assert.False(store.Update("perf-19990101-000000.json", "x", "", null));
        Assert.StartsWith("Not updated: ", store.LastError);
    }

    [Fact]
    public void BadFilesNeverBreakTheList()
    {
        var store = Store();
        var good = store.Save(Result("good"))!;
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "perf-20261020-100000.json"), "{ not json");
        File.WriteAllText(Path.Combine(_folder, "perf-20261020-100001.json"), "{}");
        File.WriteAllText(Path.Combine(_folder, "x.tmp"), "{ half written");
        // A newer app's file: a later schema with a field this one does not know.
        var newer = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, good)))!.AsObject();
        newer["Schema"] = 2;
        newer["Name"] = "from a newer app";
        newer["Created"] = "2026-10-22T09:00:00+00:00";
        newer["Spec"]!["Kind"] = "Dyno";
        newer["Weather"] = new JsonObject { ["WindKmh"] = 12 };
        const string newerFile = "perf-20261022-090000.json";
        File.WriteAllText(Path.Combine(_folder, newerFile), newer.ToJsonString());

        var list = store.List();

        Assert.Equal(new[] { newerFile, good }, list.Select(h => h.FileName));
        Assert.True(list[0].ReadOnly);
        Assert.Equal("from a newer app", list[0].Name);
        Assert.Equal("pull-3", list[0].Preset);
        Assert.False(list[1].ReadOnly);
        Assert.Equal("good", list[1].Name);
        Assert.Equal(new[] { "perf-20261020-100000.json: not a result file", "perf-20261020-100001.json: not a result file" },
            store.Skipped.Order());

        // Rewriting a newer file would drop what this app does not know.
        var before = File.ReadAllText(Path.Combine(_folder, newerFile));
        Assert.False(store.Update(newerFile, "renamed", "", null));
        Assert.StartsWith("Not updated: ", store.LastError);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_folder, newerFile)));
        Assert.Null(store.Load("perf-20261020-100000.json"));
        Assert.Null(store.Load(@"..\escape.json"));
    }

    // A hand-edited file that is still JSON but has a null where the app reads text, a null entry or a broken trace is not a
    // result file: listing it would throw in the list's filter and stop the app at start.
    [Fact]
    public void AFileWithANullPartIsNotAResult()
    {
        var store = Store();
        var good = store.Save(Result("good"))!;
        var text = File.ReadAllText(Path.Combine(_folder, good));
        var damaged = new (string Name, Action<JsonObject> Damage)[]
        {
            ("preset", r => r["Spec"]!["Preset"] = null),
            ("no-preset", r => r["Spec"]!.AsObject().Remove("Preset")),
            ("name", r => r["Name"] = null),
            ("description", r => r["Description"] = null),
            ("id", r => r["Id"] = null),
            ("flag-code", r => r["Figures"]!["Flags"]![0]!["Code"] = null),
            ("flag-message", r => r["Figures"]!["Flags"]![0]!["Message"] = null),
            ("figure", r => r["Figures"]!["Figures"]!.AsArray().Add(null)),
            ("figure-key", r => r["Figures"]!["Figures"]![0]!["Key"] = null),
            ("figure-label", r => r["Figures"]!["Figures"]![0]!["Label"] = null),
            ("figure-twice", r => r["Figures"]!["Figures"]!.AsArray().Add(r["Figures"]!["Figures"]![0]!.DeepClone())),
            ("trace", r => r["Figures"]!["Traces"]!.AsArray().Add(null)),
            ("trace-key", r => r["Figures"]!["Traces"]![0]!["Key"] = null),
            ("trace-y", r => r["Figures"]!["Traces"]![0]!["Y"] = null),
            ("trace-length", r => r["Figures"]!["Traces"]![0]!["X"]!.AsArray().Add(2500)),
            ("payload", r => r["Payloads"]![0] = null),
            ("tyre", r => r["Inputs"]!["Car"]!["TyreSize"] = null),
        };
        for (var i = 0; i < damaged.Length; i++)
        {
            var result = JsonNode.Parse(text)!.AsObject();
            damaged[i].Damage(result);
            File.WriteAllText(Path.Combine(_folder, $"perf-20261020-1000{i:00}.json"), result.ToJsonString());
        }

        var list = store.List();

        Assert.Equal(new[] { good }, list.Select(header => header.FileName));
        Assert.Equal(damaged.Length, store.Skipped.Count);
        Assert.All(store.Skipped, line => Assert.EndsWith(": not a result file", line));
        for (var i = 0; i < damaged.Length; i++)
        {
            Assert.True(store.Load($"perf-20261020-1000{i:00}.json") is null, damaged[i].Name);
        }
    }

    // Names a newer app may write: an unknown flag severity reads as suspect, never as a note; an unknown test is listed
    // read-only and never opened as another test; fields this app does not know survive a rename.
    [Fact]
    public void UnknownNamesAreReadSafely()
    {
        var store = Store();
        var good = store.Save(Result("good"))!;
        var path = Path.Combine(_folder, good);
        var text = File.ReadAllText(path);

        var severe = JsonNode.Parse(text)!.AsObject();
        severe["Figures"]!["Flags"]![0]!["Severity"] = "Catastrophic";
        severe["Weather"] = new JsonObject { ["WindKmh"] = 12 };
        File.WriteAllText(path, severe.ToJsonString());
        Assert.Equal(FlagSeverity.Suspect, Assert.Single(store.Load(good)!.Figures.Flags).Severity);
        Assert.Equal(FlagSeverity.Suspect, Assert.Single(Assert.Single(store.List()).Flags).Severity);
        Assert.True(store.Update(good, "renamed", "", null));
        var rewritten = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("renamed", rewritten["Name"]!.GetValue<string>());
        Assert.Equal(12, rewritten["Weather"]!["WindKmh"]!.GetValue<int>());

        var dyno = JsonNode.Parse(text)!.AsObject();
        dyno["Spec"]!["Kind"] = "Dyno";
        const string dynoFile = "perf-20261022-090000.json";
        File.WriteAllText(Path.Combine(_folder, dynoFile), dyno.ToJsonString());
        Assert.True(Assert.Single(store.List(), header => header.FileName == dynoFile).ReadOnly);
        Assert.False(Assert.Single(store.List(), header => header.FileName == good).ReadOnly);
        Assert.Null(store.Load(dynoFile));
        Assert.Equal($"Not opened: {dynoFile} holds a test this version does not know", store.LastError);
        Assert.False(store.Update(dynoFile, "x", "", null));
        Assert.StartsWith("Not updated: ", store.LastError);
    }

    // The list reads a file again only when it changed on disk: one with the same write time and length is the one already
    // read, so showing the tab again parses nothing.
    [Fact]
    public void TheListReadsOnlyChangedFiles()
    {
        var store = Store();
        var file = store.Save(Result("first"))!;
        var path = Path.Combine(_folder, file);
        Assert.Equal("first", Assert.Single(store.List()).Name);

        var written = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"first\"", "\"other\"", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, written);
        Assert.Equal("first", Assert.Single(store.List()).Name);

        File.SetLastWriteTimeUtc(path, written.AddSeconds(1));
        Assert.Equal("other", Assert.Single(store.List()).Name);
        Assert.True(store.Update(file, "renamed", "", null));
        Assert.Equal("renamed", Assert.Single(store.List()).Name);
        Assert.True(store.Delete(file));
        Assert.Empty(store.List());
    }

    // A newer schema whose parts have another shape still lists, read-only, and is not opened (the earlier fix to Whole covers the list).
    [Fact]
    public void ANewerFileOfAnotherShapeListsReadOnly()
    {
        var store = Store();
        var good = store.Save(Result("good"))!;
        var newer = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, good)))!.AsObject();
        newer["Schema"] = 2;
        newer["Figures"] = "moved";
        newer["Spec"] = new JsonArray(1, 2);
        newer["Created"] = 12;
        const string file = "perf-20261022-090000.json";
        File.WriteAllText(Path.Combine(_folder, file), newer.ToJsonString());

        var header = Assert.Single(store.List(), h => h.FileName == file);

        Assert.True(header.ReadOnly);
        Assert.Equal(("good", "n/a", ""), (header.Name, header.Headline, header.Preset));
        Assert.Empty(header.Flags);
        Assert.Null(store.Load(file));
        Assert.Empty(store.Skipped);
    }

    [Fact]
    public void HeadersCarryWhatTheListShows()
    {
        var store = Store();
        var file = store.Save(Result())!;

        var header = Assert.Single(store.List());

        Assert.Equal(file, header.FileName);
        Assert.Equal(Created, header.Created);
        Assert.Equal((PerfTestKind.Pull, "pull-3"), (header.Kind, header.Preset));
        Assert.Equal("5.50 s, 118 kW", header.Headline);
        Assert.Equal("gap", Assert.Single(header.Flags).Code);
        Assert.True(header.Complete);
        Assert.Null(header.BinName);
        Assert.Null(header.SavedSha256);
        Assert.Empty(store.Skipped);
    }

    [Fact]
    public void AMissingFolderListsNothing()
    {
        Assert.Empty(Store().List());
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void FailedSaveSaysWhy()
    {
        File.WriteAllText(_folder, "blocker");
        var store = Store();

        Assert.Null(store.Save(Result()));

        Assert.StartsWith("Not saved: ", store.LastError);
        Assert.DoesNotMatch(@"\.$", store.LastError!);
        Assert.Empty(store.List());
    }

    [Fact]
    public void DeleteRemovesTheFile()
    {
        var store = Store();
        var file = store.Save(Result())!;

        Assert.True(store.Delete(file));

        Assert.False(File.Exists(Path.Combine(_folder, file)));
        Assert.Empty(store.List());
        Assert.False(store.Delete(file));
        Assert.StartsWith("Not deleted: ", store.LastError);
    }

    [Fact]
    public void BlankImageHash() =>
        Assert.Equal("07854d2fef297a06ba81685e660c332de36d5d18d546927d30daad6d7fda1541",
            TuneIdentity.Sha256(BinImage.FromBytes(new byte[BinImage.Size])));
}
