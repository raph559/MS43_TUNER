using System.Globalization;

namespace MS43Logger.Core.Performance;

/// <summary>
/// Turns the frames of a run into its figures, traces and flags (performance spec 6, 7). A figure the run did not reach is
/// null, a figure outside the band is left out, and a channel the definition lacks gives null figures and no trace, never
/// zeros (spec 4.3b).
/// </summary>
public static class RunAnalysis
{
    // Retard rising this much between two samples is a knock event; a cylinder reaching it is flagged.
    private const double KnockStep = 0.75;
    private const double GapSeconds = 0.3;
    private const double SlowRate = 10;
    private const double AdviceRate = 15;
    private const double HotIntakeC = 45;
    private const double SlipShare = 0.03;
    private const double SlipFromKmh = 30;
    // VS lags N a little under hard acceleration; a run of samples, not one, is slip.
    private const int SlipSamples = 3;
    // Full throttle is the pedal at 90 % of its calibrated full value (spec 4.4). A throttle body under 30 % of the pedal's
    // degrees for three samples in a row is held back, not catching up: a limp-home throttle sits near 10°, while at low rpm
    // the DME may open it only part way under a floored pedal, so the margin is wide.
    private const double FlooredShare = 90;
    private const double ThrottleBehind = 0.3;
    private const int ThrottleSamples = 3;
    private const double BinWidth = 250;
    private const double Tolerance = 1e-9;
    private static readonly (int From, int To)[] RpmMarks = [(2000, 4000), (3000, 6000), (4000, 6000)];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Most severe first (invalid, cold, suspect, info), keeping the order within one severity.</summary>
    internal static IReadOnlyList<RunFlag> Ordered(IEnumerable<RunFlag> flags) => flags.OrderBy(flag => flag.Severity switch
    {
        FlagSeverity.Invalid => 0,
        FlagSeverity.Cold => 1,
        FlagSeverity.Suspect => 2,
        _ => 3,
    }).ToArray();

