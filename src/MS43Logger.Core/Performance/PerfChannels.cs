using System.Runtime.CompilerServices;

namespace MS43Logger.Core.Performance;

/// <summary>
/// One sample in the tests' terms, at <see cref="At"/> on the run clock. A channel or bit the sample
/// lacks is null, never zero. <see cref="Ignition"/> and <see cref="KnockRetard"/> have six entries (null where missing) or
/// are empty when the definition logs none; <see cref="KnockRetard"/> is positive degrees of retard. Pedal and throttle are
/// in the definition's degrees; a test makes them a percentage of the calibrated full-pedal value.
/// <see cref="TorqueIntervention"/> is DSC (ASC) acting on the engine torque. <see cref="InjectorDuty"/> is in
/// percent and <see cref="DmeTorque"/> the DME's own engine torque in Nm; <see cref="MalfunctionLamp"/> is the EML
/// light. <see cref="Retries"/> is how many requests the logger sent again for this sample, null when the source does not
/// say.
/// </summary>
public sealed record PerfFrame(TimeSpan At, double? Rpm, double? SpeedKmh, int? Gear, double? Pedal, double? Throttle,
    double? CoolantC, double? OilC, double? IntakeC, double? PressureKpa, double? BatteryV,
    IReadOnlyList<double?> Ignition, double? IgnitionAvg, IReadOnlyList<double?> KnockRetard,
    double? VanosIn, double? VanosEx, double? MafKgh, double? LoadMgStk, double? AfrTarget,
    double? RonAdaptation, double? LongTermTrim1, double? LongTermTrim2,
    bool? Clutch, bool? Brake, bool? OverheatLamp, bool? CatalystHeating, bool? TorqueIntervention = null,
    double? InjectorDuty = null, double? DmeTorque = null, bool? MalfunctionLamp = null, int? Retries = null);

/// <summary>Turns a decoded sample of either definition family into a <see cref="PerfFrame"/>.</summary>
public static class PerfChannels
{
    private static readonly ConditionalWeakTable<AdxDefinition, Layout> Layouts = new();

    /// <summary>
    /// The frame of one sample. <paramref name="values"/> are the sample's decoded values (the app decodes every channel); a
    /// channel absent from them, or not a finite number, reads as missing. Bits come from <paramref name="payload"/>.
    /// <paramref name="retries"/> is the sample's <see cref="LiveSample.Retries"/>, null when unknown.
    /// </summary>
    public static PerfFrame Read(AdxDefinition definition, IReadOnlyList<DecodedValue> values, ReadOnlySpan<byte> payload, TimeSpan at,
        int? retries = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        var layout = Layouts.GetValue(definition, d => new Layout(d));
        var byId = new Dictionary<string, double>(values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            byId.TryAdd(value.Definition.Id, value.Value);
        }

        double? Get(Channel? channel) =>
            channel is not null && byId.TryGetValue(channel.Id, out var value) && double.IsFinite(value)
                // Adding zero turns the -0 of an unretarded cylinder into 0.
                ? value * channel.Scale + 0d
                : null;

        // Gear is the floor of GEAR: its fraction is N's high bits; 0 means neutral or unknown.
        var gear = Get(layout.Gear) is { } decodedGear ? (int)Math.Clamp(Math.Floor(decodedGear), 0, 255) : (int?)null;

        return new PerfFrame(at, Get(layout.Rpm), Get(layout.Speed), gear, Get(layout.Pedal), Get(layout.Throttle),
            Get(layout.Coolant), Get(layout.Oil), Get(layout.Intake), Get(layout.Pressure), Get(layout.Battery),
            layout.Ignition.Select(Get).ToArray(), Get(layout.IgnitionAvg), layout.Knock.Select(Get).ToArray(),
            Get(layout.VanosIn), Get(layout.VanosEx), Get(layout.Maf), Get(layout.Load), Get(layout.Afr),
            Get(layout.Ron), Get(layout.Trim1), Get(layout.Trim2),
            Bit(definition, layout.Clutch, payload), Bit(definition, layout.Brake, payload),
            Bit(definition, layout.Overheat, payload), Bit(definition, layout.CatalystHeating, payload),
            AnyBit(definition, layout.TorqueIntervention, payload),
            Get(layout.InjectorDuty), Get(layout.DmeTorque), Bit(definition, layout.MalfunctionLamp, payload), retries);
    }

