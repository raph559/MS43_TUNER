using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp;

/// <summary>A choice of the "Compare against" picker; "None" has no file.</summary>
public sealed record BaselineChoice(string Text, string? FileName);

/// <summary>One gear of the speed calibration: "3rd", km/h per 1000 rpm "22.3", the overall ratio "5.18" (performance spec 6.5).</summary>
public sealed record CalibrationRow(string Gear, string KmhPer1000, string OverallRatio);

/// <summary>A ready check as the pane lists it; its tick and reading change in place as the samples come (minor review, finding 21).</summary>
public sealed class ReadyCheckRow : ViewModelBase
{
    private bool _met;
    private string _detail;

    internal ReadyCheckRow(ReadyCheck check)
    {
        Label = check.Label;
        _met = check.Met;
        _detail = check.Detail;
    }

    public string Label { get; }

    public bool Met { get => _met; private set => SetField(ref _met, value); }

    public string Detail { get => _detail; private set => SetField(ref _detail, value); }

    internal void Update(ReadyCheck check)
    {
        Met = check.Met;
        Detail = check.Detail;
    }
}

/// <summary>
/// The Run side of the Performance tab for the test chosen on the rail (performance spec 6, 9.1): its options, Arm, the
/// ready checks, the running view, and the result with Save, Discard and Run again; for the Setup group, the speed
/// calibration, the car values and the pedal step. One run at a time, fed every sample Live shows, whichever tab is open.
/// </summary>
public sealed class PerformanceRunViewModel : ViewModelBase
{
    private const double RevLimitMargin = 150;
    private const double MaxRpm = 8000;
    private const double MaxKmh = 250;
    private const string PedalFirst = "Calibrate the pedal first.";
    private const string PedalUnit = "°PVS";
    // A cylinder's bar gets the warning icon and a line from this retard on (spec 9.2); the link test runs 30 s (spec 6.6).
    private const double KnockWarnFrom = 1.5;
    private const double LinkSeconds = 30;
    // The live chart's time axis grows in whole steps past the baseline's length (minor review, finding 49).
    private const double TimeStep = 5;
    // Samples count as stopped after this long without one, as the Tune curve's live cursor (LiveCursorController.StaleAfter).
    private static readonly TimeSpan SilentAfter = LiveCursorController.StaleAfter;
    private static readonly BaselineChoice NoBaseline = new("None", null);
    private static readonly string AppVersion = typeof(PerformanceRunViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    private readonly PerformanceViewModel _owner;
    private PerformanceTestEntry _entry;

    // The options of the chosen test.
    private IReadOnlyList<int> _gearChoices = [];
    private int? _selectedGear;
    private string _startRpm = "";
    private string _endRpm = "";
    private string _startKmh = "";
    private string _endKmh = "";
    private string? _bandNote;
    private IReadOnlyList<BaselineChoice> _baselineChoices = [NoBaseline];
    private BaselineChoice _selectedBaseline = NoBaseline;
    private bool _baselinePicked;
    private bool _listingBaselines;
    private PerfResult? _baseline;
    private IReadOnlyList<Point>? _baselineTrace;
    private bool _canArm;
    private string? _armTip;
    // The band as last loaded, so a reload knows what the owner typed over it.
    private string _loadedStartRpm = "";
    private string _loadedEndRpm = "";
    private string _loadedStartKmh = "";
    private string _loadedEndKmh = "";

    // The run and what it was armed with (spec 8: the tune at arm time, the owner may switch bins before saving).
    private PerfRun? _run;
    private PerformanceTestEntry? _runEntry;
    private AnalysisInputs? _runInputs;
    private TuneIdentity? _runTune;
    private AdxDefinition? _runDefinition;
    // The baseline taken at Arm stays with the run: its deltas, its choice and its trace (minor review, finding 62).
    private RunFigures? _runBaseline;
    private BaselineChoice _runBaselineChoice = NoBaseline;
    private PerfResult? _runBaselineResult;
    private IReadOnlyList<Point>? _runBaselineTrace;
    private DateTimeOffset _lastArrival;
    // When the last sample reached the tab on the app's clock, and the whole seconds since while a run is on and none came
    // for over SilentAfter (minor review, finding 42).
    private DateTimeOffset? _lastSampleAt;
    private int? _silentSeconds;
    private DispatcherTimer? _sampleWatch;
    private bool _finishSeen;
    private RunStage? _lastStage;
    private PerfFrame? _lastFrame;
    private IReadOnlyList<Point>? _liveTrace;
    private bool _stale;
    private readonly ObservableCollection<ReadyCheckRow> _checks = [];
    private int _version;

    // The result, and what was written to its file once saved.
    private string _name = "";
    private string _description = "";
    private string? _direction;
    private bool _isSaved;
    private string? _savedFile;
    private string _savedName = "";
    private string _savedDescription = "";
    private string? _savedDirection;
    private string? _saveError;
    private string? _notice;
    private PerformanceTestEntry? _noticeEntry;

    // Setup.
    private string _gpsKmh = "";
    private string _gpsVsKmh = "";
    private string _massKg;
    private string _tyreSize;
    private string? _massError;
    private string? _tyreError;

    // ---- Options -----------------------------------------------------------------------------------------------------

    /// <summary>The gears the test runs in: 2nd to 4th for a pull, 4th for the knock check, 3rd or 4th for a roll-on.</summary>
    public IReadOnlyList<int> GearChoices
    {
        get => _gearChoices;
        private set
        {
            if (SetField(ref _gearChoices, value))
            {
                OnPropertyChanged(nameof(OffersGear2));
                OnPropertyChanged(nameof(OffersGear3));
                OnPropertyChanged(nameof(OffersGear4));
            }
        }
    }

    public bool HasGear => GearChoices.Count > 0;

    /// <summary>The gear segment shows only the gears the test runs in.</summary>
    public bool OffersGear2 => GearChoices.Contains(2);

    public bool OffersGear3 => GearChoices.Contains(3);

    public bool OffersGear4 => GearChoices.Contains(4);

    public bool HasRpmBand => _entry.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck;

    public bool HasSpeedBand => _entry.Kind is PerfTestKind.RollOn;

    /// <summary>Where the end came from when it is the open bin's limit: "Ends 150 rpm below the open bin's rev limit in 3rd (7254 rpm)."</summary>
    public string? BandNote { get => _bandNote; private set => SetField(ref _bandNote, value); }

    /// <summary>"Compare against": "None", then the saved results of this test and gear that are not cold, newest first.</summary>
    public IReadOnlyList<BaselineChoice> BaselineChoices { get => _baselineChoices; private set => SetField(ref _baselineChoices, value); }

    public bool HasBaseline => _entry.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn or PerfTestKind.StandingStart;

    /// <summary>The picker is free until the shown test has a run, whose deltas were taken against the baseline it was armed with.</summary>
    public bool CanChangeBaseline => Shown is null;

    /// <summary>The baseline's rpm against seconds from its start crossing, drawn dashed behind the live trace.</summary>
    public IReadOnlyList<Point>? BaselineTrace
    {
        get => _baselineTrace;
        private set
        {
            if (SetField(ref _baselineTrace, value))
            {
                OnPropertyChanged(nameof(BaselineLabel));
            }
        }
    }

    /// <summary>False for Car, which runs nothing.</summary>
    public bool HasArm => _entry.Kind is not null;

    /// <summary>The test has a gear, a band or a baseline to choose; without any, Arm leads the row.</summary>
    public bool HasOptions => HasGear || HasRpmBand || HasSpeedBand || HasBaseline;

    public string? ArmText => _entry.Kind switch
    {
        PerfTestKind.Pull or PerfTestKind.KnockCheck => "Arm pull",
        PerfTestKind.RollOn => "Arm roll-on",
        PerfTestKind.StandingStart => "Arm standing start",
        PerfTestKind.SpeedCalibration => "Start capture",
        PerfTestKind.Pedal => "Read pedal",
        PerfTestKind.LinkTest => "Start link test",
        _ => null,
    };

    public bool CanArm { get => _canArm; private set => SetField(ref _canArm, value); }

    /// <summary>Why Arm is off, in a line the pane shows too (system 5.9); null while it is on.</summary>
    public string? ArmTip
    {
        get => _armTip;
        private set
        {
            if (SetField(ref _armTip, value))
            {
                OnPropertyChanged(nameof(NeedsPedal));
                OnPropertyChanged(nameof(ShowsArmTip));
                OnPropertyChanged(nameof(ShowsRunningTest));
            }
        }
    }

    /// <summary>Arm waits for the pedal step, so the pane offers a way to it (spec 4.4).</summary>
    public bool NeedsPedal => ArmTip == PedalFirst;

    /// <summary>
    /// Arm shows until the chosen test has a run: then Cancel, or the result's Save, Discard and Run again take its place, so
    /// the screen keeps one primary button (system 5.2).
    /// </summary>
    public bool ShowsArm => HasArm && Shown is not { Stage: RunStage.Waiting or RunStage.Running } && !HasResult;

    /// <summary>The row of options and Arm; empty while a test without options runs, so it takes no room then.</summary>
    public bool ShowsOptionsRow => HasArm && (HasOptions || ShowsArm);

    /// <summary>The line under the options that says why Arm is off, while Arm shows.</summary>
    public bool ShowsArmTip => ShowsArm && ArmTip is not null;

    /// <summary>Arm is the screen's primary once connected; before that the header's Connect is (spec 9.1).</summary>
    public bool ArmIsPrimary => _owner.Live.IsConnected;

    /// <summary>The gear, band and baseline of a waiting or running run cannot change.</summary>
    public bool CanChangeOptions => Shown is not { Stage: RunStage.Waiting or RunStage.Running };

    /// <summary>
    /// Samples are arriving: live marks are orange; Ink3 once the link is lost or stale, or while a run is on and no sample came
    /// for over a second (system 1.4).
    /// </summary>
    public bool IsLive => _owner.Live.IsConnected && !_owner.Live.IsStale && _silentSeconds is null;

    /// <summary>Live's warm-up banners, at the top of the pane (performance spec 5).</summary>
    public ObservableCollection<WarmUpBannerViewModel> WarmUp => _owner.Live.WarmUpBanners;

    /// <summary>The latest sample's frame, as Live's warm-up monitor read it.</summary>
    internal PerfFrame? LastFrame => _lastFrame;

    /// <summary>
    /// A line about what just happened on the shown test: the link lost, a calibration or pedal reading saved; null otherwise.
    /// It stays with the test that raised it (minor review, finding 28).
    /// </summary>
    public string? Notice => ReferenceEquals(_noticeEntry, _entry) ? _notice : null;

    /// <summary>Arm waits for a run of another test: the line under it offers a way to that test (minor review, finding 29).</summary>
    public bool ShowsRunningTest => ShowsArmTip && _run is { Stage: RunStage.Waiting or RunStage.Running } && _runEntry is not null
        && !ReferenceEquals(_runEntry, _entry);

    // ---- The run -----------------------------------------------------------------------------------------------------

    /// <summary>The stage of the run of the chosen test; null when it has none (the options and Arm show).</summary>
    public RunStage? Stage => Shown?.Stage;

    public bool IsIdle => Shown is null;

    public bool IsWaiting => Stage == RunStage.Waiting;

    public bool IsRunning => Stage == RunStage.Running;

    public bool IsFinished => Stage == RunStage.Finished;

    /// <summary>The ready checks: the same rows while their labels stay, changed in place (minor review, finding 21).</summary>
    public IReadOnlyList<ReadyCheckRow> Checks
    {
        get
        {
            SyncChecks();
            return _checks;
        }
    }

    /// <summary>The run's own status, or "No samples for 2 s." while it is on and none come (minor review, finding 42).</summary>
    public string? Status => _silentSeconds is { } silent && Shown is { Stage: RunStage.Waiting or RunStage.Running }
        ? $"No samples for {silent.ToString(System.Globalization.CultureInfo.InvariantCulture)} s."
        : Shown?.Status;

    public string RpmText => NumberFormat.Format(Shown?.Rpm, 0);

    /// <summary>Seconds since the start crossing, "3.41"; n/a before the run starts.</summary>
    public string ElapsedText => Shown is { Stage: RunStage.Running or RunStage.Finished } run ? NumberFormat.Format(run.Elapsed, 2) : "n/a";

    public double Progress => Shown?.Progress ?? 0;

    /// <summary>The latest knock retard per cylinder; empty when the definition logs none.</summary>
    public IReadOnlyList<double?> Knock => Shown?.Knock ?? [];

    /// <summary>Rpm against seconds since the start crossing, built when it is read.</summary>
    public IReadOnlyList<Point> LiveTrace => Shown is not { } run ? [] : _liveTrace ??= run.Live.Select(point => new Point(point.T, point.Rpm)).ToArray();

    /// <summary>
    /// The live chart's time axis, held for the run: the baseline's length, or 5 s, then whole 5 s steps once the run passes
    /// it, so the chart never rescales with each sample (minor review, finding 49).
    /// </summary>
    public double? LiveXMaximum
    {
        get
        {
            if (Shown is null)
            {
                return null;
            }

            var reached = LiveTrace.Where(IsFinite).Select(point => point.X).DefaultIfEmpty(0).Max();
            var baseline = BaselineTrace?.Where(IsFinite).Select(point => point.X).DefaultIfEmpty(0).Max() ?? 0;
            var floor = baseline > 0 ? baseline : TimeStep;
            return reached <= floor ? floor : Math.Ceiling(reached / TimeStep) * TimeStep;
        }
    }

    /// <summary>The live chart's rpm axis, held for the run: the pull's band, a calibrated roll-on's speeds in its gear, else 0 to 8000 rpm.</summary>
    public double? LiveYMinimum => LiveRpmRange?.From;

    public double? LiveYMaximum => LiveRpmRange?.To;

    /// <summary>"Ignition 31.5 °CRK · VANOS in 112.0 °CRK · AFR target 12.8 · intake air 38 °C" from the latest sample (spec 6.1).</summary>
    public string? LiveValuesText => Shown is null || _lastFrame is not { } frame
        ? null
        : string.Join(" · ",
            $"Ignition {Degrees(frame.IgnitionAvg)}",
            $"VANOS in {Degrees(frame.VanosIn)}",
            $"AFR target {NumberFormat.Format(frame.AfrTarget, 1)}",
            $"intake air {ResultText.Unit(frame.IntakeC, 0, "°C")}");

    /// <summary>Changes with every sample the run takes, so the charts redraw.</summary>
    public int Version { get => _version; private set => SetField(ref _version, value); }

    /// <summary>The checks show while a test waits, and while a speed calibration or the pedal step runs on its conditions.</summary>
    public bool ShowsChecks => Shown is { Stage: RunStage.Waiting }
        || Shown is { Stage: RunStage.Running, Spec.Kind: PerfTestKind.SpeedCalibration or PerfTestKind.Pedal };

    public string ChecksTitle => Shown?.Spec.Kind switch
    {
        PerfTestKind.SpeedCalibration => "Capture",
        PerfTestKind.Pedal => "Pedal reading",
        _ => "Ready check",
    };

    /// <summary>How the test starts, under the checks (mockup: "The run starts by itself once all are met").</summary>
    public string? ChecksNote => Shown?.Spec.Kind switch
    {
        PerfTestKind.Pull or PerfTestKind.KnockCheck => "Once every check is met, floor it: the run starts by itself.",
        PerfTestKind.RollOn => "Hold the speed steady, then floor it: the run starts by itself.",
        PerfTestKind.StandingStart => "Stop for a second, then pull away: the run starts by itself.",
        PerfTestKind.SpeedCalibration => "Each gear is captured once speed and rpm hold steady for 3 s above 50 km/h with a light pedal.",
        PerfTestKind.Pedal => "Ignition on, engine off: press the pedal fully and hold it for 2 s.",
        _ => null,
    };

    /// <summary>The big reading, the progress and the live chart of a running pull, roll-on, standing start or link test.</summary>
    public bool ShowsRunReadout => Shown is { Stage: RunStage.Running, Spec.Kind: not (PerfTestKind.SpeedCalibration or PerfTestKind.Pedal) };

    public bool CanCancel => Shown is { Stage: RunStage.Waiting or RunStage.Running };

    /// <summary>A speed calibration is capturing: the GPS fields and Save calibration show.</summary>
    public bool IsCapturing => Shown is { Stage: RunStage.Running, Spec.Kind: PerfTestKind.SpeedCalibration };

    /// <summary>The running reading: rpm for a pull, the speedometer for a speed test, the seconds of a link test.</summary>
    public string ReadingValue => Shown?.Spec.Kind switch
    {
        PerfTestKind.RollOn or PerfTestKind.StandingStart => NumberFormat.Format(_lastFrame?.SpeedKmh, 0),
        PerfTestKind.LinkTest => NumberFormat.Format(Shown.Elapsed, 1),
        _ => RpmText,
    };

    public bool ReadingMissing => ReadingValue == "n/a";

    public string ReadingUnit => Shown?.Spec.Kind switch
    {
        PerfTestKind.RollOn or PerfTestKind.StandingStart => "km/h",
        PerfTestKind.LinkTest => "s",
        _ => "rpm",
    };

    /// <summary>The elapsed time beside the reading; a link test's reading is its elapsed time.</summary>
    public bool ShowsElapsed => Shown?.Spec.Kind is not PerfTestKind.LinkTest;

    /// <summary>The live values line and the rpm-against-time chart, for the tests that drive (spec 6.1).</summary>
    public bool ShowsLiveChart => Shown?.Spec.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn or PerfTestKind.StandingStart;

    /// <summary>The progress bar's scale: the run's rpm band, its speeds, or the link test's 30 s.</summary>
    public double ProgressFrom => Shown?.Spec switch
    {
        { Kind: PerfTestKind.Pull or PerfTestKind.KnockCheck, StartRpm: { } start } => start,
        { Kind: PerfTestKind.RollOn or PerfTestKind.StandingStart, StartKmh: { } start } => start,
        _ => 0,
    };

    public double ProgressTo => Shown?.Spec switch
    {
        { Kind: PerfTestKind.Pull or PerfTestKind.KnockCheck, EndRpm: { } end } => end,
        { Kind: PerfTestKind.RollOn or PerfTestKind.StandingStart, EndKmh: { } end } => end,
        { Kind: PerfTestKind.LinkTest } => LinkSeconds,
        _ => 1,
    };

    /// <summary>Where the run is on that scale; the run's own share of it, so it never runs past the end.</summary>
    public double? ProgressValue => Shown is { } run ? ProgressFrom + run.Progress * (ProgressTo - ProgressFrom) : null;

    /// <summary>Ticks every 1000 rpm, every 10 km/h of a roll-on (its splits), every 50 km/h of a standing start, every 10 s.</summary>
    public double ProgressStep => Shown?.Spec.Kind switch
    {
        PerfTestKind.Pull or PerfTestKind.KnockCheck => 1000,
        PerfTestKind.RollOn => 10,
        PerfTestKind.StandingStart => 50,
        PerfTestKind.LinkTest => 10,
        _ => 0,
    };

    /// <summary>What ends the running test, under its readout.</summary>
    public string? RunNote => Shown is not { Stage: RunStage.Running } run ? null : run.Spec.Kind switch
    {
        PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn =>
            "Lift off, brake, press the clutch or change gear and the run ends, marked incomplete.",
        PerfTestKind.StandingStart => "Release the pedal and the run ends with the times it reached.",
        PerfTestKind.LinkTest => "Leave the engine idling or the car parked until the 30 s are up.",
        _ => null,
    };

    /// <summary>"Baseline: 3rd pull 14:05", the dashed trace's legend; null without one.</summary>
    public string? BaselineLabel => _selectedBaseline.FileName is null || BaselineTrace is not { Count: > 0 } ? null : $"Baseline: {_selectedBaseline.Text}";

    /// <summary>Stops a waiting or running test; nothing is kept.</summary>
    public void Cancel() => _run?.Cancel();

    // ---- The result --------------------------------------------------------------------------------------------------

    public bool HasResult => Shown is { Stage: RunStage.Finished, Outcome: not null };

    /// <summary>The run's figures, each delta said against the baseline it was armed with ("-0.13 s against 3rd pull 14:05").</summary>
    public IReadOnlyList<FigureRow> Figures => Shown?.Outcome is { } outcome
        ? ResultText.Figures(outcome.Figures, _runBaseline, _runBaselineChoice.FileName is null ? null : _runBaselineChoice.Text)
        : [];

    public IReadOnlyList<FlagRow> Flags => Shown?.Outcome is { } outcome ? ResultText.Flags(outcome.Figures) : [];

    /// <summary>Each cylinder's worst knock retard of a finished knock check, drawn as the six bars; null for other tests.</summary>
    public IReadOnlyList<double?>? ResultKnock => Shown?.Outcome is { } outcome ? ResultText.KnockByCylinder(outcome.Figures) : null;

    public bool HasResultKnock => ResultKnock is not null;

    /// <summary>The line under the stored bars naming each cylinder at or past 1.5 °CRK, as the running view's (minor review, finding 34).</summary>
    public string? ResultKnockText => ResultKnock is { } knock ? ResultText.KnockLine(knock, KnockWarnFrom) : null;

    /// <summary>"Flags below: knock, oil cold" beside Save result, for when the flags are below the fold (minor review, finding 43).</summary>
    public string? FlagSummary => Flags.Where(flag => !flag.IsInfo).Select(flag => flag.Code.Replace('-', ' ')).Distinct().ToArray() is { Length: > 0 } codes
        ? $"Flags below: {string.Join(", ", codes)}"
        : null;

    /// <summary>A cold or invalid run: the summary takes the red warning icon, other doubts the amber one.</summary>
    public bool HasSevereFlag => Flags.Any(flag => flag.IsWarn);

    /// <summary>The tune the run was armed with, for the tune line's file name, firmware id and edits.</summary>
    public TuneIdentity? Tune => HasResult ? _runTune : null;

    public string? ConditionsText => Shown?.Outcome is { } outcome ? ResultText.ConditionsLine(outcome.Start) : null;

    public string? TuneText => HasResult ? ResultText.TuneLine(_runTune) : null;

    /// <summary>Asks whether to save a finished run before the app closes ("Save the result "3rd pull 14:32"?").</summary>
    public Func<string, System.Threading.Tasks.Task<SaveChoice>>? AskToSaveResult { get; set; }

    /// <summary>"Not saved: {reason}" after a save failed; null otherwise.</summary>
    public string? SaveError { get => _saveError; private set => SetField(ref _saveError, value); }

    // ---- Setup -------------------------------------------------------------------------------------------------------

    public bool IsCalibration => _entry.Kind == PerfTestKind.SpeedCalibration;

    public bool IsCar => _entry.Kind is null;

    public bool IsPedal => _entry.Kind == PerfTestKind.Pedal;

    /// <summary>Km/h per rpm by gear: captured so far while a capture runs, else the saved calibration.</summary>
    public IReadOnlyDictionary<int, double> Captured => Shown is { Spec.Kind: PerfTestKind.SpeedCalibration } run ? run.Captured : _owner.Settings.KmhPerRpm;

    public IReadOnlyList<CalibrationRow> CapturedRows
    {
        get
        {
            var circumference = RunMath.TyreCircumference(_owner.Settings.TyreSize);
            return Captured.OrderBy(pair => pair.Key).Select(pair => new CalibrationRow(
                ResultText.Ordinal(pair.Key),
                NumberFormat.Format(pair.Value * 1000, 1),
                // Engine turns per wheel turn: 60 × circumference / (km/h per rpm × 1000).
                circumference is { } metres ? NumberFormat.Format(0.06 * metres / pair.Value, 2) : "n/a")).ToArray();
        }
    }

    public bool CanSaveCalibration => Shown is { Spec.Kind: PerfTestKind.SpeedCalibration } run && run.Captured.Count > 0;

    /// <summary>A gear is captured or saved, so the calibration table has rows.</summary>
    public bool HasCaptured => Captured.Count > 0;

    /// <summary>The speed a phone's GPS showed, typed; the speedometer's reading is taken as it is typed (spec 6.5).</summary>
    public string GpsKmh
    {
        get => _gpsKmh;
        set
        {
            if (SetField(ref _gpsKmh, value ?? "") && !string.IsNullOrWhiteSpace(_gpsKmh) && string.IsNullOrWhiteSpace(GpsVsKmh)
                && _lastFrame?.SpeedKmh is { } speedometer)
            {
                GpsVsKmh = NumberFormat.Format(speedometer, 0);
            }
        }
    }

    /// <summary>What the speedometer (VS) read at the moment of <see cref="GpsKmh"/>.</summary>
    public string GpsVsKmh { get => _gpsVsKmh; set => SetField(ref _gpsVsKmh, value ?? ""); }

    public string? MassError { get => _massError; private set => SetField(ref _massError, value); }

    public string? TyreError { get => _tyreError; private set => SetField(ref _tyreError, value); }

    /// <summary>"Full pedal reads 96.1 °PVS." once calibrated (spec 4.4), in the pedal's ADX unit (PVS and ACCELERATORPED alike).</summary>
    public string PedalText => _owner.Settings.FullPedal is { } full
        ? $"Full pedal reads {NumberFormat.Format(full, 1)} {PedalUnit}."
        : "The pedal is not calibrated yet.";

    /// <summary>A higher reading raised the full-pedal value (spec 4.4).</summary>
    internal void FullPedalChanged() => OnPropertyChanged(nameof(PedalText));

    internal void LinkLost()
    {
        _lastSampleAt = null;
        _run?.LinkLost();
    }

    private void ListBaselines(IReadOnlyList<BaselineChoice> choices)
    {
        _listingBaselines = true;
        try
        {
            BaselineChoices = choices;
        }
        finally
        {
            _listingBaselines = false;
        }
    }

    /// <summary>The link went stale or came back: the live marks follow (system 1.4).</summary>
    internal void LiveStateChanged() => OnPropertyChanged(nameof(IsLive));

    private PerfRun? Shown => _run is not null && ReferenceEquals(_runEntry, _entry) ? _run : null;

    private AppSettings Settings => _owner.Settings;

    // The run's checks into the rows: in place while the labels stay, so the list is never rebuilt for a new reading.
    private void SyncChecks()
    {
        var checks = Shown?.Checks ?? [];
        if (checks.Count == _checks.Count && checks.Select(check => check.Label).SequenceEqual(_checks.Select(row => row.Label)))
        {
            for (var i = 0; i < checks.Count; i++)
            {
                _checks[i].Update(checks[i]);
            }

            return;
        }

        _checks.Clear();
        foreach (var check in checks)
        {
            _checks.Add(new ReadyCheckRow(check));
        }
    }

    private void SetNotice(string text, PerformanceTestEntry? entry)
    {
        (_notice, _noticeEntry) = (text, entry);
        OnPropertyChanged(nameof(Notice));
    }

    // Clears the notice of that test; another test's stays with it.
    private void ClearNotice(PerformanceTestEntry? entry)
    {
        if (_notice is not null && ReferenceEquals(_noticeEntry, entry))
        {
            (_notice, _noticeEntry) = (null, null);
            OnPropertyChanged(nameof(Notice));
        }
    }

    // The rpm axis of the live chart (finding 49): rpm = km/h ÷ (km/h per rpm) for a roll-on in a calibrated gear.
    private (double From, double To)? LiveRpmRange => Shown?.Spec switch
    {
        null => null,
        { Kind: PerfTestKind.Pull or PerfTestKind.KnockCheck, StartRpm: { } start, EndRpm: { } end } => (start, end),
        { Kind: PerfTestKind.RollOn, Gear: { } gear, StartKmh: { } start, EndKmh: { } end }
            when _runInputs?.KmhPerRpm.TryGetValue(gear, out var ratio) == true && ratio > 0 => (start / ratio, end / ratio),
        _ => (0, MaxRpm),
    };

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private string DefaultName(PerfTestSpec spec) =>
        $"{spec.ShortName} {TimeZoneInfo.ConvertTime(_lastArrival, _owner.Time.LocalTimeZone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}";

    private double? RevLimit(int gear) => TuneIdentity.From(_owner.Tune.Document, gear)?.RevLimitRpm;

    // The baseline as it reads with the run's car values and speed calibration, so a change of either since it was saved never
    // shows up as a gain (spec 9.2); no deltas when its samples cannot be decoded again, rather than figures made with others.
    private static RunFigures? BaselineFigures(PerfResult? baseline, AnalysisInputs inputs)
    {
        if (baseline is null)
        {
            return null;
        }

        try
        {
            return PerfCompare.Recompute(baseline, inputs);
        }
        catch (System.IO.InvalidDataException)
        {
            return null;
        }
    }

    private static string Whole(double? value) => value is { } v ? NumberFormat.Format(v, 0) : "";

    // Crank degrees, as the ADX names ignition, VANOS and knock retard.
    private static string Degrees(double? value) => value is { } v && double.IsFinite(v) ? NumberFormat.Format(v, 1) + " °CRK" : "n/a";
}
