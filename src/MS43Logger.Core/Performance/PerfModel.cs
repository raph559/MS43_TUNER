using System.Globalization;

namespace MS43Logger.Core.Performance;

public enum PerfTestKind
{
    Pull,
    KnockCheck,
    RollOn,
    StandingStart,
    SpeedCalibration,
    LinkTest,
    Pedal,
}

/// <summary>Info is a note; Suspect and Cold make a run doubtful or not comparable; Invalid means its figures cannot be used.</summary>
public enum FlagSeverity
{
    Info,
    Suspect,
    Invalid,
    Cold,
}

public enum RunStage
{
    Waiting,
    Running,
    Finished,
    Cancelled,
}

/// <summary>
/// One test with its options. <see cref="Preset"/> is a stable key ("pull-3", "pull-2", "pull-4", "knock-4", "rollon-3",
/// "rollon-4", "standing", "calibration", "link", "pedal") stored with a result; the bands are performance spec 6.1 to 6.4.
/// Rpm bands are for pulls and the knock check, speeds for roll-ons and standing starts.
/// </summary>
public sealed record PerfTestSpec(PerfTestKind Kind, string Preset, int? Gear,
    double? StartRpm = null, double? EndRpm = null, double? StartKmh = null, double? EndKmh = null)
{
    /// <summary>
    /// 2nd 1500 to 6400 rpm, 3rd 2000 to 6400, 4th 1500 to 4500 (spec 6.1); <paramref name="endRpm"/> replaces the end,
    /// as the open bin's rev limit does for 3rd.
    /// </summary>
    public static PerfTestSpec Pull(int gear, double? endRpm = null) => gear switch
    {
        2 => new PerfTestSpec(PerfTestKind.Pull, "pull-2", 2, 1500, endRpm ?? 6400),
        3 => new PerfTestSpec(PerfTestKind.Pull, "pull-3", 3, 2000, endRpm ?? 6400),
        4 => new PerfTestSpec(PerfTestKind.Pull, "pull-4", 4, 1500, endRpm ?? 4500),
        _ => throw new ArgumentOutOfRangeException(nameof(gear), gear, "Pulls are run in 2nd, 3rd or 4th gear."),
    };

    /// <summary>A 4th gear pull from 1500 to 4500 rpm: high load at low rpm, where knock shows first (spec 6.2).</summary>
    public static PerfTestSpec KnockCheck() => new(PerfTestKind.KnockCheck, "knock-4", 4, 1500, 4500);

    /// <summary>3rd 60 to 100 km/h, 4th 80 to 120 km/h (spec 6.3).</summary>
    public static PerfTestSpec RollOn(int gear) => gear switch
    {
        3 => new PerfTestSpec(PerfTestKind.RollOn, "rollon-3", 3, StartKmh: 60, EndKmh: 100),
        4 => new PerfTestSpec(PerfTestKind.RollOn, "rollon-4", 4, StartKmh: 80, EndKmh: 120),
        _ => throw new ArgumentOutOfRangeException(nameof(gear), gear, "Roll-ons are run in 3rd or 4th gear."),
    };

    /// <summary>0 to 200 km/h; one run gives every time it reaches (spec 6.4).</summary>
    public static PerfTestSpec StandingStart() => new(PerfTestKind.StandingStart, "standing", null, StartKmh: 0, EndKmh: 200);

    public static PerfTestSpec SpeedCalibration() => new(PerfTestKind.SpeedCalibration, "calibration", null);

    /// <summary>30 s of samples with the car parked or idling (spec 6.6).</summary>
    public static PerfTestSpec LinkTest() => new(PerfTestKind.LinkTest, "link", null);

    public static PerfTestSpec Pedal() => new(PerfTestKind.Pedal, "pedal", null);

    /// <summary>"3rd pull", "Knock check", "Roll-on 80 to 120", "Standing start", "Speed calibration", "Link test", "Pedal".</summary>
    public string ShortName => Kind switch
    {
        PerfTestKind.Pull => Gear is { } gear ? $"{Ordinal(gear)} pull" : "Pull",
        PerfTestKind.KnockCheck => "Knock check",
        PerfTestKind.RollOn => $"Roll-on {Number(StartKmh)} to {Number(EndKmh)}",
        PerfTestKind.StandingStart => "Standing start",
        PerfTestKind.SpeedCalibration => "Speed calibration",
        PerfTestKind.LinkTest => "Link test",
        PerfTestKind.Pedal => "Pedal",
        _ => Kind.ToString(),
    };

    private static string Ordinal(int gear) => gear switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => gear.ToString(CultureInfo.InvariantCulture) + "th",
    };

    private static string Number(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "n/a";
}

/// <summary>One computed number of a run; <see cref="Value"/> is null when the run did not reach it.</summary>
public sealed record Figure(string Key, string Label, double? Value, string Unit, int Decimals);

/// <summary>A curve of a run, for example power against rpm; <see cref="X"/> and <see cref="Y"/> have the same length.</summary>
public sealed record Trace(string Key, string Label, string XUnit, string YUnit, IReadOnlyList<double> X, IReadOnlyList<double> Y);

public sealed record RunFlag(string Code, FlagSeverity Severity, string Message);

/// <summary>Everything the analysis of one window gives; <see cref="Headline"/> is the one-line summary, for example "6.84 s, 118 kW".</summary>
public sealed record RunFigures(IReadOnlyList<Figure> Figures, IReadOnlyList<Trace> Traces, IReadOnlyList<RunFlag> Flags, string Headline);

/// <summary>One condition a test waits for before it starts, with its live reading in <see cref="Detail"/>.</summary>
public sealed record ReadyCheck(string Label, bool Met, string Detail);

/// <summary>
/// The values the power estimate and the speed tests use (spec 7, 6.5): test mass with driver, tyre size as "205/55 R16",
/// gearbox, wheel inertia for four wheels and engine inertia (kg·m²), drag area (m²) and rolling resistance.
/// </summary>
public sealed record CarValues(double MassKg, string TyreSize, bool Automatic,
    double WheelInertia = 4.5, double EngineInertia = 0.20, double CdA = 0.60, double Crr = 0.011);

/// <summary>
/// What an analysis depends on besides the samples: the car, the speed calibration (km/h per rpm by gear, empty until one is
/// made; true speed once corrected by a GPS speed) and the full-pedal value every pedal percentage is relative to (spec 4.4,
/// 4.5). <see cref="SpeedometerKmhPerRpm"/> is what the speedometer itself read per rpm when each gear was captured, before
/// any GPS correction: the slip check compares VS with it. Null, or a gear it lacks, means <see cref="KmhPerRpm"/> is the
/// speedometer's own. <see cref="GpsCheckedGears"/> are the calibrated gears whose speed was checked against a GPS speed
/// (spec 6.5), null in a result saved before it was kept.
/// </summary>
public sealed record AnalysisInputs(CarValues Car, IReadOnlyDictionary<int, double> KmhPerRpm, double FullPedal,
    IReadOnlyDictionary<int, double>? SpeedometerKmhPerRpm = null, IReadOnlyList<int>? GpsCheckedGears = null);

/// <summary>
/// Coolant, oil (null when not reported), intake air, pressure (kPa), battery, RON adaptation (0..1), long-term trims.
/// </summary>
public sealed record Conditions(double? CoolantC, double? OilC, double? IntakeC, double? PressureKpa, double? BatteryV,
    double? RonAdaptation, double? LongTermTrim1, double? LongTermTrim2);
