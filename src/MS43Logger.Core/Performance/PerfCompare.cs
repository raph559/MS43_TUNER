using System.Globalization;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Performance;

/// <summary>One figure of both runs; <see cref="Delta"/> is A − B (with the newer run as A, what the change did), null when either is missing or they are not the same quantity.</summary>
public sealed record FigureDelta(string Key, string Label, double? A, double? B, double? Delta, string Unit, int Decimals);

/// <summary>What differs between the runs besides the tune: "test", "gear", "intake", "pressure" or "coolant".</summary>
public sealed record Mismatch(string Code, string Message);

/// <summary>
/// Two results recomputed with the same inputs (performance spec 9.2). <see cref="SharedFrom"/> and <see cref="SharedTo"/> are
/// the rpm band (km/h for roll-ons) both runs share, null when they share none. <see cref="TableDifferences"/> are the XDF items
/// that differ between the two calibration snapshots. <see cref="Recomputed"/> says the stored figures differ from the ones
/// recomputed now; <see cref="DefinitionChanged"/> says a run is decoded with another logging definition than it was saved
/// with, so a difference can come from that rather than from the car values. <see cref="Noise"/> is the run-to-run spread of
/// the headline figure, null under three runs. <see cref="SameTune"/> is both runs on one saved image, a note rather than a
/// mismatch. <see cref="TablesNote"/> says why no table is listed when the open XDF is not the one the runs were saved with.
/// </summary>
public sealed record PerfComparison(RunFigures A, RunFigures B, double? SharedFrom, double? SharedTo,
    IReadOnlyList<FigureDelta> Deltas, IReadOnlyList<Mismatch> Mismatches, IReadOnlyList<ItemDifference> TableDifferences,
    bool Recomputed, double? Noise, bool SameTune = false, bool DefinitionChanged = false, string? TablesNote = null);

public static class PerfCompare
{
    // Spec 7: no DIN or SAE correction, so conditions this far apart are named instead.
    private const double IntakeApartC = 5;
    private const double PressureApartKpa = 2.1;
    private const double CoolantApartC = 8;
    private const double RecomputedShare = 0.005;
    private const int NoiseRuns = 3;
    private const double Tolerance = 1e-9;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    // Flags only the run could see (PerfRun): why it ended, the warm-up state while it ran, a late full throttle.
    private static readonly string[] EndCodes = ["incomplete", "link-lost"];
    private static readonly string[] RunCodes = ["cold", "oil-cold", "oil-unreported", "late-throttle"];
    private static readonly Dictionary<string, AdxDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recomputes both from their samples with <paramref name="current"/> (spec 9.2), so other car values or another speed
    /// calibration never show up as a tune gain, over the band both runs share: a pull or roll-on is cut at the shared end, so
    /// nothing above it counts. <paramref name="xdf"/> reads the tables that differ between the snapshots when it is the XDF
    /// the runs were saved with (none without it);
    /// <paramref name="sameTune"/> are the saved results the noise is taken from. Throws <see cref="InvalidDataException"/>
    /// when a or b cannot be decoded again.
    /// </summary>
    public static PerfComparison Compare(PerfResult a, PerfResult b, AnalysisInputs current, XdfDefinition? xdf, IReadOnlyList<PerfResult>? sameTune = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(current);
        var (from, to) = Shared(a.Spec, b.Spec);
        var framesA = Frames(a);
        var framesB = Frames(b);
        var figuresA = Recompute(a, framesA, from, to, current);
        var figuresB = Recompute(b, framesB, from, to, current);
        // Against the stored figures only the inputs may differ, so each is recomputed over its own band for this.
        var recomputed = Differs(a, Own(a, framesA, from, to, current, figuresA)) || Differs(b, Own(b, framesB, from, to, current, figuresB));
        var (tables, tablesNote) = Tables(a, b, xdf);
        return new PerfComparison(figuresA, figuresB, from, to, Deltas(figuresA, figuresB), Mismatches(a, b), tables,
            recomputed, sameTune is null ? null : SameTuneNoise(a, sameTune, from, to, current), SameTune(a, b),
            DefinitionChanged(a) || DefinitionChanged(b), tablesNote);
    }

