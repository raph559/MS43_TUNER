using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

// The run state machine and its analysis are performance spec 6 and 7; the figure keys and flags are the plan's.
public class PerfRunTests
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    // The shared monitor, fed every frame before the run sees it, as Live feeds it.
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
        // Full throttle is not a check: it cannot be met with the pedal released, and the status asks for it.
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
        Assert.Equal(("Peak torque, crank, from wheel power (estimate)", "Nm"), (torque.Label, torque.Unit));
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
        // Which cylinder pulled the most, bin by bin, where any did.
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
        // Angles in crank degrees, as the ADX names them.
        Assert.Equal("°CRK", Fig(figures, "knock-max").Unit);
        Assert.Equal("Knock retard on cylinder 4 reached 1.5 °CRK between 3000 and 3500 rpm.", Flag(figures, "knock").Message);
        foreach (var key in new[] { "ignition", "knock", "knock-1", "knock-2", "knock-3", "knock-4", "knock-5", "knock-6" })
        {
            Assert.Contains(figures.Traces, t => t.Key == key);
        }

        Assert.Equal(1.5, Assert.Single(figures.Traces, t => t.Key == "knock-4").Y.Max(), 3);
        Assert.Equal(0, Assert.Single(figures.Traces, t => t.Key == "knock-1").Y.Max());
    }

    // Stock definitions log no knock per cylinder and no AFR target: n/a, never zero.
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

    // DSC cutting torque on the launch is said with its speeds: the times are then traction-limited.
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
    // VS with the speedometer's own ratio, so a speedometer 3.5 % fast is no slip.
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

    // Retry bursts give several samples at one instant, and a stamp may step back; the figures stay finite and
    // the run says its timing is uneven.
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

    [Fact]
    public void LinkLostEndsARunAsIncomplete()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Frames(ThirdPull()).TakeWhile(x => x.Frame.Rpm < 4500));
        Assert.Equal(RunStage.Running, run.Stage);

        run.LinkLost();
        Assert.Equal(RunStage.Finished, run.Stage);
        var outcome = run.Outcome!;
        Assert.False(outcome.Complete);
        var flag = Flag(outcome.Figures, "link-lost");
        Assert.Equal(FlagSeverity.Invalid, flag.Severity);
        Assert.Matches(@"^Incomplete: link lost at 44\d\d rpm$", run.Status);
        Assert.Equal(run.Status + ".", flag.Message);
        Assert.DoesNotContain(outcome.Figures.Flags, f => f.Code == "incomplete");
        Assert.Equal(2.5, Fig(outcome.Figures, "t-2000-4000").Value!.Value, 0.02);

        // Waiting, or past the trigger but before the band, there is nothing to keep.
        var waiting = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(waiting, Frames(ThirdPull()).Take(10));
        waiting.LinkLost();
        Assert.Equal(RunStage.Cancelled, waiting.Stage);
        Assert.Null(waiting.Outcome);

        var early = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(early, Frames(ThirdPull()).TakeWhile(x => x.Frame.Rpm < 1900 || x.Frame.At < TimeSpan.FromSeconds(1)));
        Assert.Equal(RunStage.Running, early.Stage);
        early.LinkLost();
        Assert.Equal(RunStage.Cancelled, early.Stage);
        Assert.Null(early.Outcome);
    }

    [Fact]
    public void AnalysisIsPure()
    {
        var spec = PerfTestSpec.Pull(3);
        var frames = Frames(ThirdPull(knockCyl4: 1.5)).Select(x => x.Frame).ToArray();
        var a = RunAnalysis.Analyse(spec, frames, Inputs(), complete: true);
        var b = RunAnalysis.Analyse(spec, frames, Inputs(), complete: true);

        Assert.Equal(a.Figures, b.Figures);
        Assert.Equal(a.Flags, b.Flags);
        Assert.Equal(a.Headline, b.Headline);
        Assert.Equal(a.Traces.Select(t => t.Key), b.Traces.Select(t => t.Key));
        for (var i = 0; i < a.Traces.Count; i++)
        {
            Assert.Equal(a.Traces[i].X, b.Traces[i].X);
            Assert.Equal(a.Traces[i].Y, b.Traces[i].Y);
        }

        // A run's figures are the analysis of the samples it keeps, so a stored result can be recomputed.
        var run = new PerfRun(spec, Inputs(), warmUp);
        Drive(run, ThirdPull(knockCyl4: 1.5));
        var kept = run.Outcome!.Samples.Select(Decode).ToArray();
        Assert.Equal(run.Outcome.Figures.Figures, RunAnalysis.Analyse(spec, kept, Inputs(), complete: true).Figures);
    }

    [Fact]
    public void SamplesStartTwoSecondsEarly()
    {
        // 3 s more of the hold before the pull, so more than 2 s are there to keep.
        var pull = Frames(ThirdPull());
        var lead = Enumerable.Range(0, 90).Select(i => (pull[0].Frame with { At = TimeSpan.FromSeconds(i / 30.0) }, pull[0].Payload));
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, lead.Concat(Later(pull, 3)));

        var outcome = run.Outcome!;
        var windowAt = outcome.Samples[outcome.WindowStart].At;
        Assert.True(outcome.Samples[0].At <= windowAt - TimeSpan.FromSeconds(2));
        Assert.True(outcome.Samples[1].At > windowAt - TimeSpan.FromSeconds(2));
        Assert.True(Decode(outcome.Samples[outcome.WindowStart]).Rpm >= 2000);
        Assert.True(Decode(outcome.Samples[outcome.WindowStart - 1]).Rpm < 2000);
        Assert.Equal(outcome.Samples.Count - 1, outcome.WindowEnd);
        Assert.True(Decode(outcome.Samples[^1]).Rpm >= 6400);
        Assert.Equal(5.5, Fig(outcome.Figures, "time").Value!.Value, 0.02);

        // With under 2 s before the start, everything there was is kept.
        var shortLead = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(shortLead, pull);
        Assert.Equal(TimeSpan.Zero, shortLead.Outcome!.Samples[0].At);
    }

    [Fact]
    public void AFalseStartGoesBackToWaiting()
    {
        // Floored, then lifted at 1900 rpm before the band: nothing was timed, so the run waits again.
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(), ThirdPull(pedalPercent: 10), f => f.Rpm >= 1900).TakeWhile(x => x.Frame.Rpm < 2300));
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Null(run.Outcome);
        Assert.Equal("Waiting for the ready checks…", run.Status);

        Drive(run, Later(Frames(ThirdPull()), 10));
        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.True(run.Outcome!.Complete);
        Assert.Equal(5.5, Fig(run.Outcome.Figures, "time").Value!.Value, 0.02);
        // Of the false start, at most the sample straddling the 2 s before the window is kept.
        Assert.True(run.Outcome.Samples.Count(s => s.At < TimeSpan.FromSeconds(10)) <= 1);
    }

    [Fact]
    public void GearChangeEndsThePull()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(), SyntheticRuns.Pull(Ms43x, 4, 1700, 6500, 6.0), f => f.Rpm >= 4000));

        Assert.False(run.Outcome!.Complete);
        Assert.Matches(@"^Incomplete: gear changed at 40\d\d rpm$", run.Status);
        Assert.Equal(3, Decode(run.Outcome.Samples[^1]).Gear);
    }

    [Fact]
    public void ColdMidRunIsKeptButMarkedCold()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(), ThirdPull(coolantC: 70), f => f.Rpm >= 4000));

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var cold = Flag(outcome.Figures, "cold");
        Assert.Equal(FlagSeverity.Cold, cold.Severity);
        Assert.Equal("Engine cold: not comparable.", cold.Message);
        Assert.Equal(70, outcome.End.CoolantC!.Value, 0.5);
    }

    [Fact]
    public void ColdOilMarksTheRunSuspect()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, ThirdPull(oilC: 60));

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var oil = Flag(outcome.Figures, "oil-cold");
        Assert.Equal(FlagSeverity.Suspect, oil.Severity);
        Assert.Equal("Oil 60 °C: below working temperature (80 °C).", oil.Message);
        Assert.DoesNotContain(outcome.Figures.Flags, f => f.Code == "cold");
    }

    [Fact]
    public void SpeedCalibrationCapturesASteadyGear()
    {
        var run = new PerfRun(PerfTestSpec.SpeedCalibration(), Inputs(), warmUp);
        Assert.Equal(RunStage.Running, run.Stage);

        // 100 km/h at 3000 rpm in 5th, light pedal, for 5 s.
        Drive(run, SyntheticRuns.Pull(Ms43x, 5, 3000, 3000, 4.0, kmhPer1000: 100 / 3.0, pedalPercent: 20));
        Assert.Equal(100 / 3000.0, Assert.Single(run.Captured).Value, 1e-9);
        Assert.Equal(5, run.Captured.Keys.Single());
        Assert.Equal("Captured 5th gear", run.Status);
        Assert.Equal(RunStage.Running, run.Stage);
        Assert.Null(run.Outcome);

        run.Cancel();
        Assert.Equal(RunStage.Cancelled, run.Stage);
        Assert.Single(run.Captured);

        // Below 50 km/h nothing is captured.
        var slow = new PerfRun(PerfTestSpec.SpeedCalibration(), Inputs(), warmUp);
        Drive(slow, SyntheticRuns.Pull(Ms43x, 2, 3000, 3000, 4.0, kmhPer1000: 40 / 3.0, pedalPercent: 20));
        Assert.Empty(slow.Captured);
    }

    [Fact]
    public void PedalStepReadsTheHighestPedal()
    {
        // Ignition on and engine off, the pedal pressed to 96 and held.
        var run = new PerfRun(PerfTestSpec.Pedal(), Inputs(fullPedal: 0), warmUp);
        Assert.Equal(RunStage.Running, run.Stage);
        Drive(run, SyntheticRuns.Pull(Ms43x, 0, 0, 0, 3.0, pedalPercent: 96));

        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.Equal(96, run.PedalReading!.Value, 0.5);
        Assert.Null(run.Outcome);

        // With the engine running it waits.
        var running = new PerfRun(PerfTestSpec.Pedal(), Inputs(fullPedal: 0), warmUp);
        Drive(running, SyntheticRuns.Pull(Ms43x, 0, 750, 750, 3.0, pedalPercent: 96));
        Assert.Equal(RunStage.Running, running.Stage);
        Assert.False(Assert.Single(running.Checks, c => c.Label == "Engine off").Met);
    }

    [Fact]
    public void CancelStopsARun()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        var frames = Frames(ThirdPull());
        Drive(run, frames.Take(80));
        Assert.Equal(RunStage.Running, run.Stage);
        var changed = false;
        run.Changed += (_, _) => changed = true;

        run.Cancel();
        Assert.True(changed);
        Assert.Equal(RunStage.Cancelled, run.Stage);
        Assert.Equal("Cancelled", run.Status);
        Drive(run, frames.Skip(80));
        Assert.Equal(RunStage.Cancelled, run.Stage);
        Assert.Null(run.Outcome);
    }

    // Spec 5: oil that is not reported marks a run suspect, as cold oil does.
    [Fact]
    public void UnreportedOilMarksTheRunSuspect()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Frames(ThirdPull()).Select(x => (x.Frame with { OilC = null }, x.Payload)));

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        var oil = Flag(outcome.Figures, "oil-unreported");
        Assert.Equal(FlagSeverity.Suspect, oil.Severity);
        Assert.Equal("Oil temperature not reported by this ECU.", oil.Message);
        Assert.Null(outcome.Start.OilC);
        Assert.DoesNotContain(outcome.Figures.Flags, f => f.Code == "oil-cold");
    }

    // Oil at 80 °C or more that has not held there for 10 s yet is still amber, and the flag says so: it never calls a
    // reading above the target "below" it.
    [Fact]
    public void OilStillInItsHoldIsNotCalledBelow()
    {
        var frames = Frames(ThirdPull(oilC: 88));
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        // One cold reading just before the pull: its 10 s hold runs through the whole pull.
        Drive(run, [(frames[0].Frame with { OilC = 70 }, frames[0].Payload), .. Later(frames, 0.5)]);

        var oil = Flag(run.Outcome!.Figures, "oil-cold");
        Assert.Equal(FlagSeverity.Suspect, oil.Severity);
        Assert.Equal("Oil 88 °C: had not held 80 °C for 10 s yet.", oil.Message);
    }

    // A standing start that reached 100 km/h keeps its times when the link drops later; only what it never reached is
    // missing. Before 100 km/h it is still incomplete.
    [Fact]
    public void LinkLostAfterAHundredKeepsTheStandingStart()
    {
        var frames = Frames(SyntheticRuns.Standing(Ms43x, 7.6));
        var top = Array.FindIndex(frames, x => x.Frame.SpeedKmh >= 100);
        var run = new PerfRun(PerfTestSpec.StandingStart(), Inputs(), warmUp);
        Drive(run, frames.Take(top + 5));
        Assert.Equal(RunStage.Running, run.Stage);

        run.LinkLost();

        var outcome = run.Outcome!;
        Assert.True(outcome.Complete);
        Assert.Equal("Done", run.Status);
        Assert.Equal(7.6, Fig(outcome.Figures, "t-0-100").Value!.Value, 0.05);
        var lost = Flag(outcome.Figures, "link-lost");
        Assert.Equal(FlagSeverity.Info, lost.Severity);
        Assert.Equal("Link lost at 100 km/h: the times it reached are kept.", lost.Message);
        Assert.DoesNotContain(outcome.Figures.Flags, f => f.Severity == FlagSeverity.Invalid);

        var early = new PerfRun(PerfTestSpec.StandingStart(), Inputs(), warmUp);
        Drive(early, frames.TakeWhile(x => x.Frame.SpeedKmh < 60));
        early.LinkLost();
        Assert.False(early.Outcome!.Complete);
        Assert.Equal(FlagSeverity.Invalid, Flag(early.Outcome.Figures, "link-lost").Severity);
    }

    // Floored only once the revs were past the start, the pull cannot be timed from 2000 rpm: the run says so and waits for a
    // lift, then a proper pull runs.
    [Fact]
    public void FullThrottleAboveTheStartSaysWhy()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(pedalPercent: 50), ThirdPull(), f => f.Rpm >= 2100).TakeWhile(x => x.Frame.Rpm < 3000));

        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Null(run.Outcome);
        Assert.Equal("Full throttle came above 2000 rpm: lift off and start again below 1800 rpm.", run.Status);

        Drive(run, Later(Frames(ThirdPull()), 10));
        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.True(run.Outcome!.Complete);
    }

    // The ready checks hold from the failing side too: held outside its window a pull or roll-on never starts, a standing
    // start stopped for under 1 s never arms, and a calibration held for 2.5 s captures nothing.
    [Fact]
    public void ReadyChecksRefuseOutsideTheirWindow()
    {
        foreach (var held in new[] { 1900.0, 1200 })
        {
            var frames = Frames(SyntheticRuns.Pull(Ms43x, 3, held, 6500, 6.0));
            var pull = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
            Drive(pull, frames.TakeWhile(x => x.Frame.Pedal < 90));
            var window = Assert.Single(pull.Checks, c => c.Label == "Between 1300 and 1800 rpm");
            Assert.False(window.Met);
            Assert.Equal($"{held} rpm", window.Detail);

            Drive(pull, frames.SkipWhile(x => x.Frame.Pedal < 90));
            Assert.Equal(RunStage.Waiting, pull.Stage);
            Assert.Null(pull.Outcome);
        }

        // 2601 rpm in 3rd is 58 km/h: inside 5 km/h of the start.
        var rollOn = new PerfRun(PerfTestSpec.RollOn(3), Inputs(), warmUp);
        Drive(rollOn, SyntheticRuns.Pull(Ms43x, 3, 2601, 4619, 4.0, holdSeconds: 2));
        Assert.Equal(RunStage.Waiting, rollOn.Stage);
        Assert.Null(rollOn.Outcome);

        var standing = new PerfRun(PerfTestSpec.StandingStart(), Inputs(), warmUp);
        var launch = Frames(SyntheticRuns.Standing(Ms43x, 7.6)).Where(x => x.Frame.At >= TimeSpan.FromSeconds(1.5)).ToArray();
        Drive(standing, launch.TakeWhile(x => x.Frame.SpeedKmh <= 0));
        Assert.False(Assert.Single(standing.Checks, c => c.Label == "Stopped").Met);
        Drive(standing, launch.SkipWhile(x => x.Frame.SpeedKmh <= 0));
        Assert.Equal(RunStage.Waiting, standing.Stage);
        Assert.Null(standing.Outcome);

        var calibration = new PerfRun(PerfTestSpec.SpeedCalibration(), Inputs(), warmUp);
        Drive(calibration, SyntheticRuns.Pull(Ms43x, 5, 3000, 3000, 1.5, kmhPer1000: 100 / 3.0, pedalPercent: 20));
        Assert.Empty(calibration.Captured);
        Assert.False(Assert.Single(calibration.Checks, c => c.Label == "Steady for 3 s").Met);
    }

    // A roll-on waits for a steady speed 5 to 15 km/h below its start: still climbing through that window at a
    // light pedal, it does not start when floored.
    [Fact]
    public void RollOnNeedsASteadySpeed()
    {
        // 44 to 55 km/h over 2 s at a light pedal, then floored.
        var climb = Frames(SyntheticRuns.Pull(Ms43x, 3, 1973, 2466, 2.0, pedalPercent: 20, holdSeconds: 0));
        var floored = Later(Frames(SyntheticRuns.Pull(Ms43x, 3, 2466, 4619, 3.0, holdSeconds: 0)), 2 + 1 / 30.0);
        var run = new PerfRun(PerfTestSpec.RollOn(3), Inputs(), warmUp);
        Drive(run, climb);
        Assert.False(Assert.Single(run.Checks, c => c.Label == "Steady between 45 and 55 km/h").Met);

        Drive(run, floored);
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Null(run.Outcome);
    }

    // Conditions are kept from when the ready checks were met, beside those at the start and the end.
    [Fact]
    public void ConditionsAreKeptFromTheArm()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, Splice(ThirdPull(oilC: 84), ThirdPull(oilC: 88), f => f.Pedal >= 90));

        var outcome = run.Outcome!;
        Assert.Equal(84, outcome.Armed!.OilC!.Value, 0.6);
        Assert.Equal(92, outcome.Armed.CoolantC!.Value, 0.6);
        Assert.Equal(88, outcome.Start.OilC!.Value, 0.6);
    }

    // Spec 4.4: full throttle is the pedal with the throttle body not far behind. A floored pedal over a nearly shut throttle
    // (limp-home, a torque cut), the malfunction light or DSC acting makes a pull doubtful, and the flag says why.
    [Fact]
    public void ALimitedPullIsFlagged()
    {
        var limp = Frames(SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, throttleShare: 0.2)).Select(x => x.Frame).ToArray();
        var throttle = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), limp, Inputs(), complete: true), "throttle");
        Assert.Equal(FlagSeverity.Suspect, throttle.Severity);
        Assert.Equal("Throttle body only 20° open with the pedal floored: the DME was limiting power.", throttle.Message);

        var frames = Frames(ThirdPull()).Select(x => x.Frame).ToArray();
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), frames, Inputs(), complete: true).Flags,
            f => f.Code is "throttle" or "eml" or "dsc");
        // Two samples behind are a throttle catching up, not a limit.
        var lag = frames.Select((f, i) => i is 100 or 101 ? f with { Throttle = 10 } : f).ToArray();
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), lag, Inputs(), complete: true).Flags, f => f.Code == "throttle");

        var eml = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), frames.Select(f => f with { MalfunctionLamp = true }).ToArray(), Inputs(),
            complete: true), "eml");
        Assert.Equal((FlagSeverity.Suspect, "The engine malfunction light was on: the DME may have limited power."), (eml.Severity, eml.Message));

        var asr = frames.Select(f => f with { TorqueIntervention = f.Rpm is >= 3000 and <= 3500 }).ToArray();
        var dsc = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), asr, Inputs(), complete: true), "dsc");
        Assert.Equal((FlagSeverity.Suspect, "DSC intervened on engine torque between 3000 and 3500 rpm: the times are traction-limited."),
            (dsc.Severity, dsc.Message));
    }

    // A GPS-checked calibration corrects the speedometer, so a standing start and an uncalibrated roll-on are timed on true
    // speed.
    [Fact]
    public void GpsCorrectionAppliesToTheSpeedometer()
    {
        // The speedometer reads 3 % low in the gear that was checked: true speed is 1.03 × VS.
        var gps = new AnalysisInputs(new CarValues(1480, "205/55 R16", false), new Dictionary<int, double> { [5] = 0.0333 * 1.03 },
            SyntheticRuns.FullPedal, new Dictionary<int, double> { [5] = 0.0333 });

        var run = new PerfRun(PerfTestSpec.StandingStart(), gps, warmUp);
        Drive(run, SyntheticRuns.Standing(Ms43x, 7.6));
        var standing = run.Outcome!.Figures;
        Assert.Equal(7.6 / 1.03, Fig(standing, "t-0-100").Value!.Value, 0.05);
        Assert.Equal(7.6 / 2 / 1.03, Fig(standing, "t-0-50").Value!.Value, 0.05);

        var rollOn = Frames(SyntheticRuns.Pull(Ms43x, 3, 2152, 4619, 4.0)).Select(x => x.Frame).ToArray();
        var vsPerSecond = (4619 - 2152) / 4.0 * 0.0223;
        Assert.Equal((100 - 60) / 1.03 / vsPerSecond, Fig(RunAnalysis.Analyse(PerfTestSpec.RollOn(3), rollOn, gps, complete: true), "time").Value!.Value,
            0.05);
        // Without a GPS check the speedometer is trusted.
        Assert.Equal(40 / vsPerSecond, Fig(RunAnalysis.Analyse(PerfTestSpec.RollOn(3), rollOn, Inputs(calibrated: false), complete: true), "time").Value!.Value,
            0.05);
    }

    // At 23 samples/s and 777 rpm/s the marks fall at different points between samples, so a time read from a sample's stamp
    // would be up to 43 ms off; interpolated crossings give the analytic times.
    [Fact]
    public void CrossingsAreInterpolatedAtAnyPhase()
    {
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(run, SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 4800 / 777.0, rate: 23));

        var figures = run.Outcome!.Figures;
        Assert.Equal(4400 / 777.0, Fig(figures, "time").Value!.Value, 0.005);
        Assert.Equal(2000 / 777.0, Fig(figures, "t-2000-4000").Value!.Value, 0.005);
        Assert.Equal(3000 / 777.0, Fig(figures, "t-3000-6000").Value!.Value, 0.005);
        Assert.Equal(2000 / 777.0, Fig(figures, "t-4000-6000").Value!.Value, 0.005);
    }

    // The power estimate against the hand-worked figure. The synthetic 3rd-gear pull: 800 rpm/s, 1480 kg, 205/55 R16
    // (r 0.3065 m), k 0.0223 km/h per rpm, 101.9 kPa and 25 °C. At 4000 rpm v = 24.78 m/s and a = 4.956 m/s²; m_eff = 1480 +
    // 4.5 / r² + 0.20 × (2π / 60 × 3.6 / k)² = 1585 kg; F = 7855 + 219 drag + 160 rolling = 8234 N, so P = 204.0 kW and
    // T = 487 Nm.
    [Fact]
    public void PowerMatchesTheWorkedExample()
    {
        var frames = Frames(ThirdPull()).Select(x => x.Frame).ToArray();
        static double At4000(RunFigures figures, string key)
        {
            var trace = Assert.Single(figures.Traces, t => t.Key == key);
            return trace.Y[trace.X.ToList().IndexOf(4000)];
        }

        var calibrated = RunAnalysis.Analyse(PerfTestSpec.Pull(3), frames, Inputs(), complete: true);
        Assert.Equal(204.0, At4000(calibrated, "power"), 2.04);
        Assert.Equal(487.1, At4000(calibrated, "torque"), 4.87);

        // Without a calibration the run's own VS / N gives the same, within VS's whole-km/h steps.
        var uncalibrated = RunAnalysis.Analyse(PerfTestSpec.Pull(3), frames, Inputs(calibrated: false), complete: true);
        Assert.Equal(At4000(calibrated, "power"), At4000(uncalibrated, "power"), 2.04);

        // 20 kPa less air pressure only lowers the drag, ½ ρ CdA v² at 24.78 m/s, times v.
        var thin = frames.Select(f => f with { PressureKpa = f.PressureKpa - 20 }).ToArray();
        var lower = RunAnalysis.Analyse(PerfTestSpec.Pull(3), thin, Inputs(), complete: true);
        var (pressure, intake) = (frames[^1].PressureKpa!.Value, frames[^1].IntakeC!.Value);
        var v = 0.0223 * 4000 / 3.6;
        var drag = 0.5 * (RunMath.AirDensity(pressure, intake) - RunMath.AirDensity(pressure - 20, intake)) * 0.60 * v * v * v / 1000;
        Assert.Equal(drag, At4000(calibrated, "power") - At4000(lower, "power"), 0.02);
    }

    // Pressing the clutch or the brake ends a pull as incomplete on the sample before the press; revs falling 100 rpm below
    // their peak end it at the peak.
    [Fact]
    public void ClutchBrakeAndARevDropEndThePull()
    {
        foreach (var clutch in new[] { true, false })
        {
            var run = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
            Drive(run, SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, clutchFromRpm: clutch ? 5000 : null, brakeFromRpm: clutch ? null : 5000));

            var outcome = run.Outcome!;
            Assert.False(outcome.Complete);
            Assert.Matches($"^Incomplete: {(clutch ? "clutch" : "brake")} pressed at 50\\d\\d rpm$", run.Status);
            Assert.Equal(run.Status + ".", Flag(outcome.Figures, "incomplete").Message);
            var last = Decode(outcome.Samples[^1]);
            Assert.True(last.Rpm < 5000);
            Assert.False(clutch ? last.Clutch : last.Brake);
            Assert.Equal(outcome.Samples.Count - 1, outcome.WindowEnd);
        }

        var frames = Frames(ThirdPull());
        var peak = Array.FindIndex(frames, x => x.Frame.Rpm >= 5000);
        var top = frames[peak].Frame;
        var falling = Enumerable.Range(1, 6)
            .Select(i => (top with { At = top.At + TimeSpan.FromSeconds(i / 30.0), Rpm = top.Rpm - 25 * i }, frames[peak].Payload));
        var drop = new PerfRun(PerfTestSpec.Pull(3), Inputs(), warmUp);
        Drive(drop, frames.Take(peak + 1).Concat(falling));
        Assert.Matches(@"^Incomplete: revs dropped from 50\d\d rpm$", drop.Status);
        Assert.Equal(top.Rpm, Decode(drop.Outcome!.Samples[^1]).Rpm);
        Assert.Equal(top.At, drop.Outcome.Samples[^1].At);
    }

    // The flags that guard the figures: slip takes three samples in a row with VS more than 3 % from
    // k × N at 30 km/h or more; a slow link, a link test under 15 samples/s and hot intake air are each named.
    [Fact]
    public void GuardFlagsFire()
    {
        var frames = Frames(ThirdPull()).Select(x => x.Frame).ToArray();
        PerfFrame[] Fast(int count) => frames
            .Select((f, i) => i >= 120 && i < 120 + count ? f with { SpeedKmh = Math.Round(1.05 * 0.0223 * f.Rpm!.Value) } : f)
            .ToArray();

        var slip = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), Fast(3), Inputs(), complete: true), "slip");
        Assert.Equal(FlagSeverity.Suspect, slip.Severity);
        Assert.Matches(@"^Speedometer and engine speed disagree by up to [456] %: wheelspin or clutch slip\.$", slip.Message);
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), Fast(2), Inputs(), complete: true).Flags, f => f.Code == "slip");
        Assert.DoesNotContain(RunAnalysis.Analyse(PerfTestSpec.Pull(3), Fast(3), Inputs(calibrated: false), complete: true).Flags,
            f => f.Code == "slip");

        var slow = Frames(SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, rate: 8)).Select(x => x.Frame).ToArray();
        var slowLink = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), slow, Inputs(), complete: true), "slow-link");
        Assert.Equal((FlagSeverity.Suspect, "Only 8.0 samples/s: knock events may be undercounted."), (slowLink.Severity, slowLink.Message));

        var link = new PerfRun(PerfTestSpec.LinkTest(), Inputs(), warmUp);
        Drive(link, SyntheticRuns.Idle(Ms43x, 31, rate: 12));
        var advice = Flag(link.Outcome!.Figures, "link-advice");
        Assert.Equal((FlagSeverity.Info, "Under 15 samples/s: setting the USB adapter's latency timer to 1 ms often helps."), (advice.Severity, advice.Message));
        Assert.DoesNotContain(link.Outcome.Figures.Flags, f => f.Code == "slow-link");

        var hot = Frames(SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, 6.0, intakeC: 50)).Select(x => x.Frame).ToArray();
        var intake = Flag(RunAnalysis.Analyse(PerfTestSpec.Pull(3), hot, Inputs(), complete: true), "hot-intake");
        Assert.Equal(FlagSeverity.Suspect, intake.Severity);
        Assert.Matches(@"^Intake air reached 5\d °C, above 45 °C\.$", intake.Message);
    }

    // The link test gives the interval spread from its shortest interval, and the retries the logger made.
    [Fact]
    public void LinkTestCountsRetries()
    {
        var frames = Frames(SyntheticRuns.Idle(Ms43x, 31))
            .Select((x, i) => (Frame: x.Frame with { Retries = i % 100 == 50 ? 1 : 0 }, x.Payload))
            .ToArray();
        var run = new PerfRun(PerfTestSpec.LinkTest(), Inputs(), warmUp);
        Drive(run, frames);

        var figures = run.Outcome!.Figures;
        var shortest = Fig(figures, "interval-min");
        Assert.Equal(("Shortest interval", "ms", 0), (shortest.Label, shortest.Unit, shortest.Decimals));
        Assert.Equal(1000 / 30.0, shortest.Value!.Value, 0.5);
        var retries = Fig(figures, "retries");
        Assert.Equal(("Retries", 0), (retries.Label, retries.Decimals));
        Assert.Equal(9, retries.Value);
        Assert.Equal(frames.Take(run.Outcome.Samples.Count).Select(x => x.Frame.Retries), run.Outcome.Samples.Select(s => s.Retries));

        // A source that reports no retries gives n/a, never zero.
        var unknown = RunAnalysis.Analyse(PerfTestSpec.LinkTest(), Frames(SyntheticRuns.Idle(Ms43x, 31)).Select(x => x.Frame).ToArray(), Inputs(), true);
        Assert.Null(Fig(unknown, "retries").Value);
    }
}
