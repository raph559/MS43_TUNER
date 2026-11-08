using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private IReadOnlyList<string>? _armedLiveTags;
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

    internal PerformanceRunViewModel(PerformanceViewModel owner, PerformanceTestEntry entry)
    {
        _owner = owner;
        _entry = entry;
        _massKg = NumberFormat.Format(owner.Settings.TestMassKg, 0);
        _tyreSize = owner.Settings.TyreSize;
        LoadOptions();
    }

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

    /// <summary>
    /// A new gear brings that preset's band (the last one used, else the preset's own) and its baselines; the gear of a run
    /// that is on stays.
    /// </summary>
    public int? SelectedGear
    {
        get => _selectedGear;
        set
        {
            if ((value is { } gear && !GearChoices.Contains(gear)) || Shown is { Stage: RunStage.Waiting or RunStage.Running })
            {
                return;
            }

            if (SetGear(value))
            {
                ReloadBand();
                _baselinePicked = false;
                RefreshBaselines();
                OnPropertyChanged(nameof(InputsText));
                RefreshAvailability();
            }
        }
    }

    public bool IsGear2 { get => SelectedGear == 2; set => PressGear(2, value); }

    public bool IsGear3 { get => SelectedGear == 3; set => PressGear(3, value); }

    public bool IsGear4 { get => SelectedGear == 4; set => PressGear(4, value); }

    /// <summary>The band of a pull or knock check, as typed; the run uses it once both read as rpm with the start below the end.</summary>
    public string StartRpm { get => _startRpm; set => SetOption(ref _startRpm, value); }

    public string EndRpm { get => _endRpm; set => SetOption(ref _endRpm, value); }

    /// <summary>The speeds of a roll-on, as typed.</summary>
    public string StartKmh { get => _startKmh; set => SetOption(ref _startKmh, value); }

    public string EndKmh { get => _endKmh; set => SetOption(ref _endKmh, value); }

    public bool HasRpmBand => _entry.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck;

    public bool HasSpeedBand => _entry.Kind is PerfTestKind.RollOn;

    /// <summary>Where the end came from when it is the open bin's limit: "Ends 150 rpm below the open bin's rev limit in 3rd (7254 rpm)."</summary>
    public string? BandNote { get => _bandNote; private set => SetField(ref _bandNote, value); }

    /// <summary>"Compare against": "None", then the saved results of this test and gear that are not cold, newest first.</summary>
    public IReadOnlyList<BaselineChoice> BaselineChoices { get => _baselineChoices; private set => SetField(ref _baselineChoices, value); }

    public bool HasBaseline => _entry.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn or PerfTestKind.StandingStart;

    /// <summary>
    /// Defaults to the most recent (spec 6.1); a choice the owner made stays while it is still listed. While the shown test has
    /// a run, it is the baseline that run was armed with, until Discard or Run again.
    /// </summary>
    public BaselineChoice? SelectedBaseline
    {
        get => _selectedBaseline;
        set
        {
            // The picker writes its selection back while its list is replaced: that is not the owner's choice.
            if (_listingBaselines)
            {
                return;
            }

            if (!CanChangeBaseline)
            {
                OnPropertyChanged();
                return;
            }

            if (value is not null && BaselineChoices.Contains(value))
            {
                _baselinePicked = true;
                ShowBaseline(value);
            }
        }
    }

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

    /// <summary>The speed calibration and car values the test will use (spec 6.5: "speed not GPS-checked" when it was not).</summary>
    public string? InputsText => HasResult && _runInputs is not null
        ? ResultText.InputsLine(_runInputs, _run!.Spec.Gear)
        : _entry.Kind is PerfTestKind.Pull or PerfTestKind.RollOn or PerfTestKind.StandingStart
            ? ResultText.InputsLine(_owner.CurrentInputs(), _entry.Kind == PerfTestKind.StandingStart ? null : SelectedGear)
            : null;

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

    /// <summary>Shows the test that is armed or running.</summary>
    public void ShowRunningTest()
    {
        if (_run is { Stage: RunStage.Waiting or RunStage.Running } && _runEntry is { } entry)
        {
            _owner.SelectedEntry = entry;
        }
    }

    // ---- The run -----------------------------------------------------------------------------------------------------

    /// <summary>The stage of the run of the chosen test; null when it has none (the options and Arm show).</summary>
    public RunStage? Stage => Shown?.Stage;

    public bool IsIdle => Shown is null;

    public bool IsWaiting => Stage == RunStage.Waiting;

    public bool IsRunning => Stage == RunStage.Running;

    /// <summary>A run is waiting for its start or recording: the log's extra reads wait, so the samples keep the poll's timing.</summary>
    internal bool IsRecording => _run is { Stage: RunStage.Waiting or RunStage.Running };

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

    /// <summary>The definition logs knock per cylinder (not the stock ones: review focus 2), so the six bars show.</summary>
    public bool HasKnock => ShowsLiveChart && (_runDefinition ?? _owner.Definition) is { } definition
        && PerfChannels.Missing(definition, PerfTestKind.KnockCheck).Count == 0;

    /// <summary>"Knock retard: cylinder 4 at 1.6 °CRK." for every cylinder at or past 1.5 °CRK (spec 9.2: never colour alone).</summary>
    public string? KnockWarning => HasKnock ? ResultText.KnockLine(Knock, KnockWarnFrom) : null;

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

    /// <summary>Arms the chosen test with its options: the tune, the car values and the band are taken now (spec 8, 9.2).</summary>
    public void Arm()
    {
        RefreshAvailability();
        if (!CanArm || OptionsSpec(out _) is not { } spec)
        {
            return;
        }

        _armedLiveTags = _owner.LiveTags?.Invoke();
        _runTune = TuneIdentity.From(_owner.Tune.Document, spec.Gear) is { } tune ? tune with { LiveMaps = _armedLiveTags } : null;
        if (spec.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn)
        {
            // The last band per preset is remembered, so before and after runs normally match (spec 9.2).
            _owner.Settings.PerformanceBands[spec.Preset] = spec.Kind == PerfTestKind.RollOn
                ? [spec.StartKmh!.Value, spec.EndKmh!.Value]
                : [spec.StartRpm!.Value, spec.EndRpm!.Value];
            _owner.Settings.Save();
        }

        var inputs = _owner.CurrentInputs();
        Detach();
        _run = new PerfRun(spec, inputs, _owner.Live.WarmUp);
        _run.Changed += OnRunChanged;
        _lastSampleAt ??= _owner.Time.GetUtcNow();
        WatchSamples(true);
        _runEntry = _entry;
        _runInputs = inputs;
        _runDefinition = null;
        _runBaseline = BaselineFigures(_baseline, inputs);
        (_runBaselineChoice, _runBaselineResult, _runBaselineTrace) = (_selectedBaseline, _baseline, BaselineTrace);
        _finishSeen = false;
        _lastStage = _run.Stage;
        _savedFile = null;
        Name = "";
        Description = "";
        Direction = null;
        IsSaved = false;
        SaveError = null;
        ClearNotice(_entry);
        Project();
        RefreshAvailability();
    }

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

    /// <summary>Pre-filled as "3rd pull 14:32" when the run ends; edited inline before Save.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? ""))
            {
                OnSaveStateChanged();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetField(ref _description, value ?? ""))
            {
                OnSaveStateChanged();
            }
        }
    }

    /// <summary>"A" or "B", which way the road was driven (spec 7); null for one direction.</summary>
    public string? Direction
    {
        get => _direction;
        set
        {
            if (SetField(ref _direction, value is "A" or "B" ? value : null))
            {
                OnPropertyChanged(nameof(IsDirectionA));
                OnPropertyChanged(nameof(IsDirectionB));
                OnSaveStateChanged();
            }
        }
    }

    /// <summary>The direction segment: pressing the lit one clears it.</summary>
    public bool IsDirectionA { get => Direction == "A"; set => PressDirection("A", value); }

    public bool IsDirectionB { get => Direction == "B"; set => PressDirection("B", value); }

    public bool IsSaved
    {
        get => _isSaved;
        private set
        {
            if (SetField(ref _isSaved, value))
            {
                OnPropertyChanged(nameof(SaveText));
                OnSaveStateChanged();
            }
        }
    }

    /// <summary>
    /// Saved, and the name, description or direction changed since: Save changes writes them into the same file (minor review,
    /// findings 61 and 64).
    /// </summary>
    public bool HasChangesToSave => IsSaved && _run?.Outcome is { } outcome
        && (NameFor(outcome.Spec) != _savedName || Description.Trim() != _savedDescription || Direction != _savedDirection);

    public bool CanSave => HasResult && (!IsSaved || HasChangesToSave);

    /// <summary>"Save result", then "Save changes" once the run is in the results.</summary>
    public string SaveText => IsSaved ? "Save changes" : "Save result";

    /// <summary>The Saved mark: saved, with nothing changed since.</summary>
    public bool ShowsSaved => IsSaved && !HasChangesToSave;

    /// <summary>
    /// A finished run that is not saved, or saved with changes not written yet, whichever test the rail shows: closing the app
    /// asks about it (spec 2).
    /// </summary>
    public bool HasUnsavedResult => _run is { Stage: RunStage.Finished, Outcome: not null } && (!IsSaved || HasChangesToSave);

    /// <summary>Asks whether to save a finished run before the app closes ("Save the result "3rd pull 14:32"?").</summary>
    public Func<string, System.Threading.Tasks.Task<SaveChoice>>? AskToSaveResult { get; set; }

    /// <summary>"Not saved: {reason}" after a save failed; null otherwise.</summary>
    public string? SaveError { get => _saveError; private set => SetField(ref _saveError, value); }

    /// <summary>
    /// Writes the result with its tune, conditions and samples to the results folder (spec 8), recorded with the definition
    /// that decoded its samples; once saved, writes a changed name, description or direction into the same file.
    /// </summary>
    public void Save()
    {
        if (_run is not { Outcome: { } outcome } || _runInputs is null || _runDefinition is null)
        {
            return;
        }

        var name = NameFor(outcome.Spec);
        if (IsSaved)
        {
            if (!HasChangesToSave || _savedFile is null)
            {
                return;
            }

            if (!_owner.Store.Update(_savedFile, name, Description.Trim(), Direction))
            {
                SaveError = _owner.Store.LastError;
                return;
            }
        }
        else
        {
            // The variant of the definition the samples were decoded with, not the picker's (minor review, findings 58, 65, 70).
            if (BundledDefinitions.VariantOf(_runDefinition.SourceName) is not { } variant)
            {
                SaveError = $"Not saved: the samples were decoded with {_runDefinition.SourceName}, which is not a bundled definition";
                return;
            }

            PerfResult result;
            try
            {
                result = PerfResult.From(outcome, name, Description.Trim(), _runInputs, _runTune, variant, _runDefinition, _lastArrival, AppVersion);
            }
            catch (InvalidOperationException error)
            {
                SaveError = $"Not saved: {error.Message.Trim().TrimEnd('.')}";
                return;
            }

            result.Direction = Direction;
            if (_owner.Store.Save(result) is not { } file)
            {
                SaveError = _owner.Store.LastError;
                return;
            }

            _savedFile = file;
        }

        (_savedName, _savedDescription, _savedDirection) = (name, Description.Trim(), Direction);
        SaveError = null;
        IsSaved = true;
        OnSaveStateChanged();
        _owner.Results.Refresh();
    }

    /// <summary>
    /// True when the app may close: no finished run unsaved, it is saved now, or the owner chose not to keep it; otherwise only
    /// Discard, Run again or arming another test drop one (spec 2).
    /// </summary>
    public async System.Threading.Tasks.Task<bool> ConfirmCloseAsync()
    {
        if (!HasUnsavedResult || _run?.Outcome is not { } outcome)
        {
            return true;
        }

        var name = NameFor(outcome.Spec);
        var choice = AskToSaveResult is null ? SaveChoice.Cancel : await AskToSaveResult($"Save the result \"{name}\"?");
        switch (choice)
        {
            case SaveChoice.Save:
                Save();
                return !HasUnsavedResult;
            case SaveChoice.DontSave:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Drops the finished run unsaved; the options show again.</summary>
    public void Discard()
    {
        Detach();
        ClearNotice(_entry);
        // The picker is free again: the baselines of the test and gear shown now.
        RefreshBaselines();
        Project();
        RefreshAvailability();
    }

    /// <summary>Drops the finished run and arms the same test again.</summary>
    public void RunAgain()
    {
        if (_runEntry is not { } entry || !ReferenceEquals(entry, _entry))
        {
            return;
        }

        Discard();
        Arm();
    }

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

    /// <summary>
    /// Stores the captured gears in the settings, every one corrected by GPS / speedometer when both were typed, and ends the
    /// capture (spec 6.5); the other gears keep their calibration.
    /// </summary>
    public void SaveCalibration()
    {
        if (Shown is not { Spec.Kind: PerfTestKind.SpeedCalibration } run || run.Captured.Count == 0)
        {
            return;
        }

        var factor = ResultText.Parse(GpsKmh) is { } gps && ResultText.Parse(GpsVsKmh) is { } shown && gps > 0 && shown > 0 ? gps / shown : (double?)null;
        var gears = run.Captured.Keys.Order().ToArray();
        foreach (var gear in gears)
        {
            _owner.Settings.KmhPerRpm[gear] = run.Captured[gear] * (factor ?? 1);
            // VS stays the speedometer's reading, so the slip check needs the ratio it was captured with (spec 4.5).
            _owner.Settings.SpeedometerKmhPerRpm[gear] = run.Captured[gear];
        }

        foreach (var gear in gears)
        {
            // Each gear keeps the check it was saved with (minor review, findings 9 and 23).
            _owner.Settings.GpsChecked[gear] = factor is not null;
        }

        _owner.Settings.Save();
        run.Cancel();
        GpsKmh = "";
        GpsVsKmh = "";
        SetNotice($"Speed calibration saved for {ResultText.Join(gears.Select(ResultText.Ordinal).ToArray())}{(factor is null ? ", not GPS-checked." : ", corrected by the GPS speed.")}", _entry);
        Project();
    }

    /// <summary>Test mass with the driver, in kg; 500 to 3500 is stored, anything else is refused with a reason.</summary>
    public string MassKg
    {
        get => _massKg;
        set
        {
            if (!SetField(ref _massKg, value ?? ""))
            {
                return;
            }

            if (ResultText.Parse(_massKg) is { } kg and >= 500 and <= 3500)
            {
                _owner.Settings.TestMassKg = kg;
                _owner.Settings.Save();
                MassError = null;
                OnPropertyChanged(nameof(InputsText));
            }
            else
            {
                MassError = "Enter a mass from 500 to 3500 kg.";
            }
        }
    }

    public string? MassError { get => _massError; private set => SetField(ref _massError, value); }

    /// <summary>"205/55 R16"; a size the power estimate cannot read is refused with a reason.</summary>
    public string TyreSize
    {
        get => _tyreSize;
        set
        {
            if (!SetField(ref _tyreSize, value ?? ""))
            {
                return;
            }

            OnPropertyChanged(nameof(TyreWidth));
            OnPropertyChanged(nameof(TyreAspect));
            OnPropertyChanged(nameof(TyreRim));

            if (RunMath.TyreCircumference(_tyreSize) is not null)
            {
                _owner.Settings.TyreSize = _tyreSize.Trim();
                _owner.Settings.Save();
                TyreError = null;
                OnPropertyChanged(nameof(InputsText));
                OnPropertyChanged(nameof(CapturedRows));
            }
            else
            {
                TyreError = "Enter a size such as 205/55 R16.";
            }
        }
    }

    public string? TyreError { get => _tyreError; private set => SetField(ref _tyreError, value); }

    /// <summary>Tyre widths a shop offers, 135 to 355 mm in 10 mm steps.</summary>
    public IReadOnlyList<int> TyreWidths { get; } = Enumerable.Range(0, 23).Select(i => 135 + 10 * i).ToArray();

    /// <summary>Profiles (height as a share of the width), 25 to 85 in steps of 5.</summary>
    public IReadOnlyList<int> TyreAspects { get; } = Enumerable.Range(0, 13).Select(i => 25 + 5 * i).ToArray();

    /// <summary>Rim diameters, 13 to 22 inches.</summary>
    public IReadOnlyList<int> TyreRims { get; } = Enumerable.Range(13, 10).ToArray();

    // The three pickers read and write TyreSize, so the saved text stays the one source ("205/55 R16").
    public int? TyreWidth { get => TyreParts(_tyreSize)?.Width; set => PickTyre(value, TyreAspect, TyreRim); }

    public int? TyreAspect { get => TyreParts(_tyreSize)?.Aspect; set => PickTyre(TyreWidth, value, TyreRim); }

    public int? TyreRim { get => TyreParts(_tyreSize)?.Rim; set => PickTyre(TyreWidth, TyreAspect, value); }

    private void PickTyre(int? width, int? aspect, int? rim)
    {
        if (width is { } w && aspect is { } a && rim is { } r)
        {
            TyreSize = $"{w}/{a} R{r}";
            OnPropertyChanged(nameof(TyreWidth));
            OnPropertyChanged(nameof(TyreAspect));
            OnPropertyChanged(nameof(TyreRim));
        }
    }

    // "205/55 R16", "205/55R16", "225/45 ZR17": width, profile and rim; null for anything else.
    private static (int Width, int Aspect, int Rim)? TyreParts(string size) =>
        System.Text.RegularExpressions.Regex.Match(size ?? "", @"^\s*(\d{3})\s*/\s*(\d{2})\s*Z?R\s*(\d{2})\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m
            ? (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;

    public bool Automatic
    {
        get => _owner.Settings.Automatic;
        set
        {
            if (_owner.Settings.Automatic == value)
            {
                return;
            }

            _owner.Settings.Automatic = value;
            _owner.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManual));
            OnPropertyChanged(nameof(IsAutomatic));
            OnPropertyChanged(nameof(InputsText));
        }
    }

    /// <summary>The gearbox segment.</summary>
    public bool IsManual { get => !Automatic; set { if (value) { Automatic = false; } else { OnPropertyChanged(); } } }

    public bool IsAutomatic { get => Automatic; set { if (value) { Automatic = true; } else { OnPropertyChanged(); } } }

    /// <summary>"Full pedal reads 96.1 °PVS." once calibrated (spec 4.4), in the pedal's ADX unit (PVS and ACCELERATORPED alike).</summary>
    public string PedalText => _owner.Settings.FullPedal is { } full
        ? $"Full pedal reads {NumberFormat.Format(full, 1)} {PedalUnit}."
        : "The pedal is not calibrated yet.";

    // ---- Feeding -----------------------------------------------------------------------------------------------------

    internal void Select(PerformanceTestEntry entry)
    {
        _entry = entry;
        LoadOptions();
        // Back on the run's own test, its baseline stays as picked (minor review, finding 62).
        if (!ReferenceEquals(entry, _runEntry))
        {
            _baselinePicked = false;
        }

        RefreshBaselines();
        OnPropertyChanged(nameof(HasGear));
        OnPropertyChanged(nameof(HasRpmBand));
        OnPropertyChanged(nameof(HasSpeedBand));
        OnPropertyChanged(nameof(HasBaseline));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(HasArm));
        OnPropertyChanged(nameof(ArmText));
        OnPropertyChanged(nameof(IsCalibration));
        OnPropertyChanged(nameof(IsCar));
        OnPropertyChanged(nameof(IsPedal));
        OnPropertyChanged(nameof(InputsText));
        OnPropertyChanged(nameof(PedalText));
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(CanChangeBaseline));
        Project();
        RefreshAvailability();
    }

    /// <summary>The tab is in front again: the run view catches up with the samples it skipped while hidden.</summary>
    internal void TabShown()
    {
        if (_stale)
        {
            Project();
        }
    }

    /// <summary>A higher reading raised the full-pedal value (spec 4.4).</summary>
    internal void FullPedalChanged() => OnPropertyChanged(nameof(PedalText));

    /// <summary>
    /// A bin was opened on Tune: the band follows its rev limit (spec 6.1), except a run's own and what the owner typed over
    /// the band last loaded (minor review, finding 27).
    /// </summary>
    internal void BinOpened()
    {
        if (Shown is null)
        {
            ReloadBand(keepTyped: true);
        }
    }

    internal void Push(PerfFrame frame, LiveSample sample, AdxDefinition definition)
    {
        _lastFrame = frame;
        _lastSampleAt = _owner.Time.GetUtcNow();
        if (_silentSeconds is not null)
        {
            _silentSeconds = null;
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(Status));
        }

        if (_run is not { Stage: RunStage.Waiting or RunStage.Running } run)
        {
            return;
        }

        _runDefinition ??= definition;
        if (_runTune is { LiveMapsChanged: false } armed && !SameTags(_armedLiveTags, _owner.LiveTags?.Invoke()))
        {
            _runTune = armed with { LiveMapsChanged = true };
        }

        // The result is made when its last sample arrived (plan Task 7 ruling); never the clock at arrival for test math.
        _lastArrival = sample.Time;
        run.Push(frame, sample.Payload);
    }

    private static bool SameTags(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);

    internal void LinkLost()
    {
        _lastSampleAt = null;
        _run?.LinkLost();
    }

    /// <summary>
    /// While a run is on, notices that samples stopped coming: the marks turn Ink3 and the status says for how long (minor
    /// review, finding 42). A timer calls it every second while a run is on; the link's own loss still ends the run.
    /// </summary>
    internal void CheckSamples()
    {
        int? silent = null;
        if (_run is { Stage: RunStage.Waiting or RunStage.Running } && _lastSampleAt is { } last
            && _owner.Time.GetUtcNow() - last is var quiet && quiet > SilentAfter)
        {
            silent = (int)quiet.TotalSeconds;
        }

        if (silent == _silentSeconds)
        {
            return;
        }

        _silentSeconds = silent;
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>
    /// The band of the chosen preset again, unless its run is on: the last one used, else the preset's own (spec 6.1, 9.2);
    /// <paramref name="keepTyped"/> keeps what the owner typed over the band last loaded.
    /// </summary>
    internal void ReloadBand(bool keepTyped = false)
    {
        if (_run is { Stage: RunStage.Waiting or RunStage.Running } && ReferenceEquals(_runEntry, _entry))
        {
            return;
        }

        var limit = _entry.Kind == PerfTestKind.Pull && SelectedGear == 3 ? RevLimit(3) : null;
        var spec = PresetSpec(limit is { } rpm ? rpm - RevLimitMargin : null);
        if (spec is null)
        {
            ShowBand(null, null, keepTyped);
            return;
        }

        if (spec.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn
            && _owner.Settings.PerformanceBands.TryGetValue(spec.Preset, out var band) && band is [var start, var end])
        {
            spec = spec.Kind == PerfTestKind.RollOn ? spec with { StartKmh = start, EndKmh = end } : spec with { StartRpm = start, EndRpm = end };
            limit = null;
        }

        ShowBand(spec, limit is { } known ? $"Ends {NumberFormat.Format(RevLimitMargin, 0)} rpm below the open bin's rev limit in 3rd ({NumberFormat.Format(known, 0)} rpm)." : null, keepTyped);
    }

    /// <summary>The baselines of the chosen test and gear from the library's last listing; the most recent unless one was picked.</summary>
    internal void RefreshBaselines()
    {
        if (Shown is not null)
        {
            // The run keeps the baseline it was armed with until Discard or Run again (minor review, finding 62).
            ListBaselines([_runBaselineChoice]);
            _baseline = _runBaselineResult;
            _selectedBaseline = _runBaselineChoice;
            OnPropertyChanged(nameof(SelectedBaseline));
            BaselineTrace = _runBaselineTrace;
            return;
        }

        var preset = HasBaseline ? PresetSpec(null)?.Preset : null;
        var choices = new List<BaselineChoice> { NoBaseline };
        if (preset is not null)
        {
            choices.AddRange(_owner.Results.Headers
                .Where(header => header.Preset == preset && !header.Flags.Any(flag => flag.Severity == FlagSeverity.Cold))
                .Select(header => new BaselineChoice(header.Name, header.FileName)));
        }

        ListBaselines(choices);
        var kept = _baselinePicked ? choices.FirstOrDefault(choice => choice.FileName == _selectedBaseline.FileName) : null;
        ShowBaseline(kept ?? (choices.Count > 1 ? choices[1] : NoBaseline));
        // The picker may have dropped its selection with the old list.
        OnPropertyChanged(nameof(SelectedBaseline));
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

    /// <summary>What Arm waits for, in order: the definition's channels, the link, the pedal calibration, a run on, the band.</summary>
    internal void RefreshAvailability()
    {
        var tip = Why();
        ArmTip = tip;
        CanArm = tip is null && HasArm;
        OnPropertyChanged(nameof(ArmIsPrimary));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(HasKnock));
        OnPropertyChanged(nameof(ShowsRunningTest));
    }

    /// <summary>The link went stale or came back: the live marks follow (system 1.4).</summary>
    internal void LiveStateChanged() => OnPropertyChanged(nameof(IsLive));

    private PerfRun? Shown => _run is not null && ReferenceEquals(_runEntry, _entry) ? _run : null;

    private AppSettings Settings => _owner.Settings;

    private string? Why()
    {
        if (_entry.Kind is not { } kind)
        {
            return null;
        }

        if (ResultText.MissingText(_owner.Definition, kind) is { } missing)
        {
            return missing;
        }

        if (!_owner.Live.IsConnected)
        {
            return "Connect to the car to run a test.";
        }

        // Pull-type tests start on full throttle, which is a share of the calibrated pedal (spec 4.4).
        if (kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn && !(Settings.FullPedal > 0))
        {
            return PedalFirst;
        }

        if (_run is { Stage: RunStage.Waiting or RunStage.Running } on)
        {
            return $"{_runEntry?.Title ?? "A test"} is {(on.Stage == RunStage.Waiting ? "armed" : "running")}: cancel it first.";
        }

        OptionsSpec(out var problem);
        return problem;
    }

    private void OnRunChanged(object? sender, EventArgs e)
    {
        if (sender is not PerfRun run || !ReferenceEquals(run, _run))
        {
            return;
        }

        var stageChanged = run.Stage != _lastStage;
        _lastStage = run.Stage;
        if (run.Stage == RunStage.Cancelled)
        {
            if (run.Status == "Link lost")
            {
                SetNotice("The link to the car was lost, so the test stopped.", _runEntry);
            }
            else
            {
                ClearNotice(_runEntry);
            }

            Detach();
        }
        else if (run.Stage == RunStage.Finished && !_finishSeen)
        {
            _finishSeen = true;
            if (run.Outcome is { } outcome)
            {
                Name = DefaultName(outcome.Spec);
            }
            else if (run.Spec.Kind == PerfTestKind.Pedal && run.PedalReading is { } reading)
            {
                // The settings keep 10 to 130 (spec 4.4); the run only finishes on a reading of 10 or more.
                Settings.FullPedal = Math.Min(reading, 130);
                Settings.Save();
                SetNotice($"Pedal calibrated: full pedal reads {NumberFormat.Format(Settings.FullPedal, 1)} {PedalUnit}.", _runEntry);
                OnPropertyChanged(nameof(PedalText));
            }
        }

        _liveTrace = null;
        // A hidden tab's run view is brought up to date when it is shown (minor review, finding 21).
        if (_owner.IsVisible)
        {
            Project();
        }
        else
        {
            _stale = true;
        }

        // What Arm waits for changes with the stage, not with every sample.
        if (stageChanged)
        {
            WatchSamples(run.Stage is RunStage.Waiting or RunStage.Running);
            RefreshAvailability();
        }
    }

    // Every run projection at once; the charts redraw on Version.
    private void Project()
    {
        _stale = false;
        SyncChecks();
        foreach (var name in ProjectionNames)
        {
            OnPropertyChanged(name);
        }

        Version++;
    }

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

    private void OnSaveStateChanged()
    {
        OnPropertyChanged(nameof(HasChangesToSave));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowsSaved));
    }

    // The name saved: as typed, or "3rd pull 14:32" when left empty.
    private string NameFor(PerfTestSpec spec) => string.IsNullOrWhiteSpace(Name) ? DefaultName(spec) : Name.Trim();

    private static readonly string[] ProjectionNames =
    [
        nameof(Stage), nameof(IsIdle), nameof(IsWaiting), nameof(IsRunning), nameof(IsFinished), nameof(Status),
        nameof(RpmText), nameof(ElapsedText), nameof(Progress), nameof(Knock), nameof(LiveTrace), nameof(LiveValuesText),
        nameof(HasResult), nameof(CanSave), nameof(Figures), nameof(Flags), nameof(ConditionsText), nameof(TuneText),
        nameof(InputsText), nameof(Captured), nameof(CapturedRows), nameof(CanSaveCalibration),
        nameof(ShowsArm), nameof(CanChangeOptions), nameof(ShowsChecks), nameof(ChecksTitle), nameof(ChecksNote), nameof(ShowsRunReadout),
        nameof(CanCancel), nameof(IsCapturing), nameof(ReadingValue), nameof(ReadingUnit), nameof(ShowsElapsed), nameof(ShowsLiveChart),
        nameof(HasKnock), nameof(KnockWarning), nameof(ProgressFrom), nameof(ProgressTo), nameof(ProgressValue), nameof(ProgressStep),
        nameof(RunNote), nameof(ShowsArmTip), nameof(ReadingMissing), nameof(HasCaptured), nameof(ShowsOptionsRow), nameof(ResultKnock),
        nameof(HasResultKnock), nameof(SaveText), nameof(ShowsSaved), nameof(HasChangesToSave), nameof(CanChangeBaseline),
        nameof(LiveXMaximum), nameof(LiveYMinimum), nameof(LiveYMaximum), nameof(ResultKnockText), nameof(FlagSummary), nameof(HasSevereFlag),
        nameof(Tune),
    ];

    private void Detach()
    {
        if (_run is not null)
        {
            _run.Changed -= OnRunChanged;
        }

        _run = null;
        _runEntry = null;
        _lastStage = null;
        _liveTrace = null;
        WatchSamples(false);
    }

    // The timer behind CheckSamples runs only while a run is on.
    private void WatchSamples(bool on)
    {
        if (!on)
        {
            _sampleWatch?.Stop();
            _sampleWatch = null;
            CheckSamples();
            return;
        }

        if (_sampleWatch is null && Avalonia.Application.Current is not null)
        {
            _sampleWatch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => CheckSamples());
            _sampleWatch.Start();
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

    private void LoadOptions()
    {
        GearChoices = _entry.Kind switch
        {
            PerfTestKind.Pull => [2, 3, 4],
            PerfTestKind.KnockCheck => [4],
            PerfTestKind.RollOn => [3, 4],
            _ => [],
        };

        if (_run is not null && ReferenceEquals(_runEntry, _entry))
        {
            // Back on the test that runs: its own options.
            SetGear(_run.Spec.Gear);
            ShowBand(_run.Spec, null);
            return;
        }

        // 3rd is the main pull (spec 6.1).
        SetGear(GearChoices.Count == 0 ? null : GearChoices.Contains(3) ? 3 : GearChoices[0]);
        ReloadBand();
    }

    private bool SetGear(int? gear)
    {
        if (!SetField(ref _selectedGear, gear, nameof(SelectedGear)))
        {
            return false;
        }

        OnPropertyChanged(nameof(IsGear2));
        OnPropertyChanged(nameof(IsGear3));
        OnPropertyChanged(nameof(IsGear4));
        return true;
    }

    private void PressGear(int gear, bool on)
    {
        if (on)
        {
            SelectedGear = gear;
        }

        OnPropertyChanged(gear switch { 2 => nameof(IsGear2), 3 => nameof(IsGear3), _ => nameof(IsGear4) });
    }

    private void PressDirection(string direction, bool on)
    {
        if (on)
        {
            Direction = direction;
        }
        else if (Direction == direction)
        {
            Direction = null;
        }

        OnPropertyChanged(nameof(IsDirectionA));
        OnPropertyChanged(nameof(IsDirectionB));
    }

    private void SetOption(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetField(ref field, value ?? "", name))
        {
            RefreshAvailability();
        }
    }

    // keepTyped keeps a field the owner typed over the band last loaded; an end of their own is no longer the bin's limit.
    private void ShowBand(PerfTestSpec? spec, string? note, bool keepTyped = false)
    {
        var (startRpm, endRpm, startKmh, endKmh) = (Whole(spec?.StartRpm), Whole(spec?.EndRpm), Whole(spec?.StartKmh), Whole(spec?.EndKmh));
        var ownEnd = keepTyped && EndRpm != _loadedEndRpm;
        StartRpm = keepTyped && StartRpm != _loadedStartRpm ? StartRpm : startRpm;
        EndRpm = ownEnd ? EndRpm : endRpm;
        StartKmh = keepTyped && StartKmh != _loadedStartKmh ? StartKmh : startKmh;
        EndKmh = keepTyped && EndKmh != _loadedEndKmh ? EndKmh : endKmh;
        (_loadedStartRpm, _loadedEndRpm, _loadedStartKmh, _loadedEndKmh) = (startRpm, endRpm, startKmh, endKmh);
        BandNote = ownEnd ? null : note;
    }

    private void ShowBaseline(BaselineChoice choice)
    {
        if (!SetField(ref _selectedBaseline, choice, nameof(SelectedBaseline)) && (_baseline is not null || choice.FileName is null))
        {
            return;
        }

        _baseline = choice.FileName is { } file ? _owner.Store.Load(file) : null;
        BaselineTrace = _baseline?.Figures.Traces.FirstOrDefault(trace => trace.Key == "rpm-time") is { } trace
            ? trace.X.Zip(trace.Y, (x, y) => new Point(x, y)).Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).ToArray()
            : null;
    }

    // The preset of the chosen test and gear; endRpm replaces a pull's end.
    private PerfTestSpec? PresetSpec(double? endRpm) => _entry.Kind switch
    {
        PerfTestKind.Pull when SelectedGear is { } gear => PerfTestSpec.Pull(gear, endRpm),
        PerfTestKind.KnockCheck => PerfTestSpec.KnockCheck(),
        PerfTestKind.RollOn when SelectedGear is { } gear => PerfTestSpec.RollOn(gear),
        PerfTestKind.StandingStart => PerfTestSpec.StandingStart(),
        PerfTestKind.SpeedCalibration => PerfTestSpec.SpeedCalibration(),
        PerfTestKind.LinkTest => PerfTestSpec.LinkTest(),
        PerfTestKind.Pedal => PerfTestSpec.Pedal(),
        _ => null,
    };

    // The preset with the typed band; problem says what is wrong with it.
    private PerfTestSpec? OptionsSpec(out string? problem)
    {
        problem = null;
        if (PresetSpec(null) is not { } spec)
        {
            return null;
        }

        if (spec.Kind is PerfTestKind.Pull or PerfTestKind.KnockCheck)
        {
            if (ResultText.Parse(StartRpm) is { } start && ResultText.Parse(EndRpm) is { } end && start > 0 && end <= MaxRpm && start < end)
            {
                return spec with { StartRpm = start, EndRpm = end };
            }

            problem = $"Enter a start below the end, up to {NumberFormat.Format(MaxRpm, 0)} rpm.";
            return null;
        }

        if (spec.Kind == PerfTestKind.RollOn)
        {
            if (ResultText.Parse(StartKmh) is { } start && ResultText.Parse(EndKmh) is { } end && start > 0 && end <= MaxKmh && start < end)
            {
                return spec with { StartKmh = start, EndKmh = end };
            }

            problem = $"Enter a start below the end, up to {NumberFormat.Format(MaxKmh, 0)} km/h.";
            return null;
        }

        return spec;
    }

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