    internal static string Ordinal(int gear) => gear switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => gear.ToString(Invariant) + "th",
    };

    internal static string Whole(double value) => value.ToString("0", Invariant);

    /// <summary>
    /// True speed per km/h the speedometer reads: the GPS correction of the speed calibration (spec 6.5), the median over the
    /// gears whose corrected ratio differs from the speedometer's own; 1 when no gear was GPS-checked.
    /// </summary>
    internal static double SpeedFactor(AnalysisInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.SpeedometerKmhPerRpm is not { Count: > 0 } speedometer)
        {
            return 1;
        }

        var factor = RunMath.Median(speedometer
            .Where(pair => pair.Value > 0 && double.IsFinite(pair.Value) && Calibrated(pair.Key, inputs) is not null)
            .Select(pair => Calibrated(pair.Key, inputs)!.Value / pair.Value)
            .Where(ratio => Math.Abs(ratio - 1) > Tolerance));
        return factor > 0 && double.IsFinite(factor) ? factor : 1;
    }

    /// <summary>What a roll-on times (spec 4.5, 6.3): speed from rpm with the gear's calibration, else VS with any GPS correction.</summary>
    internal static Func<PerfFrame, double?> RollOnSpeed(int? gear, AnalysisInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var factor = SpeedFactor(inputs);
        return Calibrated(gear, inputs) is { } k ? frame => k * frame.Rpm : frame => frame.SpeedKmh * factor;
    }

    private static RunFigures RollOn(PerfTestSpec spec, IReadOnlyList<PerfFrame> frames, AnalysisInputs inputs, bool complete)
    {
        var start = spec.StartKmh ?? double.NaN;
        var end = spec.EndKmh ?? double.NaN;
        var flags = new List<RunFlag>();
        // Speed from rpm with the calibration, else VS itself with any GPS correction: the measure the run started and ended
        // on (spec 6.3).
        var ratio = Calibrated(spec.Gear, inputs);
        if (ratio is null)
        {
            flags.Add(Uncalibrated(spec.Gear, "speed read from the speedometer"));
        }

        var speed = RollOnSpeed(spec.Gear, inputs);
        var t = Times(frames);
        var v = Values(frames, speed);
        var (lead, first) = LastCrossing(t, v, start);
        var tA = t[lead..];
        var vA = v[lead..];
        double? Cross(double level) => RunMath.CrossingTime(tA, vA, level);
        double? Between(double a, double b) => Cross(a) is { } x && Cross(b) is { } y ? y - x : null;

        var band = Slice(frames, first);
        var knock = Knock(band, lead < first ? frames[lead] : null);
        var figures = new List<Figure> { Fig("time", $"{Whole(start)} to {Whole(end)} km/h", Between(start, end), "s", 2) };
        var splits = double.IsFinite(end - start) ? (int)Math.Floor((end - start) / 10 + Tolerance) : 0;
        for (var i = 0; i < splits; i++)
        {
            var a = start + 10 * i;
            figures.Add(Fig($"split-{Whole(a)}-{Whole(a + 10)}", $"{Whole(a)} to {Whole(a + 10)} km/h", Between(a, a + 10), "s", 2));
        }

        figures.Add(Fig("knock-max", "Most knock retard", knock.Max, "°CRK", 1));
        figures.Add(Fig("knock-cyl", "Knock on cylinder", knock.Cylinder, "", 0));

        var t0 = Cross(start);
        var traces = new List<Trace>();
        AddTimeTrace(traces, "speed-time", "Speed", "km/h", band, speed, t0, t0 is null ? null : start);
        AddTimeTrace(traces, "rpm-time", "Engine speed", "rpm", band, f => f.Rpm, t0, null);

        if (ratio is not null && Speedometer(spec.Gear, inputs) is { } k && Slip(band, _ => k) is { } slip)
        {
            flags.Add(slip);
        }

        flags.AddRange(knock.Flags);
        flags.AddRange(Limits(band, inputs, speed, "km/h"));
        flags.AddRange(Common(frames, lead, complete, speed, "km/h"));
        if (HotIntake(band) is { } hot)
        {
            flags.Add(hot);
        }

        return new RunFigures(figures, traces, Ordered(flags), Value(figures, "time") is { } s ? Seconds(s) : "n/a");
    }

    private static RunFigures Standing(IReadOnlyList<PerfFrame> frames, AnalysisInputs inputs, bool complete)
    {
        // True speed: VS with any GPS correction of the speed calibration (spec 6.5).
        var factor = SpeedFactor(inputs);
        double? Speed(PerfFrame f) => f.SpeedKmh * factor;
        var t = Times(frames);
        var v = Values(frames, Speed);
        // The launch follows the last stop; anything kept before it is not part of this start.
        var stop = Array.FindLastIndex(v, speed => speed <= 0);
        var from = Math.Max(stop, 0);
        var tA = t[from..];
        var vA = v[from..];
        // Time zero is back-extrapolated over 3 to 20 km/h (spec 6.4).
        var t0 = RunMath.BackExtrapolatedStart(tA, vA);
        double? Cross(double level) => RunMath.CrossingTime(tA, vA, level);
        double? FromZero(double level) => t0 is { } zero && Cross(level) is { } c ? c - zero : null;
        double? Between(double a, double b) => Cross(a) is { } x && Cross(b) is { } y ? y - x : null;

        var figures = new List<Figure>();
        void Reached(string key, string label, double? value)
        {
            if (value is { } seconds && double.IsFinite(seconds))
            {
                figures.Add(Fig(key, label, seconds, "s", 2));
            }
        }

        Reached("t-0-100", "0 to 100 km/h", FromZero(100));
        Reached("t-0-200", "0 to 200 km/h", FromZero(200));
        Reached("t-100-200", "100 to 200 km/h", Between(100, 200));
        Reached("t-0-50", "0 to 50 km/h", FromZero(50));
        Reached("t-50-100", "50 to 100 km/h", Between(50, 100));
        figures.Add(Fig("launch-rpm", "Launch", frames.Count > 0 ? frames[from].Rpm : null, "rpm", 0));

        // Each clutch press after time zero is a shift, timed from press to release (spec 6.4).
        var origin = t0 ?? (tA.Length > 0 ? tA[0] : double.NaN);
        var shift = 0;
        bool? previous = null;
        double? pressed = null;
        for (var i = from; i < frames.Count; i++)
        {
            if (frames[i].Clutch is not { } clutch)
            {
                continue;
            }

            if (clutch && previous == false && t[i] > origin)
            {
                pressed = t[i];
            }
            else if (!clutch && pressed is { } at)
            {
                shift++;
                figures.Add(Fig($"shift-{shift}", $"Shift {shift}", t[i] - at, "s", 2));
                pressed = null;
            }

            previous = clutch;
        }

        var launched = Slice(frames, from);
        var traces = new List<Trace>();
        AddTimeTrace(traces, "speed-time", "Speed", "km/h", launched, Speed, t0 ?? origin, null);
        AddTimeTrace(traces, "rpm-time", "Engine speed", "rpm", launched, f => f.Rpm, t0 ?? origin, null);

        var flags = new List<RunFlag>();
        // Wheelspin: VS against the calibrated speed of the gear the DME reports, outside shifts.
        if (Slip(launched, f => f.Clutch != true && Calibrated(f.Gear, inputs) is not null ? Speedometer(f.Gear, inputs) : null) is { } slip)
        {
            flags.Add(slip);
        }

        if (Dsc(launched, Speed, "km/h") is { } dsc)
        {
            flags.Add(dsc);
        }

        var knock = Knock(launched.Skip(1).ToArray(), launched.FirstOrDefault());
        flags.AddRange(knock.Flags);
        flags.AddRange(Common(frames, from, complete, Speed, "km/h"));
        if (HotIntake(launched) is { } hot)
        {
            flags.Add(hot);
        }

        var headline = Value(figures, "t-0-100") is { } to100 ? $"{to100.ToString("0.0", Invariant)} s to 100"
            : Value(figures, "t-0-50") is { } to50 ? $"{to50.ToString("0.0", Invariant)} s to 50"
            : "n/a";
        return new RunFigures(figures, traces, Ordered(flags), headline);
    }

    private static RunFigures Link(IReadOnlyList<PerfFrame> frames, bool complete)
    {
        var t = Times(frames).Where(double.IsFinite).ToArray();
        // A stamp that steps back is no interval; samples at one instant are intervals of zero.
        var intervals = Enumerable.Range(1, Math.Max(t.Length - 1, 0)).Select(i => t[i] - t[i - 1]).Where(d => d >= 0).Order().ToArray();
        double? median = null, shortest = null, p95 = null, longest = null, rate = null;
        if (intervals.Length > 0)
        {
            median = RunMath.Median(intervals);
            shortest = intervals[0];
            p95 = intervals[Math.Clamp((int)Math.Ceiling(0.95 * intervals.Length) - 1, 0, intervals.Length - 1)];
            longest = intervals[^1];
            var span = t[^1] - t[0];
            // Samples per second from the median interval (spec 6.6); the mean when bursts make the median zero.
            rate = median > 0 ? 1 / median : span > 0 ? (t.Length - 1) / span : null;
        }

        var figures = new List<Figure>
        {
            Fig("rate", "Samples per second", rate, "samples/s", 1),
            Fig("interval-median", "Median interval", median * 1000, "ms", 0),
            Fig("interval-min", "Shortest interval", shortest * 1000, "ms", 0),
            Fig("interval-p95", "95th percentile interval", p95 * 1000, "ms", 0),
            Fig("interval-max", "Longest interval", longest * 1000, "ms", 0),
            Fig("gaps", "Gaps over 300 ms", intervals.Length > 0 ? intervals.Count(d => d > GapSeconds) : null, "", 0),
            // Requests sent again after a timeout (spec 6.6); unknown when the source does not count them.
            Fig("retries", "Retries", frames.Any(f => f.Retries is not null) ? frames.Sum(f => f.Retries ?? 0) : null, "", 0),
        };

        var flags = new List<RunFlag>();
        if (!complete)
        {
            var seconds = t.Length > 1 ? t[^1] - t[0] : 0;
            flags.Add(new RunFlag("incomplete", FlagSeverity.Invalid, $"Incomplete: ended after {seconds.ToString("0.0", Invariant)} s."));
        }

        flags.AddRange(LinkFlags(t));
        if (rate < AdviceRate)
        {
            flags.Add(new RunFlag("link-advice", FlagSeverity.Info,
                "Under 15 samples/s: setting the USB adapter's latency timer to 1 ms often helps."));
        }

        return new RunFigures(figures, [], Ordered(flags), rate is { } r ? $"{r.ToString("0.0", Invariant)} samples/s" : "n/a");
    }

    /// <summary>
    /// The incomplete flag, then gaps and a slow link over the frames from <paramref name="from"/>: the timed part of the run
    /// and the sample before it.
    /// </summary>
    private static IEnumerable<RunFlag> Common(IReadOnlyList<PerfFrame> frames, int from, bool complete, Func<PerfFrame, double?> measure, string unit)
    {
        if (!complete)
        {
            var last = frames.Skip(from).Select(measure).LastOrDefault(value => value is { } x && double.IsFinite(x));
            var at = last is { } value ? $"{Whole(value)} {unit}" : "n/a";
            yield return new RunFlag("incomplete", FlagSeverity.Invalid, $"Incomplete: ended at {at}.");
        }

        foreach (var flag in LinkFlags(Times(frames)[from..]))
        {
            yield return flag;
        }
    }

    // A gap over 300 ms, or under 10 samples/s, can hide a knock event between samples (spec 11); samples at one instant or
    // out of order (retry bursts, review focus 4) make the timing itself doubtful.
    private static IEnumerable<RunFlag> LinkFlags(double[] t)
    {
        var times = t.Where(double.IsFinite).ToArray();
        if (times.Length < 2)
        {
            yield break;
        }

        var largest = Enumerable.Range(1, times.Length - 1).Max(i => times[i] - times[i - 1]);
        if (largest > GapSeconds)
        {
            yield return new RunFlag("gap", FlagSeverity.Suspect, $"A {Whole(largest * 1000)} ms gap in the samples.");
        }

        var uneven = Enumerable.Range(1, times.Length - 1).Count(i => times[i] <= times[i - 1]);
        if (uneven > 0)
        {
            yield return new RunFlag("timing", FlagSeverity.Suspect, uneven == 1
                ? "1 sample shares a time stamp or steps back: the timing is uneven."
                : $"{uneven.ToString(Invariant)} samples share a time stamp or step back: the timing is uneven.");
        }

        var span = times[^1] - times[0];
        if (span > 0 && (times.Length - 1) / span is var rate && rate < SlowRate)
        {
            yield return new RunFlag("slow-link", FlagSeverity.Suspect,
                $"Only {rate.ToString("0.0", Invariant)} samples/s: knock events may be undercounted.");
        }
    }

    // km/h per rpm in this gear from the speed calibration (spec 6.5); null without one.
    private static double? Calibrated(int? gear, AnalysisInputs inputs) =>
        gear is { } g && inputs.KmhPerRpm.TryGetValue(g, out var ratio) && ratio > 0 && double.IsFinite(ratio) ? ratio : null;

    // What the speedometer itself reads per rpm in this gear, the one VS is compared with: the calibration as captured before a
    // GPS correction, else the calibration itself.
    private static double? Speedometer(int? gear, AnalysisInputs inputs) =>
        gear is { } g && inputs.SpeedometerKmhPerRpm is { } speedometer && speedometer.TryGetValue(g, out var ratio) && ratio > 0 && double.IsFinite(ratio)
            ? ratio
            : Calibrated(gear, inputs);

    // Without a calibration, the median VS/N of the run in this gear at 30 km/h or more (spec 4.5); null when it has none.
    private static double? RunRatio(int? gear, IReadOnlyList<PerfFrame> frames)
    {
        var median = RunMath.Median(frames
            .Where(f => f.Gear == gear && f.SpeedKmh >= SlipFromKmh && f.Rpm > 0)
            .Select(f => f.SpeedKmh!.Value / f.Rpm!.Value));
        return median > 0 && double.IsFinite(median) ? median : null;
    }

    private static RunFlag Uncalibrated(int? gear, string consequence) =>
        new("uncalibrated", FlagSeverity.Info, $"No speed calibration for {(gear is { } g ? Ordinal(g) : "this")} gear: {consequence}.");

    // Slip, wheelspin or a slipping clutch: VS more than 3 % away from the speedometer's calibrated ratio × N at 30 km/h or more (spec 4.5).
    private static RunFlag? Slip(IReadOnlyList<PerfFrame> frames, Func<PerfFrame, double?> ratio)
    {
        var run = 0;
        var worst = 0.0;
        var found = false;
        foreach (var frame in frames)
        {
            if (frame.SpeedKmh is not { } vs || frame.Rpm is not { } n || n <= 0 || ratio(frame) is not { } k || vs < SlipFromKmh)
            {
                run = 0;
                continue;
            }

            var expected = k * n;
            var share = Math.Abs(vs - expected) / expected;
            if (!(share > SlipShare))
            {
                run = 0;
                continue;
            }

            run++;
            worst = Math.Max(worst, share);
            found |= run >= SlipSamples;
        }

        return found
            ? new RunFlag("slip", FlagSeverity.Suspect,
                $"Speedometer and engine speed disagree by up to {Whole(worst * 100)} %: wheelspin or clutch slip.")
            : null;
    }

    // DSC acting on the engine torque, with the speeds (or the engine speeds, in 50 rpm steps as for knock) it acted at (spec 6.4).
    private static RunFlag? Dsc(IReadOnlyList<PerfFrame> frames, Func<PerfFrame, double?> measure, string unit)
    {
        var acting = frames.Where(f => f.TorqueIntervention == true).ToArray();
        if (acting.Length == 0)
        {
            return null;
        }

        var values = acting.Select(f => measure(f) ?? double.NaN).Where(double.IsFinite).ToArray();
        var range = "";
        if (values.Length > 0)
        {
            var (low, high) = unit == "rpm"
                ? (Math.Floor(values.Min() / 50) * 50, Math.Ceiling(values.Max() / 50) * 50)
                : (values.Min(), values.Max());
            range = $" between {Whole(low)} and {Whole(high)} {unit}";
        }

        return new RunFlag("dsc", FlagSeverity.Suspect, $"DSC intervened on engine torque{range}: the times are traction-limited.");
    }

    /// <summary>
    /// What held the engine back at full throttle (spec 4.4): the throttle body far behind a floored pedal (limp-home or a
    /// torque cut), the malfunction light, DSC on the engine torque.
    /// </summary>
    private static IEnumerable<RunFlag> Limits(IReadOnlyList<PerfFrame> frames, AnalysisInputs inputs, Func<PerfFrame, double?> measure, string unit)
    {
        if (Throttle(frames, inputs) is { } throttle)
        {
            yield return throttle;
        }

        if (frames.Any(f => f.MalfunctionLamp == true))
        {
            yield return new RunFlag("eml", FlagSeverity.Suspect, "The engine malfunction light was on: the DME may have limited power.");
        }

        if (Dsc(frames, measure, unit) is { } dsc)
        {
            yield return dsc;
        }
    }

    // Three samples in a row with the pedal floored and the throttle body far behind it; null without a pedal calibration or a
    // throttle reading.
    private static RunFlag? Throttle(IReadOnlyList<PerfFrame> frames, AnalysisInputs inputs)
    {
        if (!(inputs.FullPedal > 0) || !double.IsFinite(inputs.FullPedal))
        {
            return null;
        }

        var run = 0;
        var found = false;
        var lowest = double.PositiveInfinity;
        foreach (var frame in frames)
        {
            if (frame.Pedal is not { } pedal || frame.Throttle is not { } throttle || 100 * pedal / inputs.FullPedal < FlooredShare
                || !(throttle < ThrottleBehind * pedal))
            {
                run = 0;
                continue;
            }

            run++;
            lowest = Math.Min(lowest, throttle);
            found |= run >= ThrottleSamples;
        }

        return found
            ? new RunFlag("throttle", FlagSeverity.Suspect, $"Throttle body only {Whole(lowest)}° open with the pedal floored: the DME was limiting power.")
            : null;
    }

    // The cylinder of the worst retard in each rpm bin where a cylinder pulled timing (spec 6.1: worst knock and which cylinder).
    private static IReadOnlyList<(double Rpm, double Value)> WorstCylinder(double[] rpm, KnockSummary knock, double from, double to)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || to < from)
        {
            return [];
        }

        var worst = new SortedDictionary<int, (double Retard, int Cylinder)>();
        for (var i = 0; i < rpm.Length; i++)
        {
            if (knock.Worst[i] is var retard && retard > 0 && RunMath.BinOf(rpm[i], from, to, BinWidth) is { } bin
                && (!worst.TryGetValue(bin, out var top) || retard > top.Retard))
            {
                worst[bin] = (retard, knock.WorstCylinder[i]);
            }
        }

        return worst.Select(pair => (from + pair.Key * BinWidth, (double)pair.Value.Cylinder)).ToArray();
    }

    private static RunFlag? HotIntake(IReadOnlyList<PerfFrame> frames)
    {
        var hottest = frames.Select(f => f.IntakeC ?? double.NaN).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
        return hottest > HotIntakeC
            ? new RunFlag("hot-intake", FlagSeverity.Suspect, $"Intake air reached {Whole(hottest)} °C, above 45 °C.")
            : null;
    }

    /// <summary>
    /// Knock retard over the frames of the timed part: the worst per cylinder and over all, the cylinder of the worst, the
    /// events (retard rising 0.75° or more from one sample to the next, counted from <paramref name="before"/>), a flag per
    /// cylinder that reached 0.75°, and the worst over the cylinders per frame with its cylinder (the first of a tie).
    /// </summary>
    private static KnockSummary Knock(IReadOnlyList<PerfFrame> frames, PerfFrame? before)
    {
        var perCylinder = new double?[6];
        var worst = new double[frames.Count];
        var worstCylinder = new int[frames.Count];
        var events = 0;
        var previous = before;
        for (var i = 0; i < frames.Count; i++)
        {
            var retard = frames[i].KnockRetard;
            worst[i] = double.NaN;
            for (var c = 0; c < Math.Min(retard.Count, 6); c++)
            {
                if (retard[c] is not { } r || !double.IsFinite(r))
                {
                    continue;
                }

                perCylinder[c] = Math.Max(perCylinder[c] ?? r, r);
                if (double.IsNaN(worst[i]) || r > worst[i])
                {
                    worst[i] = r;
                    worstCylinder[i] = c + 1;
                }

                if (previous is not null && c < previous.KnockRetard.Count && previous.KnockRetard[c] is { } p && r - p >= KnockStep - Tolerance)
                {
                    events++;
                }
            }

            previous = frames[i];
        }

        if (perCylinder.All(value => value is null))
        {
            return new KnockSummary(false, null, null, perCylinder, 0, [], worst, worstCylinder);
        }

        var max = perCylinder.Max()!.Value;
        int? cylinder = max > 0 ? Array.IndexOf(perCylinder, max) + 1 : null;
        var flags = new List<RunFlag>();
        for (var c = 0; c < 6; c++)
        {
            if (!(perCylinder[c] >= KnockStep - Tolerance))
            {
                continue;
            }

            var knocked = frames
                .Where(f => c < f.KnockRetard.Count && f.KnockRetard[c] > 0 && f.Rpm is { } n && double.IsFinite(n))
                .Select(f => f.Rpm!.Value)
                .ToArray();
            var range = knocked.Length > 0
                ? $" between {Whole(Math.Floor(knocked.Min() / 50) * 50)} and {Whole(Math.Ceiling(knocked.Max() / 50) * 50)} rpm"
                : "";
            flags.Add(new RunFlag("knock", FlagSeverity.Suspect,
                $"Knock retard on cylinder {c + 1} reached {perCylinder[c]!.Value.ToString("0.0", Invariant)} °CRK{range}."));
        }

        return new KnockSummary(true, max, cylinder, perCylinder, events, flags, worst, worstCylinder);
    }

    private sealed record KnockSummary(bool Logged, double? Max, int? Cylinder, double?[] PerCylinder, int Events,
        IReadOnlyList<RunFlag> Flags, double[] Worst, int[] WorstCylinder);

    /// <summary>
    /// The last upward crossing of level: the last point below it before the crossing (lead) and the first at or above it.
    /// Without a crossing, both are the first point at or above the level (or the end), so nothing is timed from it.
    /// </summary>
    private static (int Lead, int First) LastCrossing(double[] t, double[] v, double level)
    {
        int lead = -1, first = -1, previous = -1;
        for (var i = 0; i < v.Length; i++)
        {
            if (!double.IsFinite(t[i]) || !double.IsFinite(v[i]))
            {
                continue;
            }

            if (previous >= 0 && v[previous] < level && v[i] >= level)
            {
                lead = previous;
                first = i;
            }

            previous = i;
        }

        if (first >= 0)
        {
            return (lead, first);
        }

        var above = Array.FindIndex(v, value => value >= level);
        var at = above >= 0 ? above : v.Length;
        return (at, at);
    }

    // Air density from the run's median pressure and intake air; standard air when either is missing.
    private static double Density(IReadOnlyList<PerfFrame> frames)
    {
        var density = RunMath.AirDensity(RunMath.Median(frames.Select(f => f.PressureKpa ?? double.NaN)),
            RunMath.Median(frames.Select(f => f.IntakeC ?? double.NaN)));
        return double.IsFinite(density) && density > 0 ? density : RunMath.AirDensity(101.325, 15);
    }

    private static (double? Value, double? Rpm) Peak(IReadOnlyList<(double Rpm, double Value)> bins)
    {
        if (bins.Count == 0)
        {
            return (null, null);
        }

        var top = bins.MaxBy(bin => bin.Value);
        return (top.Value, top.Rpm);
    }

    private static double? IgnitionOf(PerfFrame frame)
    {
        if (frame.IgnitionAvg is { } average)
        {
            return average;
        }

        var cylinders = frame.Ignition.Where(value => value is { } x && double.IsFinite(x)).Select(value => value!.Value).ToArray();
        return cylinders.Length > 0 ? cylinders.Average() : null;
    }

    private static void AddTrace(List<Trace> traces, string key, string label, string unit, IReadOnlyList<(double Rpm, double Value)> bins)
    {
        if (bins.Count > 0)
        {
            traces.Add(new Trace(key, label, "rpm", unit, bins.Select(b => b.Rpm).ToArray(), bins.Select(b => b.Value).ToArray()));
        }
    }

    // A trace against seconds from time zero, opened by the crossing point itself when there is one.
    private static void AddTimeTrace(List<Trace> traces, string key, string label, string unit, IReadOnlyList<PerfFrame> frames,
        Func<PerfFrame, double?> value, double? t0, double? atZero)
    {
        var x = new List<double>();
        var y = new List<double>();
        var zero = t0 ?? (frames.Count > 0 ? frames[0].At.TotalSeconds : 0);
        if (t0 is not null && atZero is { } level)
        {
            x.Add(0);
            y.Add(level);
        }

        foreach (var frame in frames)
        {
            var seconds = frame.At.TotalSeconds - zero;
            if (value(frame) is { } v && double.IsFinite(v) && double.IsFinite(seconds))
            {
                x.Add(seconds);
                y.Add(v);
            }
        }

        if (x.Count > 0)
        {
            traces.Add(new Trace(key, label, "s", unit, x, y));
        }
    }

    private static Figure Fig(string key, string label, double? value, string unit, int decimals) =>
        new(key, label, value is { } v && double.IsFinite(v) ? v : null, unit, decimals);

    private static double? Value(IEnumerable<Figure> figures, string key) => figures.FirstOrDefault(f => f.Key == key)?.Value;

    private static string Seconds(double seconds) => seconds.ToString("0.00", Invariant) + " s";

    private static string Headline(params string?[] parts)
    {
        var known = parts.Where(part => part is not null).ToArray();
        return known.Length > 0 ? string.Join(", ", known) : "n/a";
    }

    private static double[] Times(IReadOnlyList<PerfFrame> frames) => frames.Select(f => f.At.TotalSeconds).ToArray();

    private static double[] Values(IReadOnlyList<PerfFrame> frames, Func<PerfFrame, double?> pick) =>
        frames.Select(f => pick(f) ?? double.NaN).ToArray();

    private static PerfFrame[] Slice(IReadOnlyList<PerfFrame> frames, int from) => frames.Skip(from).ToArray();

    private static double[] Nan(int count)
    {
        var values = new double[count];
        Array.Fill(values, double.NaN);
        return values;
    }
}
