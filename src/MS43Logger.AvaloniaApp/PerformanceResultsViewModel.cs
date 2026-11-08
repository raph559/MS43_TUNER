using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>One saved result in the library: name, test, date, tune, headline and flag tags, and its tick.</summary>
public sealed class ResultRow : ViewModelBase
{
    private readonly Action<ResultRow> _picked;
    private bool _isPicked;

    internal ResultRow(PerfResultHeader header, string testText, string dateText, bool isCold, bool isPicked, Action<ResultRow> picked)
    {
        Header = header;
        TestText = testText;
        DateText = dateText;
        IsCold = isCold;
        _isPicked = isPicked;
        _picked = picked;
        // Doubtful, invalid and cold runs carry a tag; notes do not.
        FlagTags = header.Flags.Where(flag => flag.Severity != FlagSeverity.Info).Select(flag => flag.Code.Replace('-', ' ')).Distinct().ToArray();
    }

    public PerfResultHeader Header { get; }

    public string FileName => Header.FileName;

    public string Name => Header.Name;

    public string Description => Header.Description;

    /// <summary>"Pull, 3rd".</summary>
    public string TestText { get; }

    /// <summary>"21 Oct 14:32".</summary>
    public string DateText { get; }

    /// <summary>The bin's file name, or "No bin" when none was open.</summary>
    public string TuneText => Header.BinName ?? "No bin";

    /// <summary>"6.84 s, 118 kW".</summary>
    public string Headline => Header.Headline;

    /// <summary>"cold", "knock", "incomplete", "slow link" and the like.</summary>
    public IReadOnlyList<string> FlagTags { get; }

    /// <summary>The tags as one line, "cold, knock"; empty when the run has none.</summary>
    public string TagsText => string.Join(", ", FlagTags);

    public bool HasTags => FlagTags.Count > 0;

    /// <summary>A cold or invalid run: its tags take the red warning icon, other doubts the amber one.</summary>
    public bool HasSevereFlag => Header.Flags.Any(flag => flag.Severity is FlagSeverity.Invalid or FlagSeverity.Cold);

    /// <summary>Made on a cold engine: not comparable, hidden until "Show cold runs".</summary>
    public bool IsCold { get; }

    /// <summary>Made by a newer version of the app: it can be opened and compared, never rewritten.</summary>
    public bool IsReadOnly => Header.ReadOnly;

    /// <summary>The row's tick for Compare (a tick per row, not list multi-select).</summary>
    public bool IsPicked
    {
        get => _isPicked;
        set
        {
            if (SetField(ref _isPicked, value))
            {
                _picked(this);
            }
        }
    }

