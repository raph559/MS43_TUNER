using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core;
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp;

/// <summary>One line of the Performance rail: a group title or a test.</summary>
public abstract class PerformanceEntry : ViewModelBase
{
}

public sealed class PerformanceGroup(string title) : PerformanceEntry
{
    public string Title { get; } = title;
}

/// <summary>
/// A test of the rail (performance spec 9.1) with its one-line summary and, when the definition lacks a channel it needs, why
/// it is off. <see cref="Kind"/> is null for Car, which holds values and runs nothing.
/// </summary>
public sealed class PerformanceTestEntry(string key, string title, string summary, PerfTestKind? kind) : PerformanceEntry
{
    private string? _offText;
    private bool _isCurrent;

    /// <summary>"pull", "knock", "rollon", "standing", "calibration", "car", "link" or "pedal".</summary>
    public string Key { get; } = key;

    public string Title { get; } = title;

    public string Summary { get; } = summary;

    public PerfTestKind? Kind { get; } = kind;

    /// <summary>"This definition does not log knock per cylinder."; null when the test can run here.</summary>
    public string? OffText
    {
        get => _offText;
        internal set
        {
            if (SetField(ref _offText, value))
            {
                OnPropertyChanged(nameof(IsOff));
            }
        }
    }

    public bool IsOff => OffText is not null;

    public bool IsCurrent { get => _isCurrent; internal set => SetField(ref _isCurrent, value); }
}

/// <summary>
/// The Performance tab (performance spec 9.1): the tests on a rail grouped Pulls, Acceleration and Setup, the chosen test's
/// Run side and the Results library. It reads the samples Live already receives (<see cref="Push"/>, from
/// <see cref="MainWindowViewModel.SampleShown"/>) and never asks the car for one of its own; a run goes on while another tab
/// is shown.
/// </summary>
public sealed class PerformanceViewModel : ViewModelBase, ISidePanel
{
    // A reading above the calibrated full pedal raises it once it has held this long (performance spec 4.4); the settings keep
    // 10 to 130.
    private static readonly TimeSpan PedalHold = TimeSpan.FromSeconds(0.5);
    private const double MaxFullPedal = 130;

    private readonly MainWindowViewModel _live;
    private readonly TuneViewModel _tune;
    private readonly Dictionary<string, AdxDefinition?> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceTestEntry _selected;
    private bool _isResultsMode;
    private bool _isVisible;
    private TimeSpan? _higherPedalSince;
    private double _higherPedalLow;

    public IReadOnlyList<PerformanceEntry> Entries { get; }

