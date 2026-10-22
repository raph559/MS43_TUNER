using Avalonia.Headless.XUnit;
using MS43Logger.Core;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Performance tab's results library and compare.</summary>
public sealed class PerformanceResultsTests : IDisposable
{
    private static readonly AnalysisInputs Inputs = new(new CarValues(1480, "205/55 R16", false), new Dictionary<int, double>(), SyntheticRuns.FullPedal);

    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // A pull (3rd gear unless said) made with the Core run, saved where the tab reads its results; cold marks it not comparable.
    private void Save(string name, double seconds, bool cold = false, int minute = 0, int gear = 3)
    {
        var warmUp = new WarmUpMonitor();
        var run = new PerfRun(PerfTestSpec.Pull(gear), Inputs, warmUp);
        foreach (var sample in SyntheticRuns.Pull(PerfFeed.Ms43x, gear, gear == 3 ? 1700 : 1200, 6500, seconds))
        {
            var frame = PerfChannels.Read(PerfFeed.Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt);
            warmUp.Push(frame);
            run.Push(frame, sample.Payload);
        }

        var outcome = run.Outcome!;
        if (cold)
        {
            outcome = outcome with
            {
                Figures = outcome.Figures with { Flags = [new RunFlag("cold", FlagSeverity.Cold, "Engine cold: not comparable."), .. outcome.Figures.Flags] },
            };
        }

        var created = new DateTimeOffset(2026, 10, 21, 12, minute, 0, TimeSpan.Zero);
        var result = PerfResult.From(outcome, name, "", Inputs, null, "ms43x", PerfFeed.Ms43x, created, "test");
        Assert.NotNull(new PerfResultStore(Path.Combine(_folder, "results"), new FixedTime(created)).Save(result));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private PerformanceResultsViewModel Results()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        shell.Performance.Results.Refresh();
        return shell.Performance.Results;
    }

    [AvaloniaFact]
    public void RowsShowTheStoredResults()
    {
        Save("Baseline", 6.2, minute: 5);
        Save("Ignition +1.5", 6.0, minute: 32);
        var results = Results();

        Assert.Equal(["Ignition +1.5", "Baseline"], results.Rows.Select(row => row.Name));
        var row = results.Rows[0];
        Assert.Equal("Pull, 3rd", row.TestText);
        Assert.Equal("21 Oct 12:32", row.DateText);
        Assert.Equal("No bin", row.TuneText);
        Assert.Matches(@"^\d\.\d\d s, \d+ kW$", row.Headline);
        Assert.False(row.IsCold);
    }

    [AvaloniaFact]
    public void CompareNeedsExactlyTwo()
    {
        Save("One", 6.2, minute: 1);
        Save("Two", 6.1, minute: 2);
        Save("Three", 6.0, minute: 3);
        var results = Results();
        Assert.False(results.CanCompare);

        results.Rows[0].IsPicked = true;
        Assert.False(results.CanCompare);
        results.Rows[1].IsPicked = true;
        Assert.True(results.CanCompare);
        results.Rows[2].IsPicked = true;
        Assert.False(results.CanCompare);
        results.Compare();
        Assert.Null(results.Comparison);

        results.Rows[0].IsPicked = false;
        Assert.True(results.CanCompare);
    }

    [AvaloniaFact]
    public void ColdRunsAreHiddenUntilAsked()
    {
        Save("Warm", 6.0, minute: 1);
        Save("Morning", 6.4, cold: true, minute: 2);
        var results = Results();

        Assert.Equal(["Warm"], results.Rows.Select(row => row.Name));
        results.ShowCold = true;
        Assert.Equal(["Morning", "Warm"], results.Rows.Select(row => row.Name));
        Assert.True(results.Rows[0].IsCold);
        Assert.Contains("cold", results.Rows[0].FlagTags);

        // Picks of a run that is hidden again are dropped, so Compare never takes a row out of view.
        results.Rows[0].IsPicked = true;
        results.ShowCold = false;
        results.Rows[0].IsPicked = true;
        Assert.False(results.CanCompare);
    }

    [AvaloniaFact]
    public void SearchFiltersByName()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var results = Results();