    internal bool Matches(string word) =>
        new[] { Name, Description, TestText, DateText, TuneText, Headline }.Any(text => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The trace the compare chart overlays.</summary>
public enum ComparisonMetric
{
    Power,
    Torque,
    Ignition,
    Knock,
    Vanos,
    Afr,
    Maf,
    Rpm,
}

/// <summary>
/// One figure of both runs: "6.84" and "6.97" s, "-0.13" (A − B); "n/a" where a run has none. <see cref="DetailA"/> and
/// <see cref="DetailB"/> are each run's companion, such as the rpm of its peak power.
/// </summary>
public sealed record DeltaRow(string Key, string Label, string A, string B, string Delta, string Unit, string? DetailA = null, string? DetailB = null)
{
    /// <summary>"A 6.97 s, B 6.84 s" under the difference; "A 322 kW at 6250 rpm, B 331 kW at 6000 rpm" for a peak.</summary>
    public string Pair => $"A {WithUnit(A)}{Beside(DetailA)}, B {WithUnit(B)}{Beside(DetailB)}";

    public bool IsMissing => Delta == "n/a";

    private string WithUnit(string value) => value == "n/a" || Unit.Length == 0 ? value : $"{value} {Unit}";

    private static string Beside(string? detail) => detail is null ? "" : " " + detail;
}

/// <summary>An XDF item that differs between the two runs' calibration snapshots, with its largest change.</summary>
public sealed record TableChangeRow(string Title, string Detail);

/// <summary>
/// Two results compared: both recomputed with the current car values and speed calibration over the
/// band they share, A (the newer) solid and B (the older) dashed, A − B for every figure, the conditions that differ and the tables
/// that changed between the two tunes.
/// </summary>
public sealed class PerformanceComparisonViewModel : ViewModelBase
{
    private readonly PerfComparison _comparison;
    private ComparisonMetric _metric;
    private int _version;

    internal PerformanceComparisonViewModel(PerfResult a, PerfResult b, PerfComparison comparison)
    {
        _comparison = comparison;
        LabelA = $"A: {a.Name}";
        LabelB = $"B: {b.Name}";
        _metric = a.Spec.Kind switch
        {
            PerfTestKind.Pull => ComparisonMetric.Power,
            PerfTestKind.KnockCheck => ComparisonMetric.Knock,
            _ => ComparisonMetric.Rpm,
        };

        // Two gears or two tests take different times for the same marks: those are named, not subtracted.
        // The rpm of a peak goes beside each run's peak, not in a row of its own.
        var timesApart = comparison.Mismatches.Any(mismatch => mismatch.Code is "gear" or "test");
        Deltas = comparison.Deltas.Where(delta => (delta.A is not null || delta.B is not null) && !ResultText.IsCompanion(delta.Key))
            .Select(delta => new DeltaRow(delta.Key, delta.Label,
                NumberFormat.Format(delta.A, delta.Decimals), NumberFormat.Format(delta.B, delta.Decimals),
                delta.Delta is { } change && !(timesApart && delta.Unit == "s") ? ResultText.Signed(change, delta.Decimals) : "n/a", delta.Unit,
                ResultText.CompanionOf(comparison.A, delta.Key), ResultText.CompanionOf(comparison.B, delta.Key))).ToArray();
        // Each run says why it is doubtful beside the deltas; a note such as "uncalibrated" holds for both alike, since both
        // are recomputed with the same inputs.
        FlagsA = ResultText.Flags(comparison.A).Where(flag => !flag.IsInfo).ToArray();
        FlagsB = ResultText.Flags(comparison.B).Where(flag => !flag.IsInfo).ToArray();
        Mismatches = comparison.Mismatches
            .Select(mismatch => mismatch.Code is "gear" or "test" ? mismatch.Message.TrimEnd('.') + "; times are not compared." : mismatch.Message)
            .ToArray();
        Tables = comparison.TableDifferences.Select(difference => new TableChangeRow(difference.Item.Title, Change(difference))).ToArray();

        var unit = a.Spec.Kind == PerfTestKind.RollOn ? "km/h" : "rpm";
        BandText = comparison is { SharedFrom: { } from, SharedTo: { } to }
            ? $"Compared over {NumberFormat.Format(from, 0)} to {NumberFormat.Format(to, 0)} {unit}, the band both runs share."
            : null;
        // An empty list always says why: one saved image, the comparison's reason (the XDF to open, two XDFs, a snapshot that
        // cannot be read), no bin open, or no change.
        TablesNote = Tables.Count > 0
            ? null
            : comparison.SameTune
                ? "Same tune in both runs, so no table differs."
                : comparison.TablesNote
                    ?? (a.Tune?.Snapshot is not { Length: > 0 } || b.Tune?.Snapshot is not { Length: > 0 }
                        ? "A run saved with no bin open has no calibration to compare."
                        : "No table differs between the two calibrations.");
        RecomputedNote = comparison.Recomputed
            ? "Recomputed with the current car values and speed calibration; the figures saved with the runs differ."
            : null;
        NoiseText = comparison.Noise is { } noise && Headline(a.Spec.Kind) is { } key
            && comparison.A.Figures.FirstOrDefault(figure => figure.Key == key) is { } headline
            ? $"Noise ±{NumberFormat.Format(noise, headline.Decimals)}{(headline.Unit.Length > 0 ? " " + headline.Unit : "")} over the saved runs of this tune."
            : null;
    }

    public ComparisonMetric Metric
    {
        get => _metric;
        set
        {
            if (!SetField(ref _metric, value))
            {
                return;
            }

            foreach (var name in new[]
                     {
                         nameof(IsPower), nameof(IsTorque), nameof(IsIgnition), nameof(IsKnock), nameof(IsVanos), nameof(IsAfr), nameof(IsMaf), nameof(IsRpm),
                         nameof(TraceA), nameof(TraceB), nameof(XUnits), nameof(YUnits), nameof(NoTraceText),
                     })
            {
                OnPropertyChanged(name);
            }

            Version++;
        }
    }

    public bool IsPower { get => Metric == ComparisonMetric.Power; set => Press(ComparisonMetric.Power, value); }

    public bool IsTorque { get => Metric == ComparisonMetric.Torque; set => Press(ComparisonMetric.Torque, value); }

    public bool IsIgnition { get => Metric == ComparisonMetric.Ignition; set => Press(ComparisonMetric.Ignition, value); }

    public bool IsKnock { get => Metric == ComparisonMetric.Knock; set => Press(ComparisonMetric.Knock, value); }

    public bool IsVanos { get => Metric == ComparisonMetric.Vanos; set => Press(ComparisonMetric.Vanos, value); }

    public bool IsAfr { get => Metric == ComparisonMetric.Afr; set => Press(ComparisonMetric.Afr, value); }

    public bool IsMaf { get => Metric == ComparisonMetric.Maf; set => Press(ComparisonMetric.Maf, value); }

    /// <summary>Rpm against seconds from the start crossing, the trace every test has.</summary>
    public bool IsRpm { get => Metric == ComparisonMetric.Rpm; set => Press(ComparisonMetric.Rpm, value); }

    public IReadOnlyList<Point> TraceA => Points(TraceOf(_comparison.A, Metric));

    /// <summary>
    /// Why a segment is off: "No airflow trace in either run."; null while either run has the trace, so the segment is on.
    /// </summary>
    public string? PowerTip => Tip(ComparisonMetric.Power);

    public string? TorqueTip => Tip(ComparisonMetric.Torque);

    public string? IgnitionTip => Tip(ComparisonMetric.Ignition);

    public string? KnockTip => Tip(ComparisonMetric.Knock);

    public string? VanosTip => Tip(ComparisonMetric.Vanos);

    public string? AfrTip => Tip(ComparisonMetric.Afr);

    public string? MafTip => Tip(ComparisonMetric.Maf);

    public string? RpmTip => Tip(ComparisonMetric.Rpm);

    public IReadOnlyList<Point> TraceB => Points(TraceOf(_comparison.B, Metric));

    public string LabelA { get; }

    public string LabelB { get; }

    public string XUnits => (TraceOf(_comparison.A, Metric) ?? TraceOf(_comparison.B, Metric))?.XUnit ?? (Metric == ComparisonMetric.Rpm ? "s" : "rpm");

    public string? YUnits => (TraceOf(_comparison.A, Metric) ?? TraceOf(_comparison.B, Metric))?.YUnit;

    public IReadOnlyList<DeltaRow> Deltas { get; }

    /// <summary>A's invalid, cold and suspect flags over the compared band, beside the deltas; notes stay in the result.</summary>
    public IReadOnlyList<FlagRow> FlagsA { get; }

    public IReadOnlyList<FlagRow> FlagsB { get; }

    public bool HasFlagsA => FlagsA.Count > 0;

    public bool HasFlagsB => FlagsB.Count > 0;

    /// <summary>"Intake air 31 and 38 °C: more than 5 °C apart." and the like (no correction, so mismatches are named).</summary>
    public IReadOnlyList<string> Mismatches { get; }

    public bool HasMismatches => Mismatches.Count > 0;

    public IReadOnlyList<TableChangeRow> Tables { get; }

    /// <summary>Why no table is listed; null while some are.</summary>
    public string? TablesNote { get; }

    /// <summary>"No power trace in either run." when the chosen trace is in neither result; null otherwise.</summary>
    public string? NoTraceText => Tip(Metric);

    private static string NameOf(ComparisonMetric metric) => metric switch
    {
        ComparisonMetric.Power => "power",
        ComparisonMetric.Torque => "torque",
        ComparisonMetric.Ignition => "ignition",
        ComparisonMetric.Knock => "knock",
        ComparisonMetric.Vanos => "VANOS",
        ComparisonMetric.Afr => "AFR target",
        ComparisonMetric.Maf => "airflow",
        _ => "engine speed",
    };

    /// <summary>"Noise ±0.04 s over the saved runs of this tune." with three or more such runs; null otherwise.</summary>
    public string? NoiseText { get; }

    /// <summary>Said when the stored figures were made with other car values or another calibration.</summary>
    public string? RecomputedNote { get; }

    /// <summary>"Compared over 2000 to 6400 rpm, the band both runs share."; null when the runs share none.</summary>
    public string? BandText { get; }

    public int Version { get => _version; private set => SetField(ref _version, value); }

    private static string KeyOf(ComparisonMetric metric) => metric switch
    {
        ComparisonMetric.Power => "power",
        ComparisonMetric.Torque => "torque",
        ComparisonMetric.Ignition => "ignition",
        ComparisonMetric.Knock => "knock",
        ComparisonMetric.Vanos => "vanos-in",
        ComparisonMetric.Afr => "afr",
        ComparisonMetric.Maf => "maf",
        _ => "rpm-time",
    };

    private void Press(ComparisonMetric metric, bool on)
    {
        if (on)
        {
            Metric = metric;
        }
        else
        {
            OnPropertyChanged(metric switch
            {
                ComparisonMetric.Power => nameof(IsPower),
                ComparisonMetric.Torque => nameof(IsTorque),
                ComparisonMetric.Ignition => nameof(IsIgnition),
                ComparisonMetric.Knock => nameof(IsKnock),
                ComparisonMetric.Vanos => nameof(IsVanos),
                ComparisonMetric.Afr => nameof(IsAfr),
                ComparisonMetric.Maf => nameof(IsMaf),
                _ => nameof(IsRpm),
            });
        }
    }

    private static Trace? TraceOf(RunFigures figures, ComparisonMetric metric) => figures.Traces.FirstOrDefault(trace => trace.Key == KeyOf(metric));

    private string? Tip(ComparisonMetric metric) =>
        Points(TraceOf(_comparison.A, metric)).Count > 0 || Points(TraceOf(_comparison.B, metric)).Count > 0 ? null : $"No {NameOf(metric)} trace in either run.";

    private static IReadOnlyList<Point> Points(Trace? trace) => trace is null
        ? []
        : trace.X.Zip(trace.Y, (x, y) => new Point(x, y)).Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).ToArray();

    // The figure the noise is taken of, as PerfCompare takes it.
    private static string? Headline(PerfTestKind kind) => kind switch
    {
        PerfTestKind.Pull or PerfTestKind.RollOn => "time",
        PerfTestKind.KnockCheck => "knock-max",
        PerfTestKind.StandingStart => "t-0-100",
        PerfTestKind.LinkTest => "rate",
        _ => null,
    };

    // As Compare's list reads a change: "largest change +61.1 °CRK" for a table, "4512 → 3520" for a value.
    private static string Change(ItemDifference difference)
    {
        if (difference.Item is XdfTable && difference.LargestChange is { } largest)
        {
            var units = CompareViewModel.UnitsOf(difference.Item);
            return $"largest change {ResultText.Signed(largest, 1)}{(units.Length > 0 ? " " + units : "")}";
        }

        // Was, now: the older run's tune (B) then the newer's (A).
        return $"{difference.ValueB ?? "n/a"} → {difference.ValueA ?? "n/a"}";
    }
}

/// <summary>A saved result opened from the library: its full result view, read-only except name, description and direction.</summary>
public sealed class OpenResultViewModel : ViewModelBase
{
    private readonly PerformanceResultsViewModel _owner;
    private string _name;
    private string _description;
    private string? _direction;
    // As the file holds them, so Back to the list knows what changed.
    private string _savedName;
    private string _savedDescription;
    private string? _savedDirection;
    private const double KnockWarnFrom = 1.5;