    /// <summary>
    /// The result's figures recomputed from its samples with <paramref name="inputs"/> over its own band, the flags only the run
    /// could see carried over: what it reads with these car values and this speed calibration (spec 9.2). Throws
    /// <see cref="InvalidDataException"/> when it cannot be decoded again.
    /// </summary>
    public static RunFigures Recompute(PerfResult result, AnalysisInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inputs);
        return Recompute(result, Frames(result), null, null, inputs);
    }

    /// <summary>
    /// Frames rebuilt from a stored result: its payloads decoded with <see cref="BundledDefinitions.Load"/> of its variant, at
    /// its sample times. Throws <see cref="InvalidDataException"/> when they cannot be.
    /// </summary>
    public static IReadOnlyList<PerfFrame> Frames(PerfResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.SampleAt.Length != result.Payloads.Length)
        {
            throw new InvalidDataException($"{result.Name}: {result.SampleAt.Length} sample times for {result.Payloads.Length} samples.");
        }

        // Retries are kept only by results saved since they were counted, one per sample.
        var retries = result.SampleRetries is { } counts && counts.Length == result.Payloads.Length ? counts : null;
        try
        {
            var definition = Definition(result.Variant);
            var frames = new PerfFrame[result.Payloads.Length];
            for (var i = 0; i < frames.Length; i++)
            {
                var payload = Convert.FromBase64String(result.Payloads[i]);
                // Whole ticks, so a stored time comes back as the run's own.
                var at = TimeSpan.FromTicks((long)Math.Round(result.SampleAt[i] * TimeSpan.TicksPerSecond));
                frames[i] = PerfChannels.Read(definition, definition.DecodePayload(payload), payload, at, retries?[i]);
            }

            return frames;
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"{result.Name}: its samples cannot be decoded again ({error.Message.Trim().TrimEnd('.')}).", error);
        }
    }

    /// <summary>
    /// Sample standard deviation of <paramref name="figureKey"/> as stored, over the results that share the first one's test,
    /// band, gear and saved-image SHA-256 and are not cold (spec 9.2); null under three.
    /// </summary>
    public static double? Noise(IReadOnlyList<PerfResult> results, string figureKey)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(figureKey);
        var first = results.FirstOrDefault(result => !Cold(result));
        return first is null
            ? null
            : Spread(results.Where(result => result.Spec == first.Spec && SameTune(first, result) && !Cold(result))
                .Select(result => Value(result.Figures, figureKey)));
    }

    /// <summary>The band two specs share: rpm for pulls and knock checks, km/h for roll-ons; none for other tests or other kinds.</summary>
    private static (double? From, double? To) Shared(PerfTestSpec a, PerfTestSpec b)
    {
        if (a.Kind != b.Kind)
        {
            return (null, null);
        }

        var (startA, endA, startB, endB) = a.Kind switch
        {
            PerfTestKind.Pull or PerfTestKind.KnockCheck => (a.StartRpm, a.EndRpm, b.StartRpm, b.EndRpm),
            PerfTestKind.RollOn => (a.StartKmh, a.EndKmh, b.StartKmh, b.EndKmh),
            _ => (null, null, null, null),
        };
        if (startA is not { } sa || endA is not { } ea || startB is not { } sb || endB is not { } eb)
        {
            return (null, null);
        }

        var from = Math.Max(sa, sb);
        var to = Math.Min(ea, eb);
        return from < to ? (from, to) : (null, null);
    }

    /// <summary>
    /// The result's figures with <paramref name="inputs"/> over the band from to (its own without one), with the flags only the
    /// run could see carried over from the stored figures.
    /// </summary>
    private static RunFigures Recompute(PerfResult result, IReadOnlyList<PerfFrame> frames, double? from, double? to, AnalysisInputs inputs)
    {
        var spec = result.Spec;
        var reached = false;
        if (from is { } start && to is { } end)
        {
            // A roll-on is cut on the speed it times, a pull on rpm.
            Func<PerfFrame, double?> measure;
            if (spec.Kind == PerfTestKind.RollOn)
            {
                spec = spec with { StartKmh = start, EndKmh = end };
                measure = RunAnalysis.RollOnSpeed(spec.Gear, inputs);
            }
            else
            {
                spec = spec with { StartRpm = start, EndRpm = end };
                measure = frame => frame.Rpm;
            }

            // Up to the first sample at the shared end, so the end crossing is still between two samples.
            var cut = -1;
            for (var i = Math.Max(result.WindowStart, 0); i < frames.Count; i++)
            {
                if (measure(frames[i]) >= end)
                {
                    cut = i;
                    break;
                }
            }

            if (cut >= 0)
            {
                frames = frames.Take(cut + 1).ToArray();
                reached = true;
            }
        }

        // A run that reached the shared end is complete over it, whatever ended it later.
        var complete = result.Complete || reached;
        var figures = RunAnalysis.Analyse(spec, frames, inputs, complete);
        var carried = result.Figures.Flags.Where(flag => RunCodes.Contains(flag.Code) || (!complete && EndCodes.Contains(flag.Code))).ToArray();
        // The run's own reason replaces the analysis' "Incomplete: ended at …".
        var replaced = carried.Any(flag => EndCodes.Contains(flag.Code));
        var own = figures.Flags.Where(flag => !(replaced && flag.Code == "incomplete") && carried.All(c => c.Code != flag.Code));
        return figures with { Flags = RunAnalysis.Ordered(own.Concat(carried)) };
    }

    // Over its own band: the shared recompute itself when the band is the result's own.
    private static RunFigures Own(PerfResult result, IReadOnlyList<PerfFrame> frames, double? from, double? to, AnalysisInputs inputs, RunFigures shared)
    {
        var ownBand = result.Spec.Kind == PerfTestKind.RollOn
            ? (result.Spec.StartKmh, result.Spec.EndKmh)
            : (result.Spec.StartRpm, result.Spec.EndRpm);
        return from is null || ownBand == (from, to) ? shared : Recompute(result, frames, null, null, inputs);
    }

    // A headline figure more than 0.5 % away from the stored one, or there on one side only.
    private static bool Differs(PerfResult stored, RunFigures recomputed) =>
        HeadlineKeys(stored.Spec.Kind).Any(key => (Value(stored.Figures, key), Value(recomputed, key)) switch
        {
            (null, null) => false,
            ({ } s, { } r) => Math.Abs(r - s) > RecomputedShare * Math.Abs(s) + Tolerance,
            _ => true,
        });

    /// <summary>The figures a test's headline is made of, the first being the one its noise is taken from.</summary>
    private static string[] HeadlineKeys(PerfTestKind kind) => kind switch
    {
        PerfTestKind.Pull => ["time", "peak-power"],
        PerfTestKind.KnockCheck => ["knock-max"],
        PerfTestKind.RollOn => ["time"],
        PerfTestKind.StandingStart => ["t-0-100", "t-0-50"],
        PerfTestKind.LinkTest => ["rate"],
        _ => [],
    };

    // A's figures in order, then any only B has; the cylinder number of the worst knock is no quantity to subtract.
    private static IReadOnlyList<FigureDelta> Deltas(RunFigures a, RunFigures b)
    {
        var deltas = new List<FigureDelta>();
        foreach (var figureA in a.Figures)
        {
            var figureB = b.Figures.FirstOrDefault(f => f.Key == figureA.Key);
            var delta = figureA.Value is { } x && figureB?.Value is { } y && figureA.Label == figureB.Label && figureA.Key != "knock-cyl"
                ? x - y
                : (double?)null;
            deltas.Add(new FigureDelta(figureA.Key, figureA.Label, figureA.Value, figureB?.Value, delta, figureA.Unit, figureA.Decimals));
        }

        deltas.AddRange(b.Figures
            .Where(figureB => a.Figures.All(f => f.Key != figureB.Key))
            .Select(figureB => new FigureDelta(figureB.Key, figureB.Label, null, figureB.Value, null, figureB.Unit, figureB.Decimals)));
        return deltas;
    }

    // Two tests or two gears, then the conditions each run started with (spec 7); a missing reading is no mismatch. They are
    // named, not refused: the owner may mean to compare them.
    private static IReadOnlyList<Mismatch> Mismatches(PerfResult a, PerfResult b)
    {
        var mismatches = new List<Mismatch>();
        if (a.Spec.Kind != b.Spec.Kind)
        {
            mismatches.Add(new Mismatch("test", $"Different tests: {Lower(a.Spec.ShortName)} and {Lower(b.Spec.ShortName)}."));
        }
        else if (a.Spec.Gear is { } gearA && b.Spec.Gear is { } gearB && gearA != gearB)
        {
            mismatches.Add(new Mismatch("gear", $"Different gears: {RunAnalysis.Ordinal(gearA)} and {RunAnalysis.Ordinal(gearB)}."));
        }

        if (Apart(a.Start.IntakeC, b.Start.IntakeC) is { } intake && intake > IntakeApartC + Tolerance)
        {
            mismatches.Add(new Mismatch("intake", $"Intake air {Whole(a.Start.IntakeC)} and {Whole(b.Start.IntakeC)} °C: more than 5 °C apart."));
        }

        // 2.1 kPa is one step of the pressure sensor.
        if (Apart(a.Start.PressureKpa, b.Start.PressureKpa) is { } pressure && pressure >= PressureApartKpa - Tolerance)
        {
            mismatches.Add(new Mismatch("pressure",
                $"Pressure {Tenth(a.Start.PressureKpa)} and {Tenth(b.Start.PressureKpa)} kPa: 2.1 kPa or more apart."));
        }

        if (Apart(a.Start.CoolantC, b.Start.CoolantC) is { } coolant && coolant > CoolantApartC + Tolerance)
        {
            mismatches.Add(new Mismatch("coolant", $"Coolant {Whole(a.Start.CoolantC)} and {Whole(b.Start.CoolantC)} °C: more than 8 °C apart."));
        }

        return mismatches;
    }

    // "3rd pull", "knock check", "roll-on 60 to 100" inside a sentence.
    private static string Lower(string name) => name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    /// <summary>
    /// The XDF items that differ between the snapshots, read through <paramref name="xdf"/> only when it is the XDF both runs
    /// were saved with (by title), since another one would read other addresses. The note says why none is listed: the XDF
    /// to open (none open, or another one), two XDFs, or a snapshot that cannot be read; null when there is no snapshot to
    /// compare or the snapshots are equal.
    /// </summary>
    private static (IReadOnlyList<ItemDifference> Items, string? Note) Tables(PerfResult a, PerfResult b, XdfDefinition? xdf)
    {
        if (a.Tune?.Snapshot is not { Length: > 0 } snapshotA || b.Tune?.Snapshot is not { Length: > 0 } snapshotB)
        {
            return ([], null);
        }

        var (titleA, titleB) = (a.Tune.XdfTitle, b.Tune.XdfTitle);
        if (!string.Equals(titleA, titleB, StringComparison.Ordinal))
        {
            return ([], $"The runs were saved with different XDFs ({titleA} and {titleB}), so their tables are not compared.");
        }

        if (snapshotA == snapshotB)
        {
            return ([], null);
        }

        if (xdf is null || !string.Equals(titleA, xdf.Title, StringComparison.Ordinal))
        {
            return ([], $"Open the XDF these runs were saved with ({titleA}) on Tune to list the tables that differ.");
        }

        try
        {
            // A − B, as in Compare: with the newer run as A it is what the change did.
            return (BinComparer.Compare(xdf, TuneIdentity.Restore(snapshotA), TuneIdentity.Restore(snapshotB)).Items, null);
        }
        catch (InvalidDataException)
        {
            // A damaged snapshot only costs the table list, and says so rather than reading as no change.
            return ([], "A run's calibration snapshot cannot be read, so the tables are not compared.");
        }
    }

    // The definition the payloads decode with now is not the one the result was saved with (spec 8: its file and SHA-256).
    private static bool DefinitionChanged(PerfResult result)
    {
        try
        {
            return !string.Equals(result.DefinitionSha256, BundledDefinitions.Sha256(result.Variant), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // An unknown variant cannot be decoded at all; Frames says so.
            return true;
        }
    }

    // The spread over the shared band of A's test on the same tune, recomputed as A and B are (spec 9.2).
    private static double? SameTuneNoise(PerfResult a, IReadOnlyList<PerfResult> sameTune, double? from, double? to, AnalysisInputs current)
    {
        var key = HeadlineKeys(a.Spec.Kind).FirstOrDefault();
        var candidates = sameTune.Where(result => result.Spec.Kind == a.Spec.Kind && result.Spec.Gear == a.Spec.Gear && !Cold(result)).ToArray();
        if (key is null || candidates.Length == 0)
        {
            return null;
        }

        var first = candidates[0];
        return Spread(candidates.Where(result => SameTune(first, result)).Select(result =>
        {
            try
            {
                return Value(Recompute(result, Frames(result), from, to, current), key);
            }
            catch (InvalidDataException)
            {
                // One result that cannot be decoded is left out, not the comparison.
                return null;
            }
        }));
    }

    private static double? Spread(IEnumerable<double?> values)
    {
        var known = values.Where(value => value is { } x && double.IsFinite(x)).Select(value => value!.Value).ToArray();
        if (known.Length < NoiseRuns)
        {
            return null;
        }

        var mean = known.Average();
        return Math.Sqrt(known.Sum(x => (x - mean) * (x - mean)) / (known.Length - 1));
    }

    private static bool SameTune(PerfResult a, PerfResult b) =>
        a.Tune?.SavedSha256 is { Length: > 0 } hash && string.Equals(hash, b.Tune?.SavedSha256, StringComparison.OrdinalIgnoreCase);

    // Cold runs are not comparable (spec 5).
    private static bool Cold(PerfResult result) => result.Figures.Flags.Any(flag => flag.Severity == FlagSeverity.Cold);

    private static double? Value(RunFigures figures, string key) => figures.Figures.FirstOrDefault(f => f.Key == key)?.Value;

    private static double? Apart(double? a, double? b) =>
        a is { } x && b is { } y && double.IsFinite(x) && double.IsFinite(y) ? Math.Abs(x - y) : null;

    private static string Whole(double? value) => value!.Value.ToString("0", Invariant);

    private static string Tenth(double? value) => value!.Value.ToString("0.0", Invariant);

    private static AdxDefinition Definition(string variant)
    {
        lock (Definitions)
        {
            if (!Definitions.TryGetValue(variant ?? "", out var definition))
            {
                definition = BundledDefinitions.Load(variant!);
                Definitions[variant!] = definition;
            }

            return definition;
        }
    }
}
