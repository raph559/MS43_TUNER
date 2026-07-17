using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

// The run state machine and its analysis are performance spec 6 and 7; the figure keys and flags are the plan's (Task 6).
public class PerfRunTests
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    // The shared monitor, fed every frame before the run sees it, as Live feeds it (spec 5).
    private readonly WarmUpMonitor warmUp = new();

    private static AnalysisInputs Inputs(double fullPedal = SyntheticRuns.FullPedal, bool calibrated = true) =>
        new(new CarValues(1480, "205/55 R16", false),
            calibrated ? new Dictionary<int, double> { [3] = 0.0223, [4] = 0.0298 } : new Dictionary<int, double>(),
            fullPedal);

    private static PerfFrame Frame(LiveSample sample) =>
        PerfChannels.Read(Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt);

    private static PerfFrame Decode(RecordedSample sample) =>
        PerfChannels.Read(Ms43x, Ms43x.DecodePayload(sample.Payload), sample.Payload, sample.At);

    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Frames(IEnumerable<LiveSample> samples) =>
        samples.Select(s => (Frame(s), s.Payload)).ToArray();

    // Frames of a until the first one where from is true, then those of b: two drives on one timeline spliced.
    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Splice(IReadOnlyList<LiveSample> a, IReadOnlyList<LiveSample> b,
        Func<PerfFrame, bool> from)
    {
        var first = Frames(a);
        var second = Frames(b);
        Assert.Equal(first.Length, second.Length);
        var at = Array.FindIndex(first, x => from(x.Frame));
        Assert.True(at > 0);
        return [.. first.Take(at), .. second.Skip(at)];
    }

    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Later(IEnumerable<(PerfFrame Frame, ReadOnlyMemory<byte> Payload)> frames,
        double seconds) =>
        frames.Select(x => (x.Frame with { At = x.Frame.At + TimeSpan.FromSeconds(seconds) }, x.Payload)).ToArray();

    private void Drive(PerfRun run, IEnumerable<(PerfFrame Frame, ReadOnlyMemory<byte> Payload)> frames)
    {
        foreach (var (frame, payload) in frames)
        {
            warmUp.Push(frame);
            run.Push(frame, payload);
        }
    }

    private void Drive(PerfRun run, IEnumerable<LiveSample> samples) => Drive(run, Frames(samples));

    private static IReadOnlyList<LiveSample> ThirdPull(double pedalPercent = 100, double coolantC = 92, double oilC = 88, double knockCyl4 = 0) =>
        SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, coolantC: coolantC, oilC: oilC, pedalPercent: pedalPercent, knockCyl4: knockCyl4);

    private static Figure Fig(RunFigures figures, string key) => Assert.Single(figures.Figures, f => f.Key == key);

    private static RunFlag Flag(RunFigures figures, string code) => Assert.Single(figures.Flags, f => f.Code == code);

    private static void AllFinite(RunFigures figures)
    {
        Assert.All(figures.Figures, f => Assert.True(f.Value is null || double.IsFinite(f.Value.Value), f.Key));
        Assert.All(figures.Traces, t =>
        {
            Assert.Equal(t.X.Count, t.Y.Count);
            Assert.All(t.X.Concat(t.Y), v => Assert.True(double.IsFinite(v), t.Key));
        });
    }

    [Fact]
    public void PullWaitsThenTimesItsBand()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        var changes = 0;
        run.Changed += (_, _) => changes++;
        var frames = Frames(ThirdPull());
        var hold = frames.TakeWhile(x => x.Frame.At < TimeSpan.FromSeconds(1)).ToArray();

        Drive(run, hold);
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Equal("Waiting for full throttle…", run.Status);
        // Full throttle is not a check: it cannot be met with the pedal released, and the status asks for it (minor review,
        // finding 38).
        Assert.Equal(new[] { "Engine warm", "Pedal calibrated", "3rd gear", "Between 1300 and 1800 rpm", "Pedal released" },
            run.Checks.Select(c => c.Label));
        Assert.All(run.Checks, c => Assert.True(c.Met, c.Label));
        Assert.Equal("1700 rpm", run.Checks[3].Detail);
        Assert.Equal(hold.Length, changes);

        Drive(run, frames.Skip(hold.Length));
        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.Equal("Done", run.Status);
        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        Assert.Same(run.Spec, outcome.Spec);
        var time = Fig(outcome.Figures, "time");
        Assert.Equal("2000 to 6400 rpm", time.Label);
        Assert.Equal(("s", 2), (time.Unit, time.Decimals));
        Assert.Equal(6.0 * 4400 / 4800, time.Value!.Value, 0.02);
        // 800 rpm/s; every mark lies inside the band.
        Assert.Equal(2.5, Fig(outcome.Figures, "t-2000-4000").Value!.Value, 0.02);
        Assert.Equal(3.75, Fig(outcome.Figures, "t-3000-6000").Value!.Value, 0.02);
        Assert.Equal(2.5, Fig(outcome.Figures, "t-4000-6000").Value!.Value, 0.02);
        Assert.Equal("4000 to 6000 rpm", Fig(outcome.Figures, "t-4000-6000").Label);

        var power = Fig(outcome.Figures, "peak-power");
        Assert.Equal(("Peak power, wheels (estimate)", "kW", 0), (power.Label, power.Unit, power.Decimals));
        Assert.True(power.Value > 0);
        Assert.InRange(Fig(outcome.Figures, "peak-power-rpm").Value!.Value, 2000, 6400);
        var torque = Fig(outcome.Figures, "peak-torque");
        Assert.Equal(("Peak torque, wheels (estimate)", "Nm"), (torque.Label, torque.Unit));
        Assert.True(torque.Value > 0);
        Assert.InRange(Fig(outcome.Figures, "peak-torque-rpm").Value!.Value, 2000, 6400);
        Assert.Equal(0, Fig(outcome.Figures, "knock-max").Value);
        Assert.Null(Fig(outcome.Figures, "knock-cyl").Value);
        Assert.Equal(25, Fig(outcome.Figures, "iat-start").Value!.Value, 0.5);
        Assert.Equal(25, Fig(outcome.Figures, "iat-end").Value!.Value, 0.5);
        Assert.Matches(@"^5\.5\d s, \d+ kW$", outcome.Figures.Headline);
        Assert.Equal($"{time.Value:0.00} s, {power.Value:0} kW", outcome.Figures.Headline);

        Assert.Equal(new[] { "power", "torque", "ignition", "knock", "vanos-in", "vanos-ex", "afr", "maf", "load", "injdc", "dme-torque", "rpm-time" },
            outcome.Figures.Traces.Select(t => t.Key));
        var powerTrace = outcome.Figures.Traces[0];
        Assert.Equal(("rpm", "kW"), (powerTrace.XUnit, powerTrace.YUnit));
        Assert.Equal(2000, powerTrace.X[0]);
        Assert.Equal(6250, powerTrace.X[^1]);
        Assert.Equal(power.Value, powerTrace.Y.Max());
        var rpmTime = outcome.Figures.Traces[^1];
        Assert.Equal(("s", "rpm"), (rpmTime.XUnit, rpmTime.YUnit));
        Assert.Equal((0d, 2000d), (rpmTime.X[0], rpmTime.Y[0]));
        Assert.Empty(outcome.Figures.Flags);
        AllFinite(outcome.Figures);

        Assert.Equal(1, run.Progress);
        Assert.Equal(time.Value!.Value, run.Elapsed, 1e-9);
        Assert.Equal((0d, 2000d), run.Live[0]);
        Assert.True(run.Live[^1].Rpm >= 6400);
        Assert.Equal(6, run.Knock.Count);
        Assert.Equal(92, outcome.Start.CoolantC!.Value, 0.5);
        Assert.Equal(88, outcome.End.OilC!.Value, 0.5);
        // AMP steps 2.12 kPa: 101.3 reads 101.9.
        Assert.Equal(101.9, outcome.Start.PressureKpa!.Value, 0.05);
    }

    [Fact]
    public void PullIsIncompleteWhenThePedalLifts()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(), ThirdPull(pedalPercent: 50), f => f.Rpm >= 5000));

        Assert.Equal(RunStage.Finished, run.Stage);
        var outcome = run.Outcome!;
        Assert.False(outcome.Complete);
        var flag = Flag(outcome.Figures, "incomplete");
        Assert.Equal(FlagSeverity.Invalid, flag.Severity);
        Assert.Contains("pedal lifted", flag.Message);
        Assert.Matches(@"^Incomplete: pedal lifted at 50\d\d rpm$", run.Status);
        Assert.Equal(run.Status + ".", flag.Message);

        // What it measured stays; what it did not reach is n/a.
        Assert.Null(Fig(outcome.Figures, "time").Value);
        Assert.Null(Fig(outcome.Figures, "t-3000-6000").Value);
        Assert.Equal(2.5, Fig(outcome.Figures, "t-2000-4000").Value!.Value, 0.02);
        Assert.NotNull(Fig(outcome.Figures, "peak-power").Value);
        Assert.Matches(@"^\d+ kW$", outcome.Figures.Headline);

        // The window ends on the last sample at full throttle.
        Assert.Equal(outcome.Samples.Count - 1, outcome.WindowEnd);
        var last = Decode(outcome.Samples[^1]);
        Assert.True(last.Pedal >= 90);
        Assert.True(last.Rpm < 5000);
    }

    [Fact]
    public void ColdEngineDoesNotStart()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, ThirdPull(coolantC: 64));

        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Null(run.Outcome);
        var warm = Assert.Single(run.Checks, c => c.Label == "Engine warm");
        Assert.False(warm.Met);
        Assert.Equal("Coolant 64 °C", warm.Detail);
        Assert.Equal("Waiting for the ready checks…", run.Status);
    }

    [Fact]
    public void PedalNotCalibratedDoesNotStart()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(fullPedal: 0), warmUp);
        Drive(run, ThirdPull());

        Assert.Equal(RunStage.Waiting, run.Stage);
        var check = Assert.Single(run.Checks, c => c.Label == "Pedal calibrated");
        Assert.False(check.Met);
        Assert.Equal("Calibrate the pedal first", check.Detail);
        Assert.Equal("Pedal n/a", Assert.Single(run.Checks, c => c.Label == "Pedal released").Detail);
    }

    [Fact]
    public void KnockIsReportedPerCylinder()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, ThirdPull(knockCyl4: 1.5));

        var figures = run.Outcome!.Figures;
        Assert.Equal(1.5, Fig(figures, "knock-max").Value!.Value, 3);
        Assert.Equal(4, Fig(figures, "knock-cyl").Value);
        var flag = Flag(figures, "knock");
        Assert.Equal(FlagSeverity.Suspect, flag.Severity);
        Assert.Equal("Knock retard on cylinder 4 reached 1.5 °CRK between 5000 and 5600 rpm.", flag.Message);
        var knock = Assert.Single(figures.Traces, t => t.Key == "knock");
        Assert.Equal(1.5, knock.Y.Max(), 3);
        // Which cylinder pulled the most, bin by bin, where any did (spec 6.1; minor review, finding 5).
        var cylinder = Assert.Single(figures.Traces, t => t.Key == "knock-cyl");
        Assert.Equal(("rpm", ""), (cylinder.XUnit, cylinder.YUnit));
        Assert.Equal(new double[] { 5000, 5250, 5500 }, cylinder.X);
        Assert.All(cylinder.Y, y => Assert.Equal(4, y));
    }

    [Fact]
    public void KnockCheckSummarisesEachCylinder()
    {
        var run = new PerfRun(PerfTestSpec.KnockCheck(), Inputs(), warmUp);
        Drive(run, SyntheticRuns.Pull(Ms43x, 4, 1100, 4700, 5.0, kmhPer1000: 29.8, knockCyl4: 1.5, knockFromRpm: 3000, knockToRpm: 3500));

        Assert.Equal("Between 800 and 1300 rpm", run.Checks[3].Label);
        Assert.Equal("4th gear", run.Checks[2].Label);
        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var figures = outcome.Figures;
        Assert.Equal(1.5, Fig(figures, "knock-max").Value!.Value, 3);
        Assert.Equal(4, Fig(figures, "knock-cyl").Value);
        Assert.Equal(1, Fig(figures, "knock-events").Value);
        Assert.Equal(1.5, Fig(figures, "knock-max-4").Value!.Value, 3);
        Assert.Equal(0, Fig(figures, "knock-max-1").Value);
        Assert.Equal(("ron-start", 2), (Fig(figures, "ron-start").Key, Fig(figures, "ron-start").Decimals));
        Assert.NotNull(Fig(figures, "ron-end").Value);
        Assert.Equal("1.5 °CRK on cylinder 4", figures.Headline);
        // Angles in crank degrees, as the ADX names them (minor review, finding 36).
        Assert.Equal("°CRK", Fig(figures, "knock-max").Unit);
        Assert.Equal("Knock retard on cylinder 4 reached 1.5 °CRK between 3000 and 3500 rpm.", Flag(figures, "knock").Message);
        foreach (var key in new[] { "ignition", "knock", "knock-1", "knock-2", "knock-3", "knock-4", "knock-5", "knock-6" })
        {
            Assert.Contains(figures.Traces, t => t.Key == key);
        }

        Assert.Equal(1.5, Assert.Single(figures.Traces, t => t.Key == "knock-4").Y.Max(), 3);
        Assert.Equal(0, Assert.Single(figures.Traces, t => t.Key == "knock-1").Y.Max());
    }

    // Stock definitions log no knock per cylinder and no AFR target: n/a, never zero (review focus 2).
    [Fact]
    public void PullOnAStockDefinitionHasNoKnockFigures()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Frames(ThirdPull()).Select(x => (x.Frame with { KnockRetard = [], AfrTarget = null }, x.Payload)));

        var figures = run.Outcome!.Figures;
        Assert.Null(Fig(figures, "knock-max").Value);
        Assert.Null(Fig(figures, "knock-cyl").Value);
        Assert.DoesNotContain(figures.Traces, t => t.Key is "knock" or "afr");
        Assert.DoesNotContain(figures.Flags, f => f.Code == "knock");
        Assert.Empty(run.Knock);
        Assert.Equal(5.5, Fig(figures, "time").Value!.Value, 0.02);
    }

    [Fact]
    public void StandingStartIsBackExtrapolated()
    {
        var run = new PerfRun(PerfTestSpec.StandingStart(), Inputs(), warmUp);
        var frames = Frames(SyntheticRuns.Standing(Ms43x, 7.6));
        var stopped = frames.TakeWhile(x => x.Frame.At < TimeSpan.FromSeconds(1.5)).ToArray();

        Drive(run, stopped);
        Assert.Equal(new[] { "Engine warm", "Stopped" }, run.Checks.Select(c => c.Label));
        Assert.All(run.Checks, c => Assert.True(c.Met, c.Label));
        Assert.Equal("Waiting for the launch…", run.Status);

        Drive(run, frames.Skip(stopped.Length));
        Assert.Equal(RunStage.Finished, run.Stage);
        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        Assert.Equal(7.6, Fig(outcome.Figures, "t-0-100").Value!.Value, 0.05);
        Assert.Equal(7.6 / 2, Fig(outcome.Figures, "t-0-50").Value!.Value, 0.05);
        Assert.Equal(7.6 / 2, Fig(outcome.Figures, "t-50-100").Value!.Value, 0.05);
        Assert.DoesNotContain(outcome.Figures.Figures, f => f.Key is "t-0-200" or "t-100-200");
        // The engine speed on the last stopped sample: the synthetic start slips the clutch at 1500 rpm.
        Assert.Equal(1500, Fig(outcome.Figures, "launch-rpm").Value);
        Assert.Equal("7.6 s to 100", outcome.Figures.Headline);
        var speed = Assert.Single(outcome.Figures.Traces, t => t.Key == "speed-time");
        Assert.Equal(("s", "km/h"), (speed.XUnit, speed.YUnit));
        Assert.Empty(outcome.Figures.Flags);

        // The window starts at the first moving sample.
        Assert.Equal(0, Decode(outcome.Samples[outcome.WindowStart - 1]).SpeedKmh);
        Assert.True(Decode(outcome.Samples[outcome.WindowStart]).SpeedKmh > 0);
        AllFinite(outcome.Figures);
    }

    // DSC cutting torque on the launch is said with its speeds: the times are then traction-limited (spec 6.4; final review,
    // finding 4).
    [Fact]
    public void DscInterventionFlagsAStandingStart()
    {
        var frames = Frames(SyntheticRuns.Standing(Ms43x, 7.6))
            .Select(x => x.Frame with { TorqueIntervention = x.Frame.SpeedKmh is >= 15 and <= 32 })
            .ToArray();

        var figures = RunAnalysis.Analyse(PerfTestSpec.StandingStart(), frames, Inputs(), complete: true);

        var dsc = Flag(figures, "dsc");
        Assert.Equal(FlagSeverity.Suspect, dsc.Severity);
        Assert.Equal("DSC intervened on engine torque between 15 and 32 km/h: the times are traction-limited.", dsc.Message);
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.StandingStart(), frames.Select(f => f with { TorqueIntervention = null }).ToArray(),
            Inputs(), complete: true).Flags, f => f.Code == "dsc");
    }

    [Fact]
    public void RollOnTimesItsSpeedRange()
    {
        // 3rd at 0.0223 km/h per rpm: 2152 rpm is 48 km/h, 4619 rpm 103 km/h.
        var run = new PerfRun(PerfTestSpec.RollOn(3), Inputs(), warmUp);
        Drive(run, SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0, holdSeconds: 2));

        Assert.Equal("Steady between 45 and 55 km/h", run.Checks[3].Label);
        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var expected = 40 / ((4619 - 2152) / 4.0 * 0.0223);
        var time = Fig(outcome.Figures, "time");
        Assert.Equal("60 to 100 km/h", time.Label);
        Assert.Equal(expected, time.Value!.Value, 0.02);
        Assert.Equal(new[] { "split-60-70", "split-70-80", "split-80-90", "split-90-100" },
            outcome.Figures.Figures.Where(f => f.Key.StartsWith("split-", StringComparison.Ordinal)).Select(f => f.Key));
        Assert.Equal(expected / 4, Fig(outcome.Figures, "split-70-80").Value!.Value, 0.02);
        Assert.Equal("70 to 80 km/h", Fig(outcome.Figures, "split-70-80").Label);
        Assert.Equal($"{time.Value:0.00} s", outcome.Figures.Headline);
        Assert.Contains(outcome.Figures.Traces, t => t.Key == "speed-time");
        Assert.Contains(outcome.Figures.Traces, t => t.Key == "rpm-time");
        Assert.Empty(outcome.Figures.Flags);
    }

    [Fact]
    public void UncalibratedLateRollOnIsFlagged()
    {
        // Half pedal from 48 km/h, full throttle only at 58 km/h, and no speed calibration.
        var full = SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0, holdSeconds: 2);
        var half = SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0, pedalPercent: 50, holdSeconds: 2);
        var run = new PerfRun(PerfTestSpec.RollOn(3), Inputs(calibrated: false), warmUp);
        Drive(run, Splice(half, full, f => f.SpeedKmh >= 58));

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        Assert.Equal(FlagSeverity.Info, Flag(outcome.Figures, "uncalibrated").Severity);
        var late = Flag(outcome.Figures, "late-throttle");
        Assert.Equal(FlagSeverity.Suspect, late.Severity);
        Assert.Equal("Full throttle came only 2 km/h before 60 km/h.", late.Message);
        Assert.Equal(40 / ((4619 - 2152) / 4.0 * 0.0223), Fig(outcome.Figures, "time").Value!.Value, 0.05);
        // Most severe first.
        Assert.Equal("late-throttle", outcome.Figures.Flags[0].Code);
    }

    // A GPS-corrected calibration holds true km/h per rpm while VS is still the speedometer's reading: the slip check compares
    // VS with the speedometer's own ratio, so a speedometer 3.5 % fast is no slip (final review, finding 3).
    [Fact]
    public void AGpsCorrectedCalibrationIsNoSlip()
    {
        var corrected = new Dictionary<int, double> { [3] = 0.0223 };
        var speedometer = new Dictionary<int, double> { [3] = 0.0223 * 1.035 };
        var gpsChecked = new AnalysisInputs(new CarValues(1480, "205/55 R16", false), corrected, SyntheticRuns.FullPedal, speedometer);
        var pull = Frames(SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, kmhPer1000: 22.3 * 1.035)).Select(x => x.Frame).ToArray();
        var rollOn = Frames(SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0, kmhPer1000: 22.3 * 1.035)).Select(x => x.Frame).ToArray();

        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), pull, gpsChecked, complete: true).Flags, f => f.Code == "slip");
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.RollOn(3), rollOn, gpsChecked, complete: true).Flags, f => f.Code == "slip");

        // VS 3.5 % away from the speedometer's own ratio is still slip.
        var trueSpeedometer = gpsChecked with { SpeedometerKmhPerRpm = corrected };
        var slip = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), pull, trueSpeedometer, complete: true), "slip");
        Assert.Equal(FlagSeverity.Suspect, slip.Severity);
    }

    [Fact]
    public void LinkTestReportsTheRate()
    {
        var run = new PerfRun(PerfTestSpec.LinkTest(), Inputs(), warmUp);
        Assert.Equal(RunStage.Running, run.Stage);
        Drive(run, SyntheticRuns.Idle(Ms43x, 31));

        Assert.Equal(RunStage.Finished, run.Stage);
        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var figures = outcome.Figures;
        var rate = Fig(figures, "rate");
        Assert.Equal(("samples/s", 1), (rate.Unit, rate.Decimals));
        Assert.Equal(30, rate.Value!.Value, 0.5);
        Assert.Equal(0, Fig(figures, "gaps").Value);
        Assert.Equal(1000 / 30.0, Fig(figures, "interval-median").Value!.Value, 0.5);
        Assert.Equal(1000 / 30.0, Fig(figures, "interval-p95").Value!.Value, 0.5);
        Assert.Equal(1000 / 30.0, Fig(figures, "interval-max").Value!.Value, 0.5);
        Assert.Equal("ms", Fig(figures, "interval-max").Unit);
        Assert.Equal("30.0 samples/s", figures.Headline);
        Assert.Empty(figures.Flags);
        Assert.Equal(30, outcome.Samples[^1].At.TotalSeconds, 1e-6);
        Assert.Equal((0, outcome.Samples.Count - 1), (outcome.WindowStart, outcome.WindowEnd));
        Assert.Equal(1, run.Progress);
    }

    [Fact]
    public void GapFlagsTheRun()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, ThirdPull().Where(s => s.RequestedAt < TimeSpan.FromSeconds(3) || s.RequestedAt >= TimeSpan.FromSeconds(3.45)));

        var figures = run.Outcome!.Figures;
        var gap = Flag(figures, "gap");
        Assert.Equal(FlagSeverity.Suspect, gap.Severity);
        Assert.Equal("A 500 ms gap in the samples.", gap.Message);
        AllFinite(figures);
        Assert.Equal(5.5, Fig(figures, "time").Value!.Value, 0.02);
    }

    // Review focus 4: retry bursts give several samples at one instant, and a stamp may step back; the figures stay finite and
    // the run says its timing is uneven (minor review, finding 55).
    [Fact]
    public void BurstyStampsStayFinite()
    {
        var frames = Frames(ThirdPull());
        var bursty = frames.Select((x, i) =>
            i % 7 == 3 ? (x.Frame with { At = frames[i - 1].Frame.At }, x.Payload)
            : i == 120 ? (x.Frame with { At = frames[i - 1].Frame.At - TimeSpan.FromMilliseconds(5) }, x.Payload)
            : x);
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, bursty);

        Assert.Equal(RunStage.Finished, run.Stage);
        AllFinite(run.Outcome!.Figures);
        Assert.Equal(5.5, Fig(run.Outcome.Figures, "time").Value!.Value, 0.05);
        Assert.NotNull(Fig(run.Outcome.Figures, "peak-power").Value);
        var timing = Flag(run.Outcome.Figures, "timing");
        Assert.Equal(FlagSeverity.Suspect, timing.Severity);
        Assert.Matches(@"^\d+ samples share a time stamp or step back: the timing is uneven\.$", timing.Message);
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), frames.Select(x => x.Frame).ToArray(), Inputs(), complete: true).Flags,
            f => f.Code == "timing");
    }
}