    internal OpenResultViewModel(PerformanceResultsViewModel owner, string fileName, PerfResult result, bool readOnly)
    {
        _owner = owner;
        FileName = fileName;
        IsReadOnly = readOnly;
        _name = _savedName = result.Name;
        _description = _savedDescription = result.Description;
        _direction = _savedDirection = result.Direction;
        // Titled with its name, the test and headline beside it.
        Headline = string.IsNullOrWhiteSpace(result.Figures.Headline) ? result.Spec.ShortName : $"{result.Spec.ShortName} · {result.Figures.Headline}";
        Figures = ResultText.Figures(result.Figures);
        Knock = ResultText.KnockByCylinder(result.Figures);
        KnockText = Knock is { } knock ? ResultText.KnockLine(knock, KnockWarnFrom) : null;
        Tune = result.Tune;
        Flags = ResultText.Flags(result.Figures);
        ConditionsText = ResultText.ConditionsLine(result.Start);
        TuneText = ResultText.TuneLine(result.Tune);
        InputsText = ResultText.InputsLine(result.Inputs, result.Spec.Gear);
    }

    public string FileName { get; }

    /// <summary>A newer version of the app made it: nothing can be changed.</summary>
    public bool IsReadOnly { get; }

    /// <summary>The result's name as its file holds it.</summary>
    public string Title => _savedName;