    public bool IsRunMode
    {
        get => !_isResultsMode;
        set
        {
            if (value)
            {
                SetResultsMode(false);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsResultsMode
    {
        get => _isResultsMode;
        set
        {
            if (value)
            {
                SetResultsMode(true);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public PerformanceRunViewModel Run { get; }

    public PerformanceResultsViewModel Results { get; }

    /// <summary>Asks before a result is deleted ("Delete the result "Baseline"?"); a null asker or false keeps the file.</summary>
    public Func<string, Task<bool>>? AskToDelete { get; set; }

    /// <summary>Live's warm-up banners, shown at the top of the pane too (performance spec 5).</summary>
    public ObservableCollection<WarmUpBannerViewModel> WarmUp => _live.WarmUpBanners;

    /// <summary>The Performance rail's width as dragged, or null for the layout's own.</summary>
    public double? RailWidth => _tune.Settings.PerformanceRailWidth;

    /// <summary>True while the Performance rail is folded to its strip (Ctrl B). Remembered between runs.</summary>
    public bool RailCollapsed => _tune.Settings.PerformanceRailCollapsed;

    public void SetRailWidth(double? width, bool save)
    {
        _tune.Settings.PerformanceRailWidth = width is double wanted ? Math.Clamp(Math.Round(wanted), SidePanel.MinWidth, SidePanel.MaxWidth) : null;
        if (save)
        {
            _tune.Settings.Save();
        }

        OnPropertyChanged(nameof(RailWidth));
    }

    public void SetRailCollapsed(bool collapsed)
    {
        if (_tune.Settings.PerformanceRailCollapsed == collapsed)
        {
            return;
        }

        _tune.Settings.PerformanceRailCollapsed = collapsed;
        _tune.Settings.Save();
        OnPropertyChanged(nameof(RailCollapsed));
    }

    /// <summary>
    /// One sample as Live showed it, with the frame Live's warm-up monitor read from it (<see cref="MainWindowViewModel.LastFrame"/>,
    /// so each sample is decoded once); <paramref name="definition"/> decoded it.
    /// </summary>
    public void Push(LiveSample sample, PerfFrame? frame, AdxDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (frame is null || definition is null)
        {
            return;
        }

        RaiseFullPedal(frame);
        Run.Push(frame, sample, definition);
    }

    // A reading above the calibration raises it once readings above it have held for 0.5 s, to the lowest of them, so one
    // glitch never lowers every later pedal share (performance spec 4.4; minor review, findings 17 and 24).
    private void RaiseFullPedal(PerfFrame frame)
    {
        if (Settings.FullPedal is not { } full || frame.Pedal is not { } pedal || !(pedal > full) || pedal > MaxFullPedal)
        {
            _higherPedalSince = null;
            return;
        }

        if (_higherPedalSince is not { } since || frame.At < since)
        {
            _higherPedalSince = frame.At;
            _higherPedalLow = pedal;
            return;
        }

        _higherPedalLow = Math.Min(_higherPedalLow, pedal);
        if (frame.At - since >= PedalHold)
        {
            Settings.FullPedal = _higherPedalLow;
            Settings.Save();
            _higherPedalSince = null;
            Run.FullPedalChanged();
        }
    }

    internal MainWindowViewModel Live => _live;

    internal TuneViewModel Tune => _tune;

    internal AppSettings Settings => _tune.Settings;

    internal PerfResultStore Store { get; }

    internal TimeProvider Time => _live.Time;

    /// <summary>What the samples are decoded with while connected; otherwise the chosen variant's definition, null if unreadable.</summary>
    internal AdxDefinition? Definition => _live.IsConnected && _live.CurrentDefinition is { } current ? current : Bundled(_live.SelectedVariant);

    /// <summary>The car values, speed calibration and full-pedal value as they are now (spec 7, 6.5, 4.4).</summary>
    internal AnalysisInputs CurrentInputs() => new(
        new CarValues(Settings.TestMassKg, Settings.TyreSize, Settings.Automatic),
        new Dictionary<int, double>(Settings.KmhPerRpm),
        Settings.FullPedal ?? 0,
        new Dictionary<int, double>(Settings.SpeedometerKmhPerRpm),
        Settings.GpsChecked.Where(pair => pair.Value).Select(pair => pair.Key).Order().ToArray());

    private void SetResultsMode(bool results)
    {
        if (SetField(ref _isResultsMode, results, nameof(IsResultsMode)))
        {
            OnPropertyChanged(nameof(IsRunMode));
        }
    }

    private AdxDefinition? Bundled(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
        {
            return null;
        }

        if (!_definitions.TryGetValue(variant, out var definition))
        {
            try
            {
                definition = BundledDefinitions.Load(variant);
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException)
            {
                definition = null;
            }

            _definitions[variant] = definition;
        }

        return definition;
    }
}

/// <summary>
/// The run clock of the performance tests (performance spec 4.2): a sample's request time on the logger's monotonic clock,
/// or, for a sample without one, its arrival time since the first such sample. Never the clock at arrival.
/// </summary>
internal sealed class RunClock
{
    private DateTimeOffset? _first;

    public TimeSpan At(LiveSample sample)
    {
        if (sample.RequestedAt != TimeSpan.Zero)
        {
            return sample.RequestedAt;
        }

        _first ??= sample.Time;
        return sample.Time - _first.Value;
    }

    public void Reset() => _first = null;
}

/// <summary>
/// A computed number as the result view shows it: "6.84" with "s"; companions such as the rpm of peak power go in
/// <see cref="Detail"/>. <see cref="Against"/> names the baseline the delta was taken against.
/// </summary>
public sealed record FigureRow(string Key, string Label, string Value, string Unit, string? Detail, string? Delta, string? Against = null)
{
    /// <summary>The run did not reach it: the reading is "n/a" in UI text (system 3.3).</summary>
    public bool IsMissing => Value == "n/a";

    /// <summary>"-0.13 s against 3rd pull 14:05"; null without a baseline figure.</summary>
    public string? DeltaText => Delta is null ? null : $"{Delta}{(Unit.Length > 0 ? " " + Unit : "")} against {Against ?? "the baseline"}";
}

/// <summary>A run's flag with its severity, for the icon and colour beside its message (a flag is never colour alone).</summary>
public sealed record FlagRow(string Code, string Message, FlagSeverity Severity)
{
    public bool IsInvalid => Severity == FlagSeverity.Invalid;

    public bool IsCold => Severity == FlagSeverity.Cold;

    public bool IsSuspect => Severity == FlagSeverity.Suspect;

    public bool IsInfo => Severity == FlagSeverity.Info;

    /// <summary>Invalid and cold runs take the red stripe and icon; suspect ones the amber (performance spec 5).</summary>
    public bool IsWarn => Severity is FlagSeverity.Invalid or FlagSeverity.Cold;
}

/// <summary>The words and numbers the run and the results views share (system 3.3: invariant, no grouping, n/a when missing).</summary>
internal static class ResultText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Figures shown beside another one, not as rows of their own.
    private static readonly Dictionary<string, (string Of, string Prefix)> Companions = new(StringComparer.Ordinal)
    {
        ["peak-power-rpm"] = ("peak-power", "at "),
        ["peak-torque-rpm"] = ("peak-torque", "at "),
        ["knock-cyl"] = ("knock-max", "on cylinder "),
    };

    /// <summary>
    /// The figures as rows, with the deltas against <paramref name="baseline"/> where it has the same figure, said against
    /// <paramref name="baselineName"/>.
    /// </summary>
    public static IReadOnlyList<FigureRow> Figures(RunFigures figures, RunFigures? baseline = null, string? baselineName = null)
    {
        var rows = new List<FigureRow>();
        // A knock check's six cylinders are drawn as the knock bars, not listed (KnockByCylinder).
        foreach (var figure in figures.Figures.Where(figure => !Companions.ContainsKey(figure.Key) && Cylinder(figure.Key) is null))
        {
            var detail = CompanionOf(figures, figure.Key);
            var before = baseline?.Figures.FirstOrDefault(other => other.Key == figure.Key && other.Label == figure.Label);
            var delta = figure.Value is { } now && before?.Value is { } then ? Signed(now - then, figure.Decimals) : null;
            rows.Add(new FigureRow(figure.Key, figure.Label, NumberFormat.Format(figure.Value, figure.Decimals), figure.Unit, detail, delta, baselineName));
        }

        return rows;
    }

    /// <summary>A figure shown beside another one ("peak-power-rpm" beside "peak-power"), never as a row of its own.</summary>
    public static bool IsCompanion(string key) => Companions.ContainsKey(key);

    /// <summary>"at 5950 rpm" or "on cylinder 4": what goes beside the figure <paramref name="key"/>; null without one.</summary>
    public static string? CompanionOf(RunFigures figures, string key)
    {
        foreach (var (companionKey, (of, prefix)) in Companions)
        {
            if (of == key && figures.Figures.FirstOrDefault(figure => figure.Key == companionKey) is { Value: { } value } companion)
            {
                return prefix + NumberFormat.Format(value, companion.Decimals) + (companion.Unit.Length > 0 ? " " + companion.Unit : "");
            }
        }

        return null;
    }

    /// <summary>
    /// Each cylinder's worst knock retard ("knock-max-1" to "knock-max-6", the knock check's), for the six bars of a stored
    /// result (performance spec 6.2, 9.2); null when the run has none.
    /// </summary>
    public static IReadOnlyList<double?>? KnockByCylinder(RunFigures figures)
    {
        var cylinders = new double?[6];
        var any = false;
        foreach (var figure in figures.Figures)
        {
            if (Cylinder(figure.Key) is { } cylinder)
            {
                cylinders[cylinder - 1] = figure.Value;
                any = true;
            }
        }

        return any ? cylinders : null;
    }

    // 1 to 6 for "knock-max-1" to "knock-max-6"; null for any other key ("knock-max" is the run's worst).
    private static int? Cylinder(string key) =>
        key.Length == "knock-max-1".Length && key.StartsWith("knock-max-", StringComparison.Ordinal) && key[^1] is >= '1' and <= '6' ? key[^1] - '0' : null;

    /// <summary>
    /// "Knock retard: cylinder 4 at 1.6 °CRK." naming every cylinder at or past <paramref name="from"/>, under the knock bars of
    /// a run and of a stored result alike (spec 9.2: never colour alone); null when none is.
    /// </summary>
    public static string? KnockLine(IReadOnlyList<double?> knock, double from)
    {
        var knocking = knock.Select((value, index) => (Value: value, Cylinder: index + 1))
            .Where(pair => pair.Value is { } retard && double.IsFinite(retard) && retard >= from)
            .Select(pair => $"cylinder {pair.Cylinder.ToString(Invariant)} at {NumberFormat.Format(pair.Value, 1)} °CRK")
            .ToArray();
        return knocking.Length > 0 ? $"Knock retard: {Join(knocking)}." : null;
    }

    public static IReadOnlyList<FlagRow> Flags(RunFigures figures) =>
        figures.Flags.Select(flag => new FlagRow(flag.Code, flag.Message, flag.Severity)).ToArray();

    /// <summary>"+3", "-0.13": the sign always shown, the ASCII minus (system 3.3).</summary>
    public static string Signed(double value, int decimals)
    {
        var text = NumberFormat.Format(value, decimals);
        return text.StartsWith('-') || text.TrimStart('0', '.').Length == 0 ? text : "+" + text;
    }

    /// <summary>
    /// "Conditions: intake air 38 °C · coolant 92 °C · oil 86 °C · pressure 101.9 kPa · battery 13.9 V · RON adaptation 0.00"
    /// (mockup), each named as the analysis names it.
    /// </summary>
    public static string ConditionsLine(Conditions start) =>
        "Conditions: " + string.Join(" · ",
            $"intake air {Unit(start.IntakeC, 0, "°C")}",
            $"coolant {Unit(start.CoolantC, 0, "°C")}",
            start.OilC is { } oil ? $"oil {Unit(oil, 0, "°C")}" : "oil not reported",
            $"pressure {Unit(start.PressureKpa, 1, "kPa")}",
            $"battery {Unit(start.BatteryV, 1, "V")}",
            $"RON adaptation {NumberFormat.Format(start.RonAdaptation, 2)}");

    /// <summary>"Tune: CAR.bin · 43X001 · saved, no edits", or "No bin open" (spec 8).</summary>
    public static string TuneLine(TuneIdentity? tune)
    {
        if (tune is null)
        {
            return "No bin open";
        }

        var (bin, firmware, edits) = TuneParts(tune);
        return $"Tune: {bin} · {firmware ?? "unknown firmware"} · {edits}";
    }

    /// <summary>The tune line's parts: the bin's file name, the firmware id (null when unknown) and the edits.</summary>
    public static (string Bin, string? Firmware, string Edits) TuneParts(TuneIdentity tune)
    {
        ArgumentNullException.ThrowIfNull(tune);
        var edits = !tune.HasUnsavedEdits
            ? "saved, no edits"
            : tune.ModifiedItems.Count == 1 ? "unsaved edits in 1 item" : $"unsaved edits in {tune.ModifiedItems.Count.ToString(Invariant)} items";
        return (tune.BinName ?? "a bin not saved yet", tune.SoftwareId, edits);
    }

    /// <summary>
    /// "Speed calibration: 3rd 22.3 km/h per 1000 rpm, speed not GPS-checked. Car: 1480 kg with driver, 205/55 R16." for a
    /// test in <paramref name="gear"/>; the car part alone without a gear. The GPS check is the gear's own (spec 6.5); inputs
    /// saved before it was kept say nothing of GPS.
    /// </summary>
    public static string InputsLine(AnalysisInputs inputs, int? gear)
    {
        var car = $"Car: {NumberFormat.Format(inputs.Car.MassKg, 0)} kg with driver, {inputs.Car.TyreSize}{(inputs.Car.Automatic ? ", automatic" : "")}.";
        if (gear is not { } g)
        {
            return car;
        }

        bool? gpsChecked = inputs.GpsCheckedGears is { } checkedGears ? checkedGears.Contains(g) : null;
        var speed = inputs.KmhPerRpm.TryGetValue(g, out var ratio)
            ? $"Speed calibration: {Ordinal(g)} {NumberFormat.Format(ratio * 1000, 1)} km/h per 1000 rpm{(gpsChecked == false ? ", speed not GPS-checked" : "")}."
            : $"No speed calibration for {Ordinal(g)}: speed is taken from the run.";
        return speed + " " + car;
    }

    /// <summary>"This definition does not log knock per cylinder."; null when <paramref name="definition"/> has every channel the test needs.</summary>
    public static string? MissingText(AdxDefinition? definition, PerfTestKind? kind) =>
        definition is not null && kind is { } test && PerfChannels.Missing(definition, test) is { Count: > 0 } missing
            ? $"This definition does not log {Join(missing)}."
            : null;

    /// <summary>"Pull, 3rd", "Knock check", "Roll-on, 4th", "Standing start" from a stored preset.</summary>
    public static string Test(PerfTestKind kind, string preset)
    {
        var gear = preset.LastIndexOf('-') is var dash and >= 0 && int.TryParse(preset[(dash + 1)..], NumberStyles.None, Invariant, out var g) ? g : (int?)null;
        return kind switch
        {
            PerfTestKind.Pull => gear is { } pull ? $"Pull, {Ordinal(pull)}" : "Pull",
            PerfTestKind.KnockCheck => "Knock check",
            PerfTestKind.RollOn => gear is { } roll ? $"Roll-on, {Ordinal(roll)}" : "Roll-on",
            PerfTestKind.StandingStart => "Standing start",
            PerfTestKind.SpeedCalibration => "Speed calibration",
            PerfTestKind.LinkTest => "Link test",
            PerfTestKind.Pedal => "Pedal",
            _ => kind.ToString(),
        };
    }

    /// <summary>"21 Oct 14:32" in the clock's own time zone.</summary>
    public static string Date(DateTimeOffset at, TimeProvider time) =>
        TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).ToString("d MMM HH:mm", Invariant);

    public static string Ordinal(int gear) => gear switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => gear.ToString(Invariant) + "th",
    };

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string Join(IReadOnlyList<string> words) => words.Count switch
    {
        0 => "",
        1 => words[0],
        _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
    };

    /// <summary>A number with its unit after a space, "92 °C", or "n/a".</summary>
    public static string Unit(double? value, int decimals, string unit) =>
        value is { } v && double.IsFinite(v) ? $"{NumberFormat.Format(v, decimals)} {unit}" : "n/a";

    /// <summary>A number as typed: invariant, a comma read as the decimal point; null when it is not one.</summary>
    public static double? Parse(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, Invariant, out var value) && double.IsFinite(value) ? value : null;
}
