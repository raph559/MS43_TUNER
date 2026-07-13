using System.Globalization;

namespace MS43Logger.Core.Performance;

/// <summary>
/// A sample kept with a run: its time on the run clock and its raw payload, so any channel or bit can be decoded again,
/// and the retries its poll needed when the source said.
/// </summary>
public sealed record RecordedSample(TimeSpan At, byte[] Payload, int? Retries = null);

/// <summary>
/// A finished run, complete or incomplete. <see cref="Samples"/> run from 2 s before the window (when the run had them) to its
/// end; <see cref="WindowStart"/> is the first sample in the band, the start crossing lying between it and the sample before,
/// and <see cref="WindowEnd"/> is the last sample. <see cref="Figures"/> is <see cref="RunAnalysis.Analyse"/> of these samples
/// with the flags only the run could see: why it ended, the warm-up state while it ran, a late full throttle. <see cref="Armed"/>
/// are the conditions when the ready checks were met (at arm, start and end).
/// </summary>
public sealed record RunOutcome(PerfTestSpec Spec, bool Complete, RunFigures Figures, Conditions Start, Conditions End,
    IReadOnlyList<RecordedSample> Samples, int WindowStart, int WindowEnd, Conditions? Armed = null);

/// <summary>
/// One test from arming to its result, fed the frames Live already receives: it never asks for a sample
/// of its own. A pull, knock check or roll-on waits for its ready checks, starts on full throttle, times its window from the
/// interpolated start crossing and ends complete at the end crossing, or incomplete when the pedal lifts, the clutch or brake
/// is pressed, the gear changes, the revs drop or the link is lost. Ending before the band was reached is a false start:
/// nothing was timed, so the run waits again.
/// </summary>
public sealed class PerfRun
{
    // Pedal shares are of the calibrated full-pedal value.
    private const double FullThrottle = 90;
    private const double Released = 30;
    private const double LightPedal = 60;
    private const double RpmDrop = 100;
    private const double SpeedDrop = 5;
    private const double LateThrottleKmh = 3;
    private const double CalibrationKmh = 50;
    private const double SteadyKmh = 1;
    private const double SteadyRpm = 50;
    // A roll-on starts from a steady speed: within 2 km/h for 1 s.
    private const double RollOnSteadyKmh = 2;
    private const double OilWarmC = 80;
    private const double LinkSeconds = 30;
    private const double FitAboveKmh = 20;
    // Settings keep a full pedal of 10 to 130; a lower peak is a pedal that was not pressed.
    private const double MinFullPedal = 10;
    private const double PedalBand = 0.01;
    private const string WaitingText = "Waiting for the ready checks…";
    private static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StoppedFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReleasedFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SteadyFor = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RollOnSteadyFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PedalHold = TimeSpan.FromSeconds(2);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly AnalysisInputs _inputs;
    private readonly WarmUpMonitor _warmUp;
    private readonly bool _pedalCalibrated;
    private readonly Func<PerfFrame, double?> _rollOnSpeed;
    private readonly double _speedFactor;
    // The frames kept: the last 2 s while waiting, then the run; payloads beside them.
    private readonly List<PerfFrame> _frames = [];
    private readonly List<byte[]> _payloads = [];
    private readonly Dictionary<int, double> _captured = [];
    private IReadOnlyList<ReadyCheck> _checks;
    private PerfFrame? _latest;
    private IReadOnlyList<(double T, double Rpm)>? _live;

    // Pull-type tests and the standing start; indices are into _frames.
    private bool _armed;
    private int _windowStart = -1;
    private double _t0;
    private double? _tEnd;
    private bool _t0Fitted;
    private int _lowFrom = -1;
    private int _releasedFrom = -1;
    private double? _peak;
    private int _peakAt = -1;
    private double? _triggerMeasure;
    private TimeSpan? _stoppedSince;
    private (double Kmh, TimeSpan Since)? _rollOnSteady;
    private bool _tooLate;
    private Conditions? _startConditions;
    private Conditions? _armedConditions;
    private bool _sawCold;
    private bool _sawOilCold;
    private bool _sawOilUnreported;
    private double? _lowestOil;

    // Speed calibration: the steady stretch and the ratios of its last 3 s.
    private (int Gear, double Kmh, double Rpm, TimeSpan Since)? _steady;
    private readonly Queue<(TimeSpan At, double Ratio)> _ratios = new();

    // Pedal step.
    private TimeSpan? _holdSince;
    private double _holdPeak;

