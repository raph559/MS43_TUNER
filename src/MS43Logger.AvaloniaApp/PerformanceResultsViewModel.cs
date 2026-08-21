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

/// <summary>One saved result in the library (performance spec 9.1): name, test, date, tune, headline and flag tags, and its tick.</summary>
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

    /// <summary>Made on a cold engine: not comparable, hidden until "Show cold runs" (spec 5, 9.2).</summary>
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

/// <summary>The trace the compare chart overlays (performance spec 9.2).</summary>
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
/// One figure of both runs: "6.97" and "6.84" s, "-0.13" (B − A); "n/a" where a run has none. <see cref="DetailA"/> and
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
/// Two results compared (performance spec 9.2): both recomputed with the current car values and speed calibration over the
/// band they share, A (the earlier) solid and B dashed, B − A for every figure, the conditions that differ and the tables
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

        // Two gears or two tests take different times for the same marks: those are named, not subtracted (minor review,
        // finding 26). The rpm of a peak goes beside each run's peak, not in a row of its own (finding 69).
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
        // cannot be read), no bin open, or no change (minor review, findings 12, 13 and 25).
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
    /// Why a segment is off: "No airflow trace in either run."; null while either run has the trace, so the segment is on
    /// (minor review, finding 37).
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

    /// <summary>"Intake air 31 and 38 °C: more than 5 °C apart." and the like (spec 7: no correction, so mismatches are named).</summary>
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

    /// <summary>Said when the stored figures were made with other car values or another calibration (spec 9.2).</summary>
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

        return $"{difference.ValueA ?? "n/a"} → {difference.ValueB ?? "n/a"}";
    }
}
