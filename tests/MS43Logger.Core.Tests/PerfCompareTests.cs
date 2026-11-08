using System.Text.Json.Nodes;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

// Comparing two saved results is performance spec 9.2; the shapes and mismatch rules are the plan's.
public class PerfCompareTests
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 14, 32, 12, TimeSpan.Zero);

    private static AnalysisInputs Inputs(double massKg = 1480) => PerfResultStoreTests.Inputs(massKg);

    // A finished run of the samples, made as the Performance tab makes one: Live's monitor fed first, then the run.
    private static RunOutcome Run(PerfTestSpec spec, IReadOnlyList<LiveSample> samples, AnalysisInputs inputs, bool thenLinkLost = false)
    {
        var warmUp = new WarmUpMonitor();
        var run = new PerfRun(spec, inputs, warmUp);
        foreach (var sample in samples)
        {
            var frame = PerfChannels.Read(Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt);
            warmUp.Push(frame);
            run.Push(frame, sample.Payload);
        }

        if (thenLinkLost)
        {
            run.LinkLost();
        }

        return run.Outcome ?? throw new InvalidOperationException(run.Status);
    }

    private static PerfResult Saved(RunOutcome outcome, AnalysisInputs inputs, TuneIdentity? tune = null) =>
        PerfResult.From(outcome, "3rd pull 14:32", "", inputs, tune, "ms43x", Ms43x, Created, "1.0.0");

    // 800 rpm/s from 1700 rpm, as long as it takes to reach toRpm.
    private static IReadOnlyList<LiveSample> ThirdPull(double toRpm = 6500, double rpmPerSecond = 800, double knockCyl4 = 0,
        double knockFromRpm = 5000, double knockToRpm = 5600) =>
        SyntheticRuns.Pull(Ms43x, 3, 1700, toRpm, (toRpm - 1700) / rpmPerSecond, knockCyl4: knockCyl4, knockFromRpm: knockFromRpm,
            knockToRpm: knockToRpm);

    private static PerfResult ThirdPullResult(AnalysisInputs? inputs = null, TuneIdentity? tune = null, double rpmPerSecond = 800)
    {
        inputs ??= Inputs();
        return Saved(Run(PerfTestSpec.Pull(3), ThirdPull(rpmPerSecond: rpmPerSecond), inputs), inputs, tune);
    }

    private static TuneIdentity Tune(string savedSha256) =>
        new(null, null, null, null, "TEST", null, false, [], savedSha256, savedSha256, null, null, "");

    private static double? Value(RunFigures figures, string key) => Assert.Single(figures.Figures, f => f.Key == key).Value;

    // Saved by the store, edited as a file and read back: what an older or hand-edited file gives.
    private static PerfResult Reloaded(PerfResult result, Action<JsonObject>? edit = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ms43-compare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PerfResultStore(folder);
            var file = store.Save(result)!;
            var path = Path.Combine(folder, file);
            var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            edit?.Invoke(node);
            File.WriteAllText(path, node.ToJsonString());
            return store.Load(file) ?? throw new InvalidDataException(store.LastError);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CompareRecomputesWithCurrentCarValues()
    {
        var a = ThirdPullResult(Inputs(1480));
        var b = ThirdPullResult(Inputs(1600));
        // The heavier car needs more power for the same pull.
        Assert.True(Value(b.Figures, "peak-power") > Value(a.Figures, "peak-power") * 1.05);

        var comparison = PerfCompare.Compare(a, b, Inputs(1480), xdf: null);

        var expected = RunAnalysis.Analyse(PerfTestSpec.Pull(3), PerfCompare.Frames(a), Inputs(1480), complete: true);
        Assert.Equal(Value(expected, "peak-power")!.Value, Value(comparison.A, "peak-power")!.Value, 9);
        Assert.Equal(Value(a.Figures, "peak-power")!.Value, Value(comparison.A, "peak-power")!.Value, 6);
        Assert.Equal(Value(comparison.A, "peak-power")!.Value, Value(comparison.B, "peak-power")!.Value, 6);
        var power = Assert.Single(comparison.Deltas, d => d.Key == "peak-power");
        Assert.Equal(("Peak power, wheels (estimate)", "kW", 0), (power.Label, power.Unit, power.Decimals));
        Assert.Equal(0, power.Delta!.Value, 6);
        Assert.True(comparison.Recomputed);
        Assert.Equal((2000, 6400), (comparison.SharedFrom, comparison.SharedTo));

        // Both made with the current values: nothing to note.
        Assert.False(PerfCompare.Compare(a, ThirdPullResult(Inputs(1480)), Inputs(1480), null).Recomputed);
    }

    [Fact]
    public void FramesComeBackFromTheStoredSamples()
    {
        var outcome = Run(PerfTestSpec.Pull(3), ThirdPull(), Inputs());
        var result = Saved(outcome, Inputs());

        var frames = PerfCompare.Frames(result);

        Assert.Equal(outcome.Samples.Count, frames.Count);
        foreach (var i in new[] { 0, outcome.WindowStart, outcome.WindowEnd })
        {
            var sample = outcome.Samples[i];
            var expected = PerfChannels.Read(Ms43x, Ms43x.DecodePayload(sample.Payload), sample.Payload, sample.At);
            Assert.Equal((expected.At, expected.Rpm, expected.Gear, expected.Pedal), (frames[i].At, frames[i].Rpm, frames[i].Gear, frames[i].Pedal));
            Assert.Equal(expected.KnockRetard, frames[i].KnockRetard);
        }

        // A payload too short for its definition cannot be decoded again.
        var conditions = new Conditions(92, 88, 25, 101.3, 14, null, null, null);
        var cut = new RunOutcome(PerfTestSpec.Pull(3), true, outcome.Figures, conditions, conditions,
            [new RecordedSample(TimeSpan.Zero, [1, 2, 3])], 0, 0);
        Assert.Throws<InvalidDataException>(() => PerfCompare.Frames(Saved(cut, Inputs())));
    }

    [Fact]
    public void CompareUsesTheSharedBand()
    {
        var a = ThirdPullResult();
        // B also ran to the car's limit, with knock above A's end only.
        var bSpec = PerfTestSpec.Pull(3, 6954);
        var b = Saved(Run(bSpec, ThirdPull(toRpm: 7100, knockCyl4: 1.5, knockFromRpm: 6600, knockToRpm: 6900), Inputs()), Inputs());
        Assert.Equal("2000 to 6954 rpm", b.Figures.Figures.Single(f => f.Key == "time").Label);
        Assert.Equal(1.5, Value(b.Figures, "knock-max"));

        var comparison = PerfCompare.Compare(a, b, Inputs(), null);

        Assert.Equal((2000, 6400), (comparison.SharedFrom, comparison.SharedTo));
        var time = Assert.Single(comparison.Deltas, d => d.Key == "time");
        Assert.Equal("2000 to 6400 rpm", time.Label);
        Assert.Equal(5.5, time.A!.Value, 0.02);
        Assert.Equal(5.5, time.B!.Value, 0.02);
        Assert.Equal(0, time.Delta!.Value, 0.02);
        var marks = Assert.Single(comparison.Deltas, d => d.Key == "t-4000-6000");
        Assert.Equal(2.5, marks.B!.Value, 0.02);
        Assert.NotNull(marks.Delta);
        // Over the shared band, B never knocked, and both power curves end with A's band.
        Assert.Equal(0, Value(comparison.B, "knock-max"));
        Assert.DoesNotContain(comparison.B.Flags, f => f.Code == "knock");
        Assert.Equal(comparison.A.Traces.Single(t => t.Key == "power").X, comparison.B.Traces.Single(t => t.Key == "power").X);
        // Recomputed only notes other car values, never the narrower band.
        Assert.False(comparison.Recomputed);
    }

    [Fact]
    public void AnIncompleteRunKeepsItsReason()
    {
        var a = ThirdPullResult();
        var lost = Run(PerfTestSpec.Pull(3), ThirdPull(toRpm: 5000), Inputs(), thenLinkLost: true);
        Assert.False(lost.Complete);
        var b = Saved(lost, Inputs());

        var comparison = PerfCompare.Compare(a, b, Inputs(), null);

        // The run knew why it ended; the recompute alone only knows where.
        var flag = Assert.Single(comparison.B.Flags, f => f.Code == "link-lost");
        Assert.Equal(b.Figures.Flags.Single(f => f.Code == "link-lost").Message, flag.Message);
        Assert.DoesNotContain(comparison.B.Flags, f => f.Code == "incomplete");
        Assert.Null(Assert.Single(comparison.Deltas, d => d.Key == "time").Delta);
        Assert.DoesNotContain(comparison.A.Flags, f => f.Severity == FlagSeverity.Invalid);
    }

    [Fact]
    public void MismatchesAreNamed()
    {
        var outcome = Run(PerfTestSpec.Pull(3), ThirdPull(), Inputs());
        var start = outcome.Start with { IntakeC = 31, PressureKpa = 101.3, CoolantC = 92 };
        var a = Saved(outcome with { Start = start }, Inputs());
        var b = Saved(outcome with { Start = start with { IntakeC = 38, PressureKpa = 99.2, CoolantC = 101 } }, Inputs());

        var mismatches = PerfCompare.Compare(a, b, Inputs(), null).Mismatches;

        Assert.Equal(new[] { "intake", "pressure", "coolant" }, mismatches.Select(m => m.Code));
        Assert.Equal("Intake air 31 and 38 °C: more than 5 °C apart.", mismatches[0].Message);
        Assert.Equal("Pressure 101.3 and 99.2 kPa: 2.1 kPa or more apart.", mismatches[1].Message);
        Assert.Equal("Coolant 92 and 101 °C: more than 8 °C apart.", mismatches[2].Message);

        // Within the limits, and a missing reading, are no mismatch.
        var near = Saved(outcome with { Start = start with { IntakeC = 36, PressureKpa = 99.3, CoolantC = 100 } }, Inputs());
        Assert.Empty(PerfCompare.Compare(a, near, Inputs(), null).Mismatches);
        var unknown = Saved(outcome with { Start = start with { IntakeC = null } }, Inputs());
        Assert.Empty(PerfCompare.Compare(a, unknown, Inputs(), null).Mismatches);

        // The same tune is no condition that differs: it is said apart, as a note.
        var sameTune = PerfCompare.Compare(Saved(outcome, Inputs(), Tune("aa")), Saved(outcome, Inputs(), Tune("aa")), Inputs(), null);
        Assert.Empty(sameTune.Mismatches);
        Assert.True(sameTune.SameTune);
        Assert.False(PerfCompare.Compare(a, b, Inputs(), null).SameTune);
        Assert.False(PerfCompare.Compare(Saved(outcome, Inputs(), Tune("aa")), Saved(outcome, Inputs(), Tune("bb")), Inputs(), null).SameTune);
    }

    // Two gears, or two tests, are named, so their differences are not read as the tune's doing.
    [Fact]
    public void DifferentGearsAreNamed()
    {
        var third = ThirdPullResult();
        var second = Saved(Run(PerfTestSpec.Pull(2), SyntheticRuns.Pull(Ms43x, 2, 1200, 6500, 6.0, kmhPer1000: 14.2), Inputs()), Inputs());
        var gears = Assert.Single(PerfCompare.Compare(second, third, Inputs(), null).Mismatches, m => m.Code == "gear");
        Assert.Equal("Different gears: 2nd and 3rd.", gears.Message);

        var knock = Saved(Run(PerfTestSpec.KnockCheck(), SyntheticRuns.Pull(Ms43x, 4, 1100, 4700, 5.0, kmhPer1000: 29.8), Inputs()), Inputs());
        var mismatches = PerfCompare.Compare(third, knock, Inputs(), null).Mismatches;
        Assert.Equal("Different tests: 3rd pull and knock check.", Assert.Single(mismatches, m => m.Code == "test").Message);
        Assert.DoesNotContain(mismatches, m => m.Code == "gear");
        Assert.DoesNotContain(PerfCompare.Compare(third, ThirdPullResult(), Inputs(), null).Mismatches, m => m.Code is "gear" or "test");
    }

    // Roll-ons compared over the speeds they share are cut at the shared end, as pulls are: B's knock above A's end does not
    // count.
    [Fact]
    public void RollOnsAreCutAtTheSharedEnd()
    {
        var a = Saved(Run(PerfTestSpec.RollOn(3), SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0, holdSeconds: 2), Inputs()), Inputs());
        // B ran to 120 km/h, cylinder 4 knocking only from 4750 rpm, 106 km/h in 3rd.
        var bSpec = PerfTestSpec.RollOn(3) with { EndKmh = 120 };
        var b = Saved(Run(bSpec, SyntheticRuns.Pull(Ms43x, 3, 2152, 5560, 5.5, holdSeconds: 2, knockCyl4: 1.5, knockFromRpm: 4750, knockToRpm: 5300),
            Inputs()), Inputs());
        Assert.Equal(1.5, Value(b.Figures, "knock-max"));

        var comparison = PerfCompare.Compare(a, b, Inputs(), null);

        Assert.Equal((60, 100), (comparison.SharedFrom, comparison.SharedTo));
        Assert.Equal(0, Value(comparison.B, "knock-max"));
        Assert.DoesNotContain(comparison.B.Flags, f => f.Code is "knock" or "incomplete");
        Assert.NotNull(Assert.Single(comparison.Deltas, d => d.Key == "time").Delta);
        Assert.False(comparison.Recomputed);
    }

    // A result decoded now with another logging definition than it was saved with is said apart from other car values.
    [Fact]
    public void ANewerDefinitionIsNoted()
    {
        var a = ThirdPullResult();
        Assert.False(PerfCompare.Compare(a, ThirdPullResult(), Inputs(), null).DefinitionChanged);

        var older = Reloaded(ThirdPullResult(), r => r["DefinitionSha256"] = "00");
        Assert.True(PerfCompare.Compare(a, older, Inputs(), null).DefinitionChanged);
        Assert.True(PerfCompare.Compare(older, a, Inputs(), null).DefinitionChanged);
    }

    // A result saved before injector duty, DME torque and the knock cylinder were traced still loads and compares; the
    // recompute brings the new traces.
    [Fact]
    public void AnOlderResultWithoutTheNewTracesCompares()
    {
        var a = ThirdPullResult();
        var older = Reloaded(a, r =>
        {
            var traces = r["Figures"]!["Traces"]!.AsArray();
            foreach (var trace in traces.Where(t => t!["Key"]!.GetValue<string>() is "injdc" or "dme-torque" or "knock-cyl").ToArray())
            {
                traces.Remove(trace);
            }

            r.Remove("Armed");
            r.Remove("SampleRetries");
        });
        Assert.DoesNotContain(older.Figures.Traces, t => t.Key is "injdc" or "dme-torque");
        Assert.Null(older.Armed);

        var comparison = PerfCompare.Compare(older, a, Inputs(), null);

        Assert.Contains(comparison.A.Traces, t => t.Key == "injdc");
        Assert.Contains(comparison.A.Traces, t => t.Key == "dme-torque");
        Assert.Equal(0, Assert.Single(comparison.Deltas, d => d.Key == "time").Delta!.Value, 1e-9);
        Assert.False(comparison.Recomputed);
    }

    // The retries of a link test are kept with its samples and read again from the file.
    [Fact]
    public void RetriesSurviveTheRoundTrip()
    {
        var warmUp = new WarmUpMonitor();
        var run = new PerfRun(PerfTestSpec.LinkTest(), Inputs(), warmUp);
        var i = 0;
        foreach (var sample in SyntheticRuns.Idle(Ms43x, 31))
        {
            var frame = PerfChannels.Read(Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt, retries: i++ % 100 == 50 ? 1 : 0);
            warmUp.Push(frame);
            run.Push(frame, sample.Payload);
        }

        var outcome = run.Outcome!;
        var result = Reloaded(Saved(outcome, Inputs()));

        Assert.Equal(9, Value(result.Figures, "retries"));
        Assert.Equal(outcome.Samples.Select(s => s.Retries), PerfCompare.Frames(result).Select(f => f.Retries));
        Assert.Equal(9, Value(PerfCompare.Recompute(result, Inputs()), "retries"));
        // A file saved before retries were kept reads none.
        var older = Reloaded(Saved(outcome, Inputs()), r => r.Remove("SampleRetries"));
        Assert.All(PerfCompare.Frames(older), f => Assert.Null(f.Retries));
        Assert.Null(Value(PerfCompare.Recompute(older, Inputs()), "retries"));
    }

    // A stock-definition pull, from its own payloads, through run, save, reload and compare: knock is n/a, never zero.
    [Fact]
    public void AStockDefinitionPullGoesThroughToCompare()
    {
        var b25 = BundledDefinitions.Load("b25");
        var warmUp = new WarmUpMonitor();
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        foreach (var sample in SyntheticRuns.Pull(b25, 3, 1700, 6500, 6.0))
        {
            var frame = PerfChannels.Read(b25, sample.Values, sample.Payload.Span, sample.RequestedAt);
            warmUp.Push(frame);
            run.Push(frame, sample.Payload);
        }

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        Assert.Equal(5.5, Value(outcome.Figures, "time")!.Value, 0.02);
        var result = Reloaded(PerfResult.From(outcome, "Stock pull", "", Inputs(), null, "b25", b25, Created, "1.0.0"));
        Assert.Equal("b25", result.Variant);
        Assert.Null(Value(result.Figures, "knock-max"));
        Assert.Null(Value(result.Figures, "knock-cyl"));

        var comparison = PerfCompare.Compare(result, result, Inputs(), null);

        Assert.Equal(5.5, Value(comparison.A, "time")!.Value, 0.02);
        Assert.Null(Value(comparison.B, "knock-max"));
        Assert.Null(Assert.Single(comparison.Deltas, d => d.Key == "knock-max").Delta);
        Assert.Equal(0, Assert.Single(comparison.Deltas, d => d.Key == "time").Delta!.Value, 1e-9);
        Assert.DoesNotContain(comparison.A.Traces, t => t.Key is "knock" or "afr");
        Assert.False(comparison.Recomputed);
    }

    [Fact]
    public void TablesThatDifferComeFromTheSnapshots()
    {
        var xdf = BinComparerTests.Synthetic();
        var before = BinComparerTests.With(BinComparerTests.Blank(), (0x70100, 10), (0x70201, 1));
        var after = BinComparerTests.With(before, (0x70101, 14));
        TuneIdentity TuneOf(BinImage image) => TuneIdentity.From(CalibrationDocument.FromParts(xdf, image, null), 3)!;
        var outcome = Run(PerfTestSpec.Pull(3), ThirdPull(), Inputs());
        var a = Saved(outcome, Inputs(), TuneOf(before));
        var b = Saved(outcome, Inputs(), TuneOf(after));

        // The newer tune as A, as the app passes it: A − B is what the change did.
        var comparison = PerfCompare.Compare(b, a, Inputs(), xdf);

        var table = Assert.Single(comparison.TableDifferences);
        Assert.Equal("fuel_map", table.Item.Title);
        Assert.Equal(1, table.ChangedCells);
        Assert.Equal(7, table.LargestChange);
        Assert.Empty(PerfCompare.Compare(a, b, Inputs(), xdf: null).TableDifferences);
        Assert.Empty(PerfCompare.Compare(a, Saved(outcome, Inputs()), Inputs(), xdf).TableDifferences);
        Assert.Empty(PerfCompare.Compare(a, a, Inputs(), xdf).TableDifferences);
        Assert.Null(comparison.TablesNote);

        // Through another XDF than the runs were saved with, no table is listed and the note names the XDF they need.
        var a2 = Saved(outcome, Inputs(), TuneOf(before) with { XdfTitle = "MS43X001 v1.0" });
        var b2 = Saved(outcome, Inputs(), TuneOf(after) with { XdfTitle = "MS43X001 v1.0" });
        var other = PerfCompare.Compare(a2, b2, Inputs(), xdf);
        Assert.Empty(other.TableDifferences);
        Assert.Equal("Open the XDF these runs were saved with (MS43X001 v1.0) on Tune to list the tables that differ.", other.TablesNote);
        var mixed = PerfCompare.Compare(a, b2, Inputs(), xdf);
        Assert.Empty(mixed.TableDifferences);
        Assert.Equal("The runs were saved with different XDFs (TEST and MS43X001 v1.0), so their tables are not compared.", mixed.TablesNote);
    }

    // An empty table list always says why: no XDF open, or a snapshot that cannot be read, never "no table differs".
    [Fact]
    public void TablesNoteSaysWhyNoneIsListed()
    {
        var xdf = BinComparerTests.Synthetic();
        var before = BinComparerTests.With(BinComparerTests.Blank(), (0x70100, 10), (0x70201, 1));
        var after = BinComparerTests.With(before, (0x70101, 14));
        TuneIdentity TuneOf(BinImage image) => TuneIdentity.From(CalibrationDocument.FromParts(xdf, image, null), 3)!;
        var outcome = Run(PerfTestSpec.Pull(3), ThirdPull(), Inputs());
        var a = Saved(outcome, Inputs(), TuneOf(before));
        var b = Saved(outcome, Inputs(), TuneOf(after));

        Assert.Equal("Open the XDF these runs were saved with (TEST) on Tune to list the tables that differ.",
            PerfCompare.Compare(a, b, Inputs(), xdf: null).TablesNote);
        // Equal snapshots differ in no table, with or without the XDF.
        Assert.Null(PerfCompare.Compare(a, a, Inputs(), xdf: null).TablesNote);

        var damaged = Saved(outcome, Inputs(), TuneOf(after) with { Snapshot = "not a snapshot" });
        var comparison = PerfCompare.Compare(a, damaged, Inputs(), xdf);
        Assert.Empty(comparison.TableDifferences);
        Assert.Equal("A run's calibration snapshot cannot be read, so the tables are not compared.", comparison.TablesNote);
    }

    [Fact]
    public void NoiseNeedsThreeRuns()
    {
        PerfResult WithPower(double kw, string savedSha256 = "aa", bool cold = false)
        {
            var figures = new RunFigures([new Figure("peak-power", "Peak power, wheels (estimate)", kw, "kW", 0)], [],
                cold ? [new RunFlag("cold", FlagSeverity.Cold, "Engine cold: not comparable.")] : [], $"{kw} kW");
            var conditions = new Conditions(92, 88, 25, 101.3, 14, null, null, null);
            var outcome = new RunOutcome(PerfTestSpec.Pull(3), true, figures, conditions, conditions, [], 0, 0);
            return Saved(outcome, Inputs(), Tune(savedSha256));
        }

        Assert.Null(PerfCompare.Noise([WithPower(115), WithPower(116)], "peak-power"));
        Assert.Equal(1.0, PerfCompare.Noise([WithPower(115), WithPower(116), WithPower(117)], "peak-power")!.Value, 9);
        // Another tune and a cold run are not part of the spread.
        Assert.Equal(1.0, PerfCompare.Noise([WithPower(115), WithPower(200, "bb"), WithPower(116), WithPower(300, cold: true), WithPower(117)],
            "peak-power")!.Value, 9);
        Assert.Null(PerfCompare.Noise([WithPower(115), WithPower(116), WithPower(117, "bb")], "peak-power"));
    }

    [Fact]
    public void CompareShowsTheNoiseOfTheSameTune()
    {
        var tune = Tune("aa");
        // Pulls of 5.5, 5.61 and 5.72 s over 2000 to 6400 rpm: a spread of 0.11 s.
        var runs = new[] { 800, 784.3, 769.2 }.Select(rate => ThirdPullResult(tune: tune, rpmPerSecond: rate)).ToArray();

        var comparison = PerfCompare.Compare(runs[0], runs[1], Inputs(), null, runs);

        Assert.Equal(0.11, comparison.Noise!.Value, 0.01);
        Assert.Null(PerfCompare.Compare(runs[0], runs[1], Inputs(), null).Noise);
        Assert.Null(PerfCompare.Compare(runs[0], runs[1], Inputs(), null, runs[..2]).Noise);
    }
}