    /// <param name="spec">The test and its options.</param>
    /// <param name="inputs">Car values, speed calibration and full-pedal value, as they are when the test is armed.</param>
    /// <param name="warmUp">
    /// The monitor Live keeps. The caller pushes each frame into it before <see cref="Push"/>; the run only reads it, for the
    /// "Engine warm" check and the cold and oil flags.
    /// </param>
    public PerfRun(PerfTestSpec spec, AnalysisInputs inputs, WarmUpMonitor warmUp)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(warmUp);
        Spec = spec;
        _inputs = inputs;
        _warmUp = warmUp;
        _pedalCalibrated = double.IsFinite(inputs.FullPedal) && inputs.FullPedal > 0;
        _rollOnSpeed = RunAnalysis.RollOnSpeed(spec.Gear, inputs);
        _speedFactor = RunAnalysis.SpeedFactor(inputs);
        (Stage, Status) = spec.Kind switch
        {
            PerfTestKind.SpeedCalibration => (RunStage.Running, "Hold a steady speed above 50 km/h…"),
            PerfTestKind.LinkTest => (RunStage.Running, "Running"),
            PerfTestKind.Pedal => (RunStage.Running, "Press the pedal fully and hold it…"),
            _ => (RunStage.Waiting, WaitingText),
        };
        _checks = spec.Kind switch
        {
            PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn => PullChecks(null, null, false, false, false, false),
            PerfTestKind.StandingStart => StandingChecks(null, false, false),
            PerfTestKind.SpeedCalibration => CalibrationChecks(null, false, false, false, TimeSpan.Zero),
            PerfTestKind.Pedal => PedalChecks(null, false, false, TimeSpan.Zero),
            _ => [],
        };
    }

    public PerfTestSpec Spec { get; }

    public RunStage Stage { get; private set; }

    /// <summary>Each condition of the test with its live reading; empty for the link test.</summary>
    public IReadOnlyList<ReadyCheck> Checks => _checks;

    /// <summary>"Waiting for full throttle…", "Running", "Done", "Incomplete: pedal lifted at 5210 rpm" and the like.</summary>
    public string Status { get; private set; }

    /// <summary>0 to 1 of the band, the speed range, the link test's 30 s or the hold a calibration or pedal step needs.</summary>
    public double Progress { get; private set; }

    /// <summary>Seconds since the start crossing (since time zero of a standing start, since the first sample of a link test).</summary>
    public double Elapsed { get; private set; }

    public double? Rpm => _latest?.Rpm;

    /// <summary>The latest knock retard per cylinder, positive degrees; empty when the definition logs none.</summary>
    public IReadOnlyList<double?> Knock => _latest?.KnockRetard ?? [];

    /// <summary>Rpm against seconds since the start crossing, for the running chart; empty before the window.</summary>
    public IReadOnlyList<(double T, double Rpm)> Live => _live ??= BuildLive();

    /// <summary>Speed calibration: km/h per rpm by gear captured so far; a later capture of a gear replaces it.</summary>
    public IReadOnlyDictionary<int, double> Captured => _captured;

    /// <summary>Pedal step: the highest pedal reading seen, in the definition's unit.</summary>
    public double? PedalReading { get; private set; }

    /// <summary>Set when a pull, knock check, roll-on, standing start or link test finishes; calibration and the pedal step have none.</summary>
    public RunOutcome? Outcome { get; private set; }

    public event EventHandler? Changed;

    public void Push(PerfFrame frame, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Stage is RunStage.Finished or RunStage.Cancelled)
        {
            return;
        }

        _latest = frame;
        _live = null;
        switch (Spec.Kind)
        {
            case PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn:
                PushPullType(frame, payload);
                break;
            case PerfTestKind.StandingStart:
                PushStanding(frame, payload);
                break;
            case PerfTestKind.SpeedCalibration:
                PushCalibration(frame);
                break;
            case PerfTestKind.LinkTest:
                PushLink(frame, payload);
                break;
            case PerfTestKind.Pedal:
                PushPedal(frame);
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops a waiting or running test without an outcome; a finished run keeps its outcome.</summary>
    public void Cancel()
    {
        if (Stage is RunStage.Finished or RunStage.Cancelled)
        {
            return;
        }

        Stage = RunStage.Cancelled;
        Status = "Cancelled";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The link to the car is gone: a run with a window ends incomplete with what it measured and the flag
    /// "link-lost", except a standing start that had reached 100 km/h, which is complete with what it reached and
    /// says so in a note; anything else, waiting included, is cancelled.
    /// </summary>
    public void LinkLost()
    {
        if (Stage is RunStage.Finished or RunStage.Cancelled)
        {
            return;
        }

        if (Stage == RunStage.Running && _windowStart >= 0 && Spec.Kind is not (PerfTestKind.SpeedCalibration or PerfTestKind.Pedal))
        {
            var end = LastGood(_frames.Count - 1);
            if (Spec.Kind == PerfTestKind.StandingStart && _releasedFrom >= 0)
            {
                end = Math.Min(end, _releasedFrom - 1);
            }

            if (end >= _windowStart && (Spec.Kind != PerfTestKind.LinkTest || end >= 1))
            {
                var where = Spec.Kind == PerfTestKind.LinkTest ? $"after {Elapsed.ToString("0.0", Invariant)} s" : $"at {MeasureText(_frames[end])}";
                Finish(Spec.Kind == PerfTestKind.StandingStart && ReachedHundred(end), $"link lost {where}", end, linkLost: true);
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        Stage = RunStage.Cancelled;
        Status = "Link lost";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool RollOn => Spec.Kind == PerfTestKind.RollOn;

    private double StartLevel => (RollOn || Spec.Kind == PerfTestKind.StandingStart ? Spec.StartKmh : Spec.StartRpm) ?? double.NaN;

    private double EndLevel => (RollOn || Spec.Kind == PerfTestKind.StandingStart ? Spec.EndKmh : Spec.EndRpm) ?? double.NaN;

    private string Unit => RollOn || Spec.Kind == PerfTestKind.StandingStart ? "km/h" : "rpm";

    // Pulls wait 200 to 700 rpm below the start, roll-ons 5 to 15 km/h below it.
    private (double Far, double Near) Window => RollOn ? (15d, 5d) : (700d, 200d);

    // A roll-on's speed held within 2 km/h for 1 s (steady below the start speed); a pull needs no steadiness.
    private bool Steady(PerfFrame frame, double? measure)
    {
        if (!RollOn)
        {
            return true;
        }

        if (measure is not { } kmh)
        {
            _rollOnSteady = null;
            return false;
        }

        if (_rollOnSteady is not { } stretch || Math.Abs(kmh - stretch.Kmh) > RollOnSteadyKmh)
        {
            _rollOnSteady = (kmh, frame.At);
            return false;
        }

        return frame.At - stretch.Since >= RollOnSteadyFor;
    }

    private void PushPullType(PerfFrame frame, ReadOnlyMemory<byte> payload)
    {
        var measure = Measure(frame);
        if (Stage == RunStage.Waiting)
        {
            Record(frame, payload, prune: true);
            var pedal = PedalShare(frame);
            var warm = Warm(frame);
            var inGear = Spec.Gear is { } gear && frame.Gear == gear;
            var (far, near) = Window;
            var steady = Steady(frame, measure);
            var ready = measure is { } m && m >= StartLevel - far && m <= StartLevel - near && steady;
            var released = pedal < Released;
            var full = pedal >= FullThrottle;
            _checks = PullChecks(frame, measure, warm, inGear, ready, released);

            // Ready is judged with the pedal released and holds while it goes down, however slowly; only full throttle
            // below the start level starts the run. Full throttle that comes at or above it cannot be timed from the
            // start: the run says so until the pedal is lifted.
            var held = warm && _pedalCalibrated && inGear;
            var wasArmed = _armed;
            if (released)
            {
                _armed = held && ready;
                _tooLate = false;
            }
            else if (!held)
            {
                _armed = false;
            }

            if (_armed && !wasArmed)
            {
                _armedConditions = ConditionsOf(frame);
            }

            if (full && _armed && held && measure < StartLevel)
            {
                StartRunning(frame);
                _triggerMeasure = measure;
                _peak = frame.Rpm;
                _peakAt = _frames.Count - 1;
                return;
            }

            if (full && _armed && measure >= StartLevel)
            {
                _armed = false;
                _tooLate = true;
            }

            Status = _tooLate
                ? $"Full throttle came above {RunAnalysis.Whole(StartLevel)} {Unit}: lift off and start again below {RunAnalysis.Whole(StartLevel - near)} {Unit}."
                : _armed ? "Waiting for full throttle…" : WaitingText;
            return;
        }

        Record(frame, payload, prune: false);
        Watch(frame);
        var index = _frames.Count - 1;
        if (_windowStart < 0 && measure >= StartLevel)
        {
            index = OpenWindow(index, StartLevel);
        }

        if (_windowStart >= 0 && measure >= EndLevel)
        {
            _tEnd = CrossingInto(index, EndLevel);
            Finish(true, null, index);
            return;
        }

        // Incomplete: two samples below full throttle, the clutch or the brake, another gear, or the revs falling
        // 100 below their peak.
        if (PedalShare(frame) is { } share)
        {
            if (share >= FullThrottle)
            {
                _lowFrom = -1;
            }
            else if (_lowFrom < 0)
            {
                _lowFrom = index;
            }
            else
            {
                End($"pedal lifted at {MeasureText(_frames[_lowFrom])}", _lowFrom - 1);
                return;
            }
        }

        if (frame.Clutch == true)
        {
            End($"clutch pressed at {MeasureText(frame)}", index - 1);
            return;
        }

        if (frame.Brake == true)
        {
            End($"brake pressed at {MeasureText(frame)}", index - 1);
            return;
        }

        if (frame.Gear is { } now && now != Spec.Gear)
        {
            End($"gear changed at {MeasureText(frame)}", index - 1);
            return;
        }

        if (frame.Rpm is { } rpm)
        {
            if (_peak is { } peak && rpm <= peak - RpmDrop)
            {
                End($"{(RollOn ? "speed" : "revs")} dropped from {MeasureText(_frames[_peakAt])}", _peakAt);
                return;
            }

            if (_peak is null || rpm > _peak)
            {
                _peak = rpm;
                _peakAt = index;
            }
        }

        if (_windowStart >= 0)
        {
            Elapsed = frame.At.TotalSeconds - _t0;
            if (measure is { } m)
            {
                Progress = Share(m - StartLevel, EndLevel - StartLevel);
            }
        }
    }

    private void PushStanding(PerfFrame frame, ReadOnlyMemory<byte> payload)
    {
        var speed = Measure(frame);
        if (Stage == RunStage.Waiting)
        {
            var wasArmed = _armed;
            Record(frame, payload, prune: true);
            var warm = Warm(frame);
            if (speed is { } v)
            {
                _stoppedSince = v <= 0 ? _stoppedSince ?? frame.At : null;
            }

            var stopped = speed <= 0 && _stoppedSince is { } since && frame.At - since >= StoppedFor;
            _checks = StandingChecks(frame, warm, stopped);
            // Running at the first moving sample after a stop of 1 s.
            if (speed > 0 && wasArmed && warm)
            {
                StartRunning(frame);
                var index = OpenWindow(_frames.Count - 1, null);
                _peak = speed;
                _peakAt = index;
                Progress = Share(speed.Value, EndLevel);
                return;
            }

            _armed = warm && stopped;
            if (_armed && !wasArmed)
            {
                _armedConditions = ConditionsOf(frame);
            }

            Status = _armed ? "Waiting for the launch…" : WaitingText;
            return;
        }

        Record(frame, payload, prune: false);
        Watch(frame);
        var at = _frames.Count - 1;
        if (speed is { } s)
        {
            // Time zero is back-extrapolated once the speed has passed 20 km/h; until then it is the first moving sample.
            if (!_t0Fitted && s > FitAboveKmh && FitStart() is { } zero)
            {
                _t0 = zero;
                _t0Fitted = true;
            }

            if (s >= EndLevel)
            {
                Finish(true, null, at);
                return;
            }

            if (_peak is { } peak && s <= peak - SpeedDrop)
            {
                StandingEnd($"speed dropped from {MeasureText(_frames[_peakAt])}", _peakAt);
                return;
            }

            if (_peak is null || s > _peak)
            {
                _peak = s;
                _peakAt = at;
            }
        }

        if (PedalShare(frame) is { } share)
        {
            if (share >= Released)
            {
                _releasedFrom = -1;
            }
            else
            {
                if (_releasedFrom < 0)
                {
                    _releasedFrom = at;
                }

                if (frame.At - _frames[_releasedFrom].At >= ReleasedFor)
                {
                    StandingEnd($"pedal released at {MeasureText(_frames[_releasedFrom])}", _releasedFrom - 1);
                    return;
                }
            }
        }

        Elapsed = frame.At.TotalSeconds - _t0;
        if (speed is { } kmh)
        {
            Progress = Share(kmh, EndLevel);
        }
    }

    private void PushCalibration(PerfFrame frame)
    {
        var fast = frame.SpeedKmh >= CalibrationKmh;
        var inGear = frame.Gear > 0;
        // Without a pedal calibration its share is unknown, so the light-pedal condition waits for one.
        var light = !_pedalCalibrated || PedalShare(frame) < LightPedal;
        if (fast && inGear && light && frame.Rpm > 0)
        {
            var (gear, kmh, rpm) = (frame.Gear!.Value, frame.SpeedKmh!.Value, frame.Rpm!.Value);
            // Steady: within 1 km/h and 50 rpm of where the stretch began, in one gear.
            if (_steady is not { } stretch || stretch.Gear != gear || Math.Abs(kmh - stretch.Kmh) > SteadyKmh || Math.Abs(rpm - stretch.Rpm) > SteadyRpm)
            {
                _steady = (gear, kmh, rpm, frame.At);
                _ratios.Clear();
            }

            _ratios.Enqueue((frame.At, kmh / rpm));
            while (_ratios.Peek().At < frame.At - SteadyFor)
            {
                _ratios.Dequeue();
            }
        }
        else
        {
            _steady = null;
            _ratios.Clear();
        }

        var held = _steady is { } steady ? frame.At - steady.Since : TimeSpan.Zero;
        if (_steady is { } captured && held >= SteadyFor)
        {
            _captured[captured.Gear] = RunMath.Median(_ratios.Select(r => r.Ratio));
            Status = $"Captured {RunAnalysis.Ordinal(captured.Gear)} gear";
        }
        else
        {
            Status = _steady is null ? "Hold a steady speed above 50 km/h…" : "Hold it steady…";
        }

        _checks = CalibrationChecks(frame, fast, inGear, light, held);
        Progress = Share(held.TotalSeconds, SteadyFor.TotalSeconds);
    }

    private void PushLink(PerfFrame frame, ReadOnlyMemory<byte> payload)
    {
        Record(frame, payload, prune: false);
        if (_windowStart < 0)
        {
            _windowStart = 0;
            _t0 = frame.At.TotalSeconds;
            _startConditions = _armedConditions = ConditionsOf(frame);
        }

        Elapsed = frame.At.TotalSeconds - _t0;
        Progress = Share(Elapsed, LinkSeconds);
        if (Elapsed >= LinkSeconds - 1e-9)
        {
            Finish(true, null, _frames.Count - 1);
        }
    }

    private void PushPedal(PerfFrame frame)
    {
        if (frame.Pedal is { } reading)
        {
            PedalReading = Math.Max(PedalReading ?? reading, reading);
        }

        // Read with the ignition on and the engine off: held within 1 % of the highest reading for 2 s.
        var off = frame.Rpm is not { } rpm || rpm <= 0;
        var peak = PedalReading ?? 0;
        var near = off && frame.Pedal is { } pedal && peak >= MinFullPedal && pedal >= peak * (1 - PedalBand);
        if (!near)
        {
            _holdSince = null;
        }
        else if (_holdSince is null || peak > _holdPeak * (1 + PedalBand))
        {
            _holdSince = frame.At;
            _holdPeak = peak;
        }

        var held = _holdSince is { } since ? frame.At - since : TimeSpan.Zero;
        _checks = PedalChecks(frame, off, near, held);
        Progress = Share(held.TotalSeconds, PedalHold.TotalSeconds);
        if (held >= PedalHold)
        {
            Stage = RunStage.Finished;
            Status = "Done";
            Progress = 1;
            return;
        }

        Status = near ? "Hold it…" : "Press the pedal fully and hold it…";
    }

    private void StartRunning(PerfFrame frame)
    {
        Stage = RunStage.Running;
        Status = "Running";
        _armed = false;
        _windowStart = -1;
        _tEnd = null;
        _t0Fitted = false;
        _lowFrom = -1;
        _releasedFrom = -1;
        _peak = null;
        _peakAt = -1;
        _triggerMeasure = null;
        _startConditions = null;
        _sawCold = _sawOilCold = _sawOilUnreported = false;
        _lowestOil = null;
        Progress = 0;
        Elapsed = 0;
        Watch(frame);
    }

    /// <summary>
    /// Opens the window at frame <paramref name="index"/>: time zero is the crossing of <paramref name="level"/> from the
    /// frame before (the frame itself without a level), and only 2 s of frames are kept before it. Returns the new index.
    /// </summary>
    private int OpenWindow(int index, double? level)
    {
        _t0 = level is { } crossing ? CrossingInto(index, crossing) : _frames[index].At.TotalSeconds;
        var from = _frames[index].At - PreRoll;
        var cut = 0;
        // The frame before the window always stays: the start crossing is interpolated from it.
        while (cut + 1 < index && _frames[cut + 1].At <= from)
        {
            cut++;
        }

        Drop(cut);
        _windowStart = index - cut;
        _startConditions = ConditionsOf(_frames[_windowStart]);
        return _windowStart;
    }

    // The time the measure crossed level between the last frame before index that has one and index.
    private double CrossingInto(int index, double level)
    {
        var at = _frames[index].At.TotalSeconds;
        if (Measure(_frames[index]) is not { } value)
        {
            return at;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (Measure(_frames[i]) is { } before)
            {
                return RunMath.CrossingTime([_frames[i].At.TotalSeconds, at], [before, value], level) ?? at;
            }
        }

        return at;
    }

    // Time zero of a standing start: a line through 3 to 20 km/h, taken at 0 km/h.
    private double? FitStart()
    {
        var from = Math.Max(_windowStart - 1, 0);
        var frames = _frames.Skip(from).ToArray();
        return RunMath.BackExtrapolatedStart(frames.Select(f => f.At.TotalSeconds).ToArray(),
            frames.Select(f => Measure(f) ?? double.NaN).ToArray());
    }

    // Ends a pull-type run that no longer holds; before the window, or with nothing in it, it is a false start.
    private void End(string reason, int endIndex)
    {
        endIndex = LastGood(endIndex);
        if (_windowStart < 0 || endIndex < _windowStart)
        {
            Rearm();
            return;
        }

        Finish(false, reason, endIndex);
    }

    // A standing start is complete when it reached 100 km/h by the end of its window.
    private void StandingEnd(string reason, int endIndex)
    {
        if (endIndex < _windowStart)
        {
            Rearm();
            return;
        }

        Finish(ReachedHundred(endIndex), reason, endIndex);
    }

    private bool ReachedHundred(int endIndex) => _frames.Take(endIndex + 1).Skip(_windowStart).Any(f => Measure(f) >= 100);

    // A lift seen on one sample, then another reason to end: the window still ends before the lift.
    private int LastGood(int endIndex) => _lowFrom >= 0 ? Math.Min(endIndex, _lowFrom - 1) : endIndex;

    private void Rearm()
    {
        Stage = RunStage.Waiting;
        Status = WaitingText;
        _armed = false;
        _windowStart = -1;
        _lowFrom = -1;
        _releasedFrom = -1;
        _peak = null;
        _peakAt = -1;
        _tEnd = null;
        _t0Fitted = false;
        _stoppedSince = null;
        _rollOnSteady = null;
        _tooLate = false;
        _startConditions = null;
        Progress = 0;
        Elapsed = 0;
    }

    private void Finish(bool complete, string? reason, int endIndex, bool linkLost = false)
    {
        if (endIndex < _frames.Count - 1)
        {
            _frames.RemoveRange(endIndex + 1, _frames.Count - endIndex - 1);
            _payloads.RemoveRange(endIndex + 1, _payloads.Count - endIndex - 1);
        }

        var frames = _frames.ToArray();
        var analysis = RunAnalysis.Analyse(Spec, frames, _inputs, complete);
        // The analysis can only say where an incomplete run ended; the run knows why.
        var flags = analysis.Flags.Where(flag => flag.Code != "incomplete").ToList();
        if (!complete)
        {
            flags.Add(new RunFlag(linkLost ? "link-lost" : "incomplete", FlagSeverity.Invalid, $"Incomplete: {reason}."));
        }
        else if (linkLost)
        {
            // A standing start past 100 km/h: what it reached stands.
            flags.Add(new RunFlag("link-lost", FlagSeverity.Info, $"{char.ToUpperInvariant(reason![0])}{reason[1..]}: the times it reached are kept."));
        }

        flags.AddRange(RunFlags());
        var end = frames[endIndex];
        var samples = frames.Select((frame, i) => new RecordedSample(frame.At, _payloads[i], frame.Retries)).ToArray();
        var start = _startConditions ?? ConditionsOf(frames[Math.Max(_windowStart, 0)]);
        Outcome = new RunOutcome(Spec, complete, analysis with { Flags = RunAnalysis.Ordered(flags) },
            start, ConditionsOf(end), samples, _windowStart, endIndex, _armedConditions ?? start);
        Stage = RunStage.Finished;
        Status = complete ? "Done" : $"Incomplete: {reason}";
        if (Spec.Kind != PerfTestKind.LinkTest)
        {
            Elapsed = (complete && _tEnd is { } tEnd ? tEnd : end.At.TotalSeconds) - _t0;
        }

        if (complete)
        {
            Progress = 1;
        }

        _live = null;
    }

    private IEnumerable<RunFlag> RunFlags()
    {
        // A run made anyway with the coolant red is kept but not comparable; cold or unreported oil makes it doubtful.
        if (_sawCold)
        {
            yield return new RunFlag("cold", FlagSeverity.Cold, "Engine cold: not comparable.");
        }

        // The oil banner is amber below 80 °C and through its 10 s hold at 80 °C or more; the flag words the lowest reading
        // the run saw while it was amber, so a reading above 80 °C is never called below it.
        if (_sawOilCold)
        {
            yield return new RunFlag("oil-cold", FlagSeverity.Suspect, _lowestOil switch
            {
                double oil when oil < OilWarmC => $"Oil {RunAnalysis.Whole(oil)} °C: below working temperature (80 °C).",
                double held => $"Oil {RunAnalysis.Whole(held)} °C: had not held 80 °C for 10 s yet.",
                null => "Oil below working temperature (80 °C).",
            });
        }

        if (_sawOilUnreported)
        {
            yield return new RunFlag("oil-unreported", FlagSeverity.Suspect, "Oil temperature not reported by this ECU.");
        }

        if (RollOn && _triggerMeasure is { } at && Spec.StartKmh is { } start && at > start - LateThrottleKmh)
        {
            yield return new RunFlag("late-throttle", FlagSeverity.Suspect,
                $"Full throttle came only {RunAnalysis.Whole(start - at)} km/h before {RunAnalysis.Whole(start)} km/h.");
        }
    }

    // The warm-up state over the run, read from the shared monitor the caller feeds.
    private void Watch(PerfFrame frame)
    {
        _sawCold |= _warmUp.CoolantCold;
        _sawOilUnreported |= _warmUp.OilUnreported;
        if (_warmUp.OilCold)
        {
            _sawOilCold = true;
            if (frame.OilC is { } oil)
            {
                _lowestOil = Math.Min(_lowestOil ?? oil, oil);
            }
        }
    }

    private void Record(PerfFrame frame, ReadOnlyMemory<byte> payload, bool prune)
    {
        _frames.Add(frame);
        _payloads.Add(payload.ToArray());
        if (!prune)
        {
            return;
        }

        // Waiting keeps 2 s and the frame just before them: the pre-roll a result stores.
        var cut = 0;
        while (cut + 1 < _frames.Count && _frames[cut + 1].At <= frame.At - PreRoll)
        {
            cut++;
        }

        Drop(cut);
    }

    private void Drop(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _frames.RemoveRange(0, count);
        _payloads.RemoveRange(0, count);
        int Shift(int index) => index < 0 ? index : Math.Max(index - count, 0);
        _windowStart = Shift(_windowStart);
        _lowFrom = Shift(_lowFrom);
        _releasedFrom = Shift(_releasedFrom);
        _peakAt = Shift(_peakAt);
    }

    private IReadOnlyList<(double T, double Rpm)> BuildLive()
    {
        if (_windowStart < 0 || Spec.Kind is PerfTestKind.LinkTest or PerfTestKind.SpeedCalibration or PerfTestKind.Pedal)
        {
            return [];
        }

        var points = new List<(double T, double Rpm)>();
        if (Spec.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck)
        {
            points.Add((0, StartLevel));
        }
        else if (RollOn && _windowStart > 0 && _frames[_windowStart - 1].Rpm is { } a && _frames[_windowStart].Rpm is { } b)
        {
            // The rpm at the start crossing, between the two frames around it.
            var (ta, tb) = (_frames[_windowStart - 1].At.TotalSeconds, _frames[_windowStart].At.TotalSeconds);
            var fraction = tb > ta ? Math.Clamp((_t0 - ta) / (tb - ta), 0, 1) : 1;
            points.Add((0, a + fraction * (b - a)));
        }

        for (var i = _windowStart; i < _frames.Count; i++)
        {
            if (_frames[i].Rpm is { } rpm)
            {
                points.Add((_frames[i].At.TotalSeconds - _t0, rpm));
            }
        }

        return points;
    }

    // What a pull-type test times: rpm, or for a roll-on the speed from rpm when calibrated, else VS; a standing start times
    // VS. VS carries any GPS correction of the speed calibration.
    private double? Measure(PerfFrame frame) => Spec.Kind switch
    {
        PerfTestKind.RollOn => _rollOnSpeed(frame),
        PerfTestKind.StandingStart => frame.SpeedKmh * _speedFactor,
        _ => frame.Rpm,
    };

    private string MeasureText(PerfFrame frame) => Measure(frame) is { } value ? $"{RunAnalysis.Whole(value)} {Unit}" : "n/a";

    private double? PedalShare(PerfFrame? frame) => _pedalCalibrated && frame?.Pedal is { } pedal ? 100 * pedal / _inputs.FullPedal : null;

    // A coolant reading and no red banner.
    private bool Warm(PerfFrame? frame) => frame?.CoolantC is not null && !_warmUp.CoolantCold;

    private Conditions ConditionsOf(PerfFrame frame) => new(frame.CoolantC, _warmUp.OilUnreported ? null : frame.OilC, frame.IntakeC,
        frame.PressureKpa, frame.BatteryV, frame.RonAdaptation, frame.LongTermTrim1, frame.LongTermTrim2);

    private static double Share(double part, double whole) => whole > 0 && double.IsFinite(part / whole) ? Math.Clamp(part / whole, 0, 1) : 0;

    // Full throttle is no check: it cannot be met together with "Pedal released", and the status asks for it once the checks
    // are met.
    private IReadOnlyList<ReadyCheck> PullChecks(PerfFrame? frame, double? measure, bool warm, bool inGear, bool ready, bool released)
    {
        var pedal = PedalText(frame);
        var gear = Spec.Gear is { } g ? RunAnalysis.Ordinal(g) : "n/a";
        // The window the run waits in, as the check tests it: "Between 1300 and 1800 rpm", "Steady between 45 and 55 km/h".
        var (far, near) = Window;
        var range = $"{RunAnalysis.Whole(StartLevel - far)} and {RunAnalysis.Whole(StartLevel - near)} {Unit}";
        return
        [
            new ReadyCheck("Engine warm", warm, CoolantText(frame)),
            new ReadyCheck("Pedal calibrated", _pedalCalibrated,
                _pedalCalibrated ? $"Full pedal {_inputs.FullPedal.ToString("0.#", Invariant)}" : "Calibrate the pedal first"),
            new ReadyCheck($"{gear} gear", inGear, GearText(frame)),
            new ReadyCheck(RollOn ? $"Steady between {range}" : $"Between {range}", ready,
                measure is { } m ? $"{RunAnalysis.Whole(m)} {Unit}" : "n/a"),
            new ReadyCheck("Pedal released", released, pedal),
        ];
    }

    private IReadOnlyList<ReadyCheck> StandingChecks(PerfFrame? frame, bool warm, bool stopped)
    {
        var detail = frame?.SpeedKmh switch
        {
            null => "n/a",
            <= 0 when _stoppedSince is { } since => $"0 km/h for {(frame.At - since).TotalSeconds.ToString("0.0", Invariant)} s",
            { } v => $"{RunAnalysis.Whole(v)} km/h",
        };
        return [new ReadyCheck("Engine warm", warm, CoolantText(frame)), new ReadyCheck("Stopped", stopped, detail)];
    }

    private IReadOnlyList<ReadyCheck> CalibrationChecks(PerfFrame? frame, bool fast, bool inGear, bool light, TimeSpan held) =>
    [
        new ReadyCheck("Above 50 km/h", fast, frame?.SpeedKmh is { } v ? $"{RunAnalysis.Whole(v)} km/h" : "n/a"),
        new ReadyCheck("In gear", inGear, GearText(frame)),
        new ReadyCheck("Light pedal", light, _pedalCalibrated ? PedalText(frame) : "Pedal not calibrated"),
        new ReadyCheck("Steady for 3 s", held >= SteadyFor, $"{held.TotalSeconds.ToString("0.0", Invariant)} s"),
    ];

    private static IReadOnlyList<ReadyCheck> PedalChecks(PerfFrame? frame, bool off, bool near, TimeSpan held) =>
    [
        new ReadyCheck("Engine off", off, frame?.Rpm is { } rpm ? $"{RunAnalysis.Whole(rpm)} rpm" : "n/a"),
        new ReadyCheck("Pedal pressed fully", near, frame?.Pedal is { } pedal ? pedal.ToString("0.0", Invariant) : "n/a"),
        new ReadyCheck("Held for 2 s", held >= PedalHold, $"{held.TotalSeconds.ToString("0.0", Invariant)} s"),
    ];

    private static string CoolantText(PerfFrame? frame) => frame?.CoolantC is { } c ? $"Coolant {RunAnalysis.Whole(c)} °C" : "Coolant n/a";

    private static string GearText(PerfFrame? frame) => frame?.Gear switch
    {
        null => "Gear n/a",
        0 => "In neutral",
        { } gear => $"In {RunAnalysis.Ordinal(gear)}",
    };

    private string PedalText(PerfFrame? frame) => PedalShare(frame) is { } share ? $"Pedal {RunAnalysis.Whole(share)} %" : "Pedal n/a";
}