    /// <summary>
    /// What a test needs that this definition lacks, in the user's words and in this order: "engine speed", "vehicle speed",
    /// "gear", "pedal", "coolant temperature", "knock per cylinder". Empty when the test can run.
    /// </summary>
    public static IReadOnlyList<string> Missing(AdxDefinition definition, PerfTestKind kind)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var layout = Layouts.GetValue(definition, d => new Layout(d));
        var missing = new List<string>();
        void Need(bool needed, bool present, string name)
        {
            if (needed && !present)
            {
                missing.Add(name);
            }
        }

        // Each test needs what its ready checks and its start and end conditions read; coolant is needed wherever
        // full throttle is used, since a missing reading would pass the warm-up check unseen.
        var pullType = kind is PerfTestKind.Pull or PerfTestKind.KnockCheck or PerfTestKind.RollOn;
        Need(pullType || kind is PerfTestKind.SpeedCalibration, layout.Rpm is not null, "engine speed");
        Need(kind is PerfTestKind.RollOn or PerfTestKind.StandingStart or PerfTestKind.SpeedCalibration, layout.Speed is not null, "vehicle speed");
        Need(pullType || kind is PerfTestKind.SpeedCalibration, layout.Gear is not null, "gear");
        Need(kind is not PerfTestKind.LinkTest, layout.Pedal is not null, "pedal");
        Need(pullType || kind is PerfTestKind.StandingStart, layout.Coolant is not null, "coolant temperature");
        Need(kind is PerfTestKind.KnockCheck, layout.Knock.Count == 6 && layout.Knock.All(channel => channel is not null), "knock per cylinder");
        return missing;
    }

    private static bool? Bit(AdxDefinition definition, string? id, ReadOnlySpan<byte> payload) =>
        id is null ? null : definition.DecodeBitmask(id, payload);

    // Set when any of the bits is; unknown only when none of them reads.
    private static bool? AnyBit(AdxDefinition definition, IReadOnlyList<string> ids, ReadOnlySpan<byte> payload)
    {
        bool? any = null;
        foreach (var id in ids)
        {
            if (definition.DecodeBitmask(id, payload) is { } bit)
            {
                any = any == true || bit;
            }
        }

        return any;
    }

    /// <summary>A value id and the factor that brings it to the frame's unit.</summary>
    private sealed record Channel(string Id, double Scale);

    /// <summary>Which id of each family a definition carries, resolved once per definition: ms43x first, then the stock v2.9 one.</summary>
    private sealed class Layout
    {
        private readonly HashSet<string> _ids;

        public Layout(AdxDefinition definition)
        {
            _ids = new HashSet<string>(definition.Values.Select(value => value.Id), StringComparer.OrdinalIgnoreCase);
            Rpm = Find("N", "RPM");
            Speed = Find("VS", "VEHICLESPEED");
            Gear = Find("GEAR");
            Pedal = Find("PVS", "ACCELERATORPED");
            Throttle = Find("TPS");
            Coolant = Find("TCO", "COOLANTTEMP");
            Oil = Find("TOIL", "OILTEMP");
            Intake = Find("TIA", "INTAKEAIRTEMP");
            // AMP is kPa, HPA is hPa.
            Pressure = Find("AMP") ?? Scaled("HPA", 0.1);
            Battery = Find("VB", "BATTERYVOLTAGE");
            Ignition = Six(cylinder => Find($"IGA{cylinder}", $"IGNITION{cylinder}"));
            IgnitionAvg = Find("IGAAVG", "IGNITIONAVG");
            // Raw 128 is no correction and retard decodes negative; the frame keeps degrees of retard.
            Knock = Six(cylinder => Scaled($"IGAKNK{cylinder}", -1));
            VanosIn = Find("CAMAVIN", "VANOSINTAKE");
            VanosEx = Find("CAMAVEX", "VANOSEXHAUST");
            Maf = Find("MAF");
            Load = Find("MAFTI", "ENGINELOADTI");
            Afr = Find("AFTARGET");
            // RONADFAC reads 0..1, RONADFACTOR 0..100.
            Ron = Find("RONADFAC") ?? Scaled("RONADFACTOR", 0.01);
            // The multiplicative trims, in percent; the frame has no place for the additive TIADADD ones.
            Trim1 = Find("TIADFAC1");
            Trim2 = Find("TIADFAC2");
            Clutch = FindBit(definition, "CSSTATE", "CLUTCHSWITCH");
            Brake = FindBit(definition, "BLSSTATE", "BRAKELIGHTSWIT");
            // The stock definitions call the overheat lamp lv_teng_state.
            Overheat = FindBit(definition, "TENGSTATE", "lv_teng_state");
            CatalystHeating = FindBit(definition, "CHSTATE", "FD96.5");
            // DSC on the engine torque: ms43x logs traction (ASR) and drag torque (MSR) apart, the stock definitions one bit.
            TorqueIntervention = new[] { "TQINTVASR", "TQINTVMSR", "STATE_TQ_INTV" }.Where(id => definition.FindBitmask(id) is not null).ToArray();
            InjectorDuty = Find("INJDC", "DUTYCYCLE%");
            DmeTorque = Find("TQITQR", "ENGINETORQUE");
            MalfunctionLamp = FindBit(definition, "EMLSTATE", "EMLLIGHT");
        }

        public Channel? Rpm { get; }
        public Channel? Speed { get; }
        public Channel? Gear { get; }
        public Channel? Pedal { get; }
        public Channel? Throttle { get; }
        public Channel? Coolant { get; }
        public Channel? Oil { get; }
        public Channel? Intake { get; }
        public Channel? Pressure { get; }
        public Channel? Battery { get; }
        public IReadOnlyList<Channel?> Ignition { get; }
        public Channel? IgnitionAvg { get; }
        public IReadOnlyList<Channel?> Knock { get; }
        public Channel? VanosIn { get; }
        public Channel? VanosEx { get; }
        public Channel? Maf { get; }
        public Channel? Load { get; }
        public Channel? Afr { get; }
        public Channel? Ron { get; }
        public Channel? Trim1 { get; }
        public Channel? Trim2 { get; }
        public string? Clutch { get; }
        public string? Brake { get; }
        public string? Overheat { get; }
        public string? CatalystHeating { get; }
        public IReadOnlyList<string> TorqueIntervention { get; }
        public Channel? InjectorDuty { get; }
        public Channel? DmeTorque { get; }
        public string? MalfunctionLamp { get; }

        // The first of these ids the definition has, read as it decodes.
        private Channel? Find(params string[] ids) => ids.Where(_ids.Contains).Select(id => new Channel(id, 1)).FirstOrDefault();

        private Channel? Scaled(string id, double scale) => _ids.Contains(id) ? new Channel(id, scale) : null;

        // Six entries when the definition logs any cylinder, none when it logs no cylinder at all.
        private static IReadOnlyList<Channel?> Six(Func<int, Channel?> find)
        {
            var channels = Enumerable.Range(1, 6).Select(find).ToArray();
            return channels.Any(channel => channel is not null) ? channels : [];
        }

        private static string? FindBit(AdxDefinition definition, params string[] ids) =>
            ids.FirstOrDefault(id => definition.FindBitmask(id) is not null);
    }
}