    /// <summary>"3rd pull · 5.50 s, 331 kW".</summary>
    public string Headline { get; }

    public IReadOnlyList<FigureRow> Figures { get; }

    /// <summary>Each cylinder's worst knock retard of a knock check, for the six bars; null for other tests.</summary>
    public IReadOnlyList<double?>? Knock { get; }

    public bool HasKnock => Knock is not null;

    /// <summary>The line under the bars naming each cylinder at or past 1.5 °CRK.</summary>
    public string? KnockText { get; }

    /// <summary>The tune it was made with, for the tune line's file name, firmware id and edits.</summary>
    public TuneIdentity? Tune { get; }

    public IReadOnlyList<FlagRow> Flags { get; }

    public string ConditionsText { get; }

    public string TuneText { get; }

    public string InputsText { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? ""))
            {
                OnChangesChanged();
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
                OnChangesChanged();
            }
        }
    }

    public string? Direction
    {
        get => _direction;
        set
        {
            if (SetField(ref _direction, value is "A" or "B" ? value : null))
            {
                OnPropertyChanged(nameof(IsDirectionA));
                OnPropertyChanged(nameof(IsDirectionB));
                OnChangesChanged();
            }
        }
    }

    /// <summary>The direction segment: pressing the lit one clears it.</summary>
    public bool IsDirectionA { get => Direction == "A"; set => PressDirection("A", value); }

    public bool IsDirectionB { get => Direction == "B"; set => PressDirection("B", value); }

    /// <summary>The name, description or direction differs from what the file holds: Save changes is on only then.</summary>
    public bool HasChanges => !IsReadOnly
        && (Name.Trim() != _savedName.Trim() || Description.Trim() != _savedDescription.Trim() || Direction != _savedDirection);

    /// <summary>What Save changes does, or why it is off (every disabled control says why).</summary>
    public string SaveTip => IsReadOnly
        ? "A newer version of the app made this result, so it cannot be changed."
        : HasChanges
            ? "Writes the name, description and direction into the result's file"
            : "Nothing to save: the name, description and direction are as saved.";

    /// <summary>Writes the name, description and direction into the same file; false with the library's Error set when it cannot.</summary>
    public bool SaveDetails()
    {
        if (IsReadOnly || !_owner.UpdateDetails(FileName, Name, Description, Direction))
        {
            return false;
        }

        (_savedName, _savedDescription, _savedDirection) = (Name.Trim(), Description.Trim(), Direction);
        OnPropertyChanged(nameof(Title));
        OnChangesChanged();
        return true;
    }

    private void OnChangesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(SaveTip));
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
}