        results.SearchText = "ignition";
        Assert.Equal(["Ignition +1.5"], results.Rows.Select(row => row.Name));
        results.SearchText = "";
        Assert.Equal(2, results.Rows.Count);
    }

    [AvaloniaFact]
    public void RenameAndDescriptionKeepTheFile()
    {
        Save("Baseline", 6.2);
        var results = Results();
        var file = results.Rows[0].FileName;

        Assert.True(results.Rename(results.Rows[0], "Before"));
        Assert.True(results.EditDescription(results.Rows[0], "SP95, dry"));

        var row = Assert.Single(results.Rows);
        Assert.Equal(file, row.FileName);
        Assert.Equal("Before", row.Name);
        Assert.Equal("SP95, dry", row.Description);
        Assert.False(results.Rename(row, "  "));
    }

    [AvaloniaFact]
    public async Task DeleteAsksFirst()
    {
        Save("Baseline", 6.2);
        var (_, shell) = PerfFeed.Open(_folder);
        var results = shell.Performance.Results;
        string? asked = null;
        shell.Performance.AskToDelete = message =>
        {
            asked = message;
            return Task.FromResult(false);
        };

        Assert.False(await results.DeleteAsync(results.Rows[0]));
        Assert.Equal("Delete the result \"Baseline\"?", asked);
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results")));

        shell.Performance.AskToDelete = _ => Task.FromResult(true);
        Assert.True(await results.DeleteAsync(results.Rows[0]));
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "results")));
        Assert.Empty(results.Rows);
    }

    [AvaloniaFact]
    public void CompareShowsPowerTraces()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var results = Results();
        results.Rows[0].IsPicked = true;
        results.Rows[1].IsPicked = true;

        results.Compare();

        var comparison = results.Comparison!;
        Assert.Null(results.Error);
        Assert.True(comparison.IsPower);
        Assert.NotEmpty(comparison.TraceA);
        Assert.NotEmpty(comparison.TraceB);
        Assert.Equal("rpm", comparison.XUnits);
        Assert.Equal("kW", comparison.YUnits);
        // A is the newer run and B the older, as in Compare, so A − B is what the change did.
        Assert.Equal("A: Ignition +1.5", comparison.LabelA);
        Assert.Equal("B: Baseline", comparison.LabelB);
        var time = Assert.Single(comparison.Deltas, delta => delta.Key == "time");
        Assert.StartsWith("-0.", time.Delta);
        Assert.Equal("Compared over 2000 to 6400 rpm, the band both runs share.", comparison.BandText);

        var version = comparison.Version;
        comparison.IsRpm = true;
        Assert.False(comparison.IsPower);
        Assert.Equal("s", comparison.XUnits);
        Assert.NotEmpty(comparison.TraceA);
        Assert.True(comparison.Version > version);
    }

    // Each run's doubts show beside the deltas, so a cold, invalid or knocking run never gets bare B − A figures; notes stay
    // in the result itself.
    [AvaloniaFact]
    public void CompareShowsEachRunsFlags()
    {
        Save("Warm", 6.0, minute: 1);
        Save("Morning", 6.4, cold: true, minute: 2);
        var results = Results();
        results.ShowCold = true;
        foreach (var row in results.Rows)
        {
            row.IsPicked = true;
        }

        results.Compare();

        var comparison = results.Comparison!;
        Assert.Equal("A: Morning", comparison.LabelA);
        Assert.Empty(comparison.FlagsB);
        Assert.False(comparison.HasFlagsB);
        var cold = Assert.Single(comparison.FlagsA, flag => flag.Code == "cold");
        Assert.True(cold.IsWarn);
        Assert.True(comparison.HasFlagsA);
        Assert.DoesNotContain(comparison.FlagsA, flag => flag.IsInfo);
    }

    // Back to the list keeps what was typed into an opened result: it is written to the file.
    [AvaloniaFact]
    public void ClosingAnOpenedResultKeepsItsEdits()
    {
        Save("Baseline", 6.2);
        var results = Results();
        results.Open(results.Rows[0]);
        results.Opened!.Name = "Before";
        results.Opened.Description = "SP95";
        results.Opened.IsDirectionA = true;

        results.CloseOpened();

        Assert.False(results.HasOpened);
        var row = Assert.Single(results.Rows);
        Assert.Equal(("Before", "SP95"), (row.Name, row.Description));
        Assert.Equal("A", new PerfResultStore(Path.Combine(_folder, "results")).Load(row.FileName)!.Direction);

        // A blank name is not written: the result stays open and says why.
        results.Open(row);
        results.Opened!.Name = " ";
        results.CloseOpened();
        Assert.True(results.HasOpened);
        Assert.Equal("Not renamed: a result needs a name", results.Error);
    }

    // A ticked row stays ticked and in view while the search changes, so A and B can each be found by search.
    [AvaloniaFact]
    public void SearchKeepsTheTicks()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var results = Results();
        results.Rows.Single(row => row.Name == "Baseline").IsPicked = true;

        results.SearchText = "ignition";

        Assert.Equal(["Ignition +1.5", "Baseline"], results.Rows.Select(row => row.Name));
        Assert.True(results.Rows.Single(row => row.Name == "Baseline").IsPicked);
        results.Rows.Single(row => row.Name == "Ignition +1.5").IsPicked = true;
        Assert.True(results.CanCompare);
        results.SearchText = "zzz";
        Assert.Equal(2, results.Rows.Count);
        results.SearchText = "";
        Assert.Equal(2, results.PickedCount);
    }

    // The rpm of each run's peak goes beside its peak, not in a row of its own reading "0 rpm".
    [AvaloniaFact]
    public void ThePeakRpmShowsBesideThePeak()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var results = Results();
        foreach (var row in results.Rows)
        {
            row.IsPicked = true;
        }

        results.Compare();

        var deltas = results.Comparison!.Deltas;
        Assert.DoesNotContain(deltas, delta => delta.Key is "peak-power-rpm" or "peak-torque-rpm" or "knock-cyl");
        Assert.Matches(@"^A \d+ kW at \d+ rpm, B \d+ kW at \d+ rpm$", Assert.Single(deltas, delta => delta.Key == "peak-power").Pair);
        Assert.Matches(@"^A \d+ Nm at \d+ rpm, B \d+ Nm at \d+ rpm$", Assert.Single(deltas, delta => delta.Key == "peak-torque").Pair);
    }

    // Two gears are named, and their times are not subtracted.
    [AvaloniaFact]
    public void DifferentGearsLeaveTheTimesOut()
    {
        Save("Second", 6.0, minute: 1, gear: 2);
        Save("Third", 6.0, minute: 2);
        var results = Results();
        foreach (var row in results.Rows)
        {
            row.IsPicked = true;
        }

        results.Compare();

        var comparison = results.Comparison!;
        // A (the newer run, 3rd) first, as every A and B pair reads.
        Assert.Contains("Different gears: 3rd and 2nd; times are not compared.", comparison.Mismatches);
        var times = comparison.Deltas.Where(delta => delta.Unit == "s").ToArray();
        Assert.NotEmpty(times);
        Assert.All(times, delta => Assert.Equal("n/a", delta.Delta));
        Assert.NotEqual("n/a", Assert.Single(comparison.Deltas, delta => delta.Key == "time").A);
    }

    // An empty table list says why, from the comparison, and one saved image is a plain line, not a mismatch.
    [AvaloniaFact]
    public void TablesSayWhyNoneIsListed()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var store = new PerfResultStore(Path.Combine(_folder, "results"));
        var files = store.List().OrderBy(header => header.Created).Select(header => header.FileName).ToArray();
        var (a, b) = (store.Load(files[0])!, store.Load(files[1])!);
        var comparison = PerfCompare.Compare(a, b, Inputs, xdf: null);
        const string OpenXdf = "Open the XDF these runs were saved with (MS43X001 v1.0) on Tune to list the tables that differ.";

        Assert.Equal(OpenXdf, new PerformanceComparisonViewModel(a, b, comparison with { TablesNote = OpenXdf }).TablesNote);
        var same = new PerformanceComparisonViewModel(a, b, comparison with { SameTune = true });
        Assert.Equal("Same tune in both runs, so no table differs.", same.TablesNote);
        Assert.False(same.HasMismatches);
        Assert.Equal("A run saved with no bin open has no calibration to compare.", new PerformanceComparisonViewModel(a, b, comparison).TablesNote);
    }

    [AvaloniaFact]
    public void OpenShowsAStoredResult()
    {
        Save("Baseline", 6.2);
        var results = Results();

        results.Open(results.Rows[0]);

        var opened = results.Opened!;
        Assert.Equal("Baseline", opened.Name);
        Assert.Contains(opened.Figures, figure => figure.Key == "time");
        Assert.Equal("No bin open", opened.TuneText);
        opened.Name = "Before";
        Assert.True(opened.SaveDetails());
        Assert.Equal("Before", Assert.Single(results.Rows).Name);
    }

    // A trace neither run has is said plainly, and its segment is off with the reason.
    [AvaloniaFact]
    public void ATraceNeitherRunHasIsOff()
    {
        Save("Baseline", 6.2, minute: 1);
        Save("Ignition +1.5", 6.0, minute: 2);
        var store = new PerfResultStore(Path.Combine(_folder, "results"));
        var files = store.List().OrderBy(header => header.Created).Select(header => header.FileName).ToArray();
        var (a, b) = (store.Load(files[0])!, store.Load(files[1])!);
        var compared = PerfCompare.Compare(a, b, Inputs, xdf: null);
        static RunFigures WithoutAirflow(RunFigures figures) => figures with { Traces = figures.Traces.Where(trace => trace.Key != "maf").ToArray() };

        var comparison = new PerformanceComparisonViewModel(a, b, compared with { A = WithoutAirflow(compared.A), B = WithoutAirflow(compared.B) });

        Assert.Equal("No airflow trace in either run.", comparison.MafTip);
        Assert.Null(comparison.PowerTip);
        Assert.Null(comparison.RpmTip);
        comparison.IsMaf = true;
        Assert.Equal("No airflow trace in either run.", comparison.NoTraceText);
        comparison.IsPower = true;
        Assert.Null(comparison.NoTraceText);
    }

    // An opened result is titled with its name, the test beside it; Save changes waits for a change.
    [AvaloniaFact]
    public void AnOpenedResultIsTitledWithItsName()
    {
        Save("Baseline", 6.2);
        var results = Results();
        results.Open(results.Rows[0]);
        var opened = results.Opened!;
        var changes = new List<string?>();
        opened.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal("Baseline", opened.Title);
        Assert.Matches(@"^3rd pull · \d\.\d\d s, \d+ kW$", opened.Headline);
        Assert.False(opened.HasChanges);

        opened.Name = "Before";
        Assert.True(opened.HasChanges);
        Assert.Contains(nameof(OpenResultViewModel.HasChanges), changes);
        Assert.Equal("Baseline", opened.Title);

        Assert.True(opened.SaveDetails());
        Assert.False(opened.HasChanges);
        Assert.Equal("Before", opened.Title);
        opened.IsDirectionB = true;
        Assert.True(opened.HasChanges);
    }
}