/// <summary>
/// The Results side of the Performance tab: the saved results with search and "Show cold runs",
/// rename, description and delete with confirmation, a tick on two rows and Compare, and a result opened read-only.
/// </summary>
public sealed class PerformanceResultsViewModel : ViewModelBase
{
    private readonly PerformanceViewModel _owner;
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PerfResultHeader> _headers = [];
    private IReadOnlyList<string> _skipped = [];
    private string _searchText = "";
    private bool _showCold;
    private string? _error;
    private PerformanceComparisonViewModel? _comparison;
    private OpenResultViewModel? _opened;

    internal PerformanceResultsViewModel(PerformanceViewModel owner)
    {
        _owner = owner;
    }

    /// <summary>The results the search and the cold switch let through, newest first.</summary>
    public ObservableCollection<ResultRow> Rows { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? ""))
            {
                Filter();
            }
        }
    }

    /// <summary>Cold runs are hidden by default.</summary>
    public bool ShowCold
    {
        get => _showCold;
        set
        {
            if (SetField(ref _showCold, value))
            {
                Filter();
            }
        }
    }

    public int PickedCount => _picked.Count;

    /// <summary>What Compare waits for: "Tick two results to compare them." and the like.</summary>
    public string PickText => _picked.Count switch
    {
        0 => "Tick two results to compare them.",
        1 => "Tick one more to compare.",
        2 => "Two ticked: ready to compare.",
        _ => "Tick only two to compare.",
    };

    /// <summary>Exactly two rows ticked.</summary>
    public bool CanCompare => _picked.Count == 2;

    /// <summary>"perf-….json: not a result file" for each file the list left out.</summary>
    public IReadOnlyList<string> Skipped
    {
        get => _skipped;
        private set
        {
            if (SetField(ref _skipped, value))
            {
                OnPropertyChanged(nameof(HasSkipped));
            }
        }
    }

    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>"No saved results yet." or "No result matches "xyz"." while the list is empty; null otherwise.</summary>
    public string? EmptyText => Rows.Count > 0
        ? null
        : _headers.Count == 0 ? "No saved results yet." : !string.IsNullOrWhiteSpace(SearchText) ? $"No result matches \"{SearchText.Trim()}\"." : "Only cold runs are saved: turn on Show cold runs to see them.";

    /// <summary>"Not saved: …", "Not compared: …" and the like after an action failed; null after one succeeded.</summary>
    public string? Error { get => _error; private set => SetField(ref _error, value); }

    public PerformanceComparisonViewModel? Comparison
    {
        get => _comparison;
        private set
        {
            if (SetField(ref _comparison, value))
            {
                OnPropertyChanged(nameof(HasComparison));
                OnPropertyChanged(nameof(ShowsList));
            }
        }
    }

    public bool HasComparison => Comparison is not null;

    public OpenResultViewModel? Opened
    {
        get => _opened;
        private set
        {
            if (SetField(ref _opened, value))
            {
                OnPropertyChanged(nameof(HasOpened));
                OnPropertyChanged(nameof(ShowsList));
            }
        }
    }

    public bool HasOpened => Opened is not null;

    /// <summary>The list shows unless a result is open or two are compared; each of those has a way back to it.</summary>
    public bool ShowsList => !HasOpened && !HasComparison;

    /// <summary>The headers of the last listing, for the Run side's baselines.</summary>
    internal IReadOnlyList<PerfResultHeader> Headers => _headers;

    /// <summary>Reads the folder again; ticks stay on the files still there.</summary>
    public void Refresh()
    {
        _headers = _owner.Store.List();
        Skipped = _owner.Store.Skipped;
        Filter();
        _owner.Run.RefreshBaselines();
    }

    public bool Rename(ResultRow row, string name)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(name))
        {
            Error = "Not renamed: a result needs a name";
            return false;
        }

        return Update(row.FileName, result => (name.Trim(), result.Description, result.Direction));
    }

    public bool EditDescription(ResultRow row, string text)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Update(row.FileName, result => (result.Name, (text ?? "").Trim(), result.Direction));
    }

    /// <summary>Deletes the file once the user confirmed (the confirm dialog); false when they did not or it failed.</summary>
    public async Task<bool> DeleteAsync(ResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_owner.AskToDelete is not { } ask || !await ask($"Delete the result \"{row.Name}\"?"))
        {
            return false;
        }

        if (!_owner.Store.Delete(row.FileName))
        {
            Error = _owner.Store.LastError;
            return false;
        }

        Error = null;
        if (Opened?.FileName == row.FileName)
        {
            Opened = null;
        }

        Refresh();
        return true;
    }

    /// <summary>Compares the two ticked results, the newer as A and the older as B, so A − B is what the change did, as in Compare.</summary>
    public void Compare()
    {
        if (!CanCompare)
        {
            return;
        }

        var picked = Rows.Where(row => row.IsPicked).OrderBy(row => row.Header.Created).ThenBy(row => row.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (picked.Length != 2 || Load(picked[1].FileName) is not { } a || Load(picked[0].FileName) is not { } b)
        {
            return;
        }

        try
        {
            // PerfCompare reads the tables only through the XDF the runs were saved with, and names it otherwise.
            Comparison = new PerformanceComparisonViewModel(a, b, PerfCompare.Compare(a, b, _owner.CurrentInputs(), _owner.Tune.Document?.Xdf, SameTune(a)));
            Error = null;
        }
        catch (InvalidDataException error)
        {
            Comparison = null;
            Error = $"Not compared: {error.Message.Trim().TrimEnd('.')}";
        }
    }

    public void CloseComparison() => Comparison = null;

    /// <summary>Shows a saved result's full result view.</summary>
    public void Open(ResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Load(row.FileName) is { } result)
        {
            Opened = new OpenResultViewModel(this, row.FileName, result, row.IsReadOnly);
        }
    }

    /// <summary>
    /// Back to the list (or Ctrl K): what was typed into the result is written first, and one that cannot be written keeps the
    /// result open with why.
    /// </summary>
    public void CloseOpened()
    {
        if (Opened is { HasChanges: true } opened && !opened.SaveDetails())
        {
            return;
        }

        Opened = null;
    }

    internal bool UpdateDetails(string fileName, string name, string description, string? direction)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Error = "Not renamed: a result needs a name";
            return false;
        }

        return Update(fileName, _ => (name.Trim(), (description ?? "").Trim(), direction));
    }

    private bool Update(string fileName, Func<PerfResult, (string Name, string Description, string? Direction)> change)
    {
        if (Load(fileName) is not { } result)
        {
            return false;
        }

        var (name, description, direction) = change(result);
        if (!_owner.Store.Update(fileName, name, description, direction))
        {
            Error = _owner.Store.LastError;
            return false;
        }

        Error = null;
        Refresh();
        return true;
    }

    private PerfResult? Load(string fileName)
    {
        var result = _owner.Store.Load(fileName);
        Error = result is null ? _owner.Store.LastError : null;
        return result;
    }

    // The noise is the spread over the saved, warm runs of A's test and tune; PerfCompare takes the first one's tune.
    private IReadOnlyList<PerfResult>? SameTune(PerfResult a)
    {
        if (a.Tune?.SavedSha256 is not { } hash)
        {
            return null;
        }

        var others = _headers
            .Where(header => header.Id != a.Id && header.Preset == a.Spec.Preset && header.SavedSha256 == hash && !IsCold(header))
            .Select(header => _owner.Store.Load(header.FileName))
            .OfType<PerfResult>();
        return [a, .. others];
    }

    private void Filter()
    {
        var words = XdfCatalog.Words(SearchText);
        var rows = new List<ResultRow>();
        foreach (var header in _headers)
        {
            var cold = IsCold(header);
            if (cold && !ShowCold)
            {
                continue;
            }

            var row = new ResultRow(header, ResultText.Test(header.Kind, header.Preset), ResultText.Date(header.Created, _owner.Time), cold,
                _picked.Contains(header.FileName), OnPicked);
            // A ticked row stays in view whatever the search, so A and B can each be found by search.
            if (row.IsPicked || words.All(row.Matches))
            {
                rows.Add(row);
            }
        }

        // A row out of view (deleted, or hidden as cold) is never ticked, so Compare never takes one the user cannot see.
        _picked.IntersectWith(rows.Select(row => row.FileName));
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        OnPicksChanged();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnPicked(ResultRow row)
    {
        if (row.IsPicked)
        {
            _picked.Add(row.FileName);
        }
        else
        {
            _picked.Remove(row.FileName);
        }

        OnPicksChanged();
    }

    private void OnPicksChanged()
    {
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(PickText));
        OnPropertyChanged(nameof(CanCompare));
    }

    private static bool IsCold(PerfResultHeader header) => header.Flags.Any(flag => flag.Severity == FlagSeverity.Cold);
}
