using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

/// <summary>
/// Made-up drives on the ms43x definition, as the logger would deliver them: whole frames built with <see cref="LivePayloads"/>,
/// every channel decoded, <see cref="LiveSample.RequestedAt"/> at i / rate seconds on the run clock. Besides what each run
/// drives, intake air reads 25 °C, pressure 101.3 kPa, battery 14 V, ignition 30° and knock none (raw zero would read 48° of
/// retard); the other channels stay at raw zero.
/// </summary>
internal static class SyntheticRuns
{
    /// <summary>The pedal reading a test treats as fully pressed; percentages are relative to it.</summary>
    public const double FullPedal = 100;

    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <paramref name="holdSeconds"/> held at <paramref name="fromRpm"/> with the pedal at 10 %, then the pedal at
    /// <paramref name="pedalPercent"/> and the rpm rising linearly to <paramref name="toRpm"/> over <paramref name="seconds"/>;
    /// the last sample is at <paramref name="toRpm"/>. Speed is rpm × <paramref name="kmhPer1000"/> / 1000, whole km/h as VS
    /// carries it. Cylinder 4 is retarded by <paramref name="knockCyl4"/> degrees while the rpm is from
    /// <paramref name="knockFromRpm"/> to <paramref name="knockToRpm"/>. The throttle body reads
    /// <paramref name="throttleShare"/> of the pedal's degrees; the clutch or brake bit is set on the ramp from
    /// <paramref name="clutchFromRpm"/> or <paramref name="brakeFromRpm"/>. <paramref name="adx"/> is ms43x or a stock v2.9
    /// definition, which logs no knock per cylinder.
    /// </summary>
    public static IReadOnlyList<LiveSample> Pull(AdxDefinition adx, int gear, double fromRpm, double toRpm, double seconds,
        double rate = 30, double kmhPer1000 = 22.3, double coolantC = 92, double oilC = 88, double pedalPercent = 100,
        double knockCyl4 = 0, double knockFromRpm = 5000, double knockToRpm = 5600, double holdSeconds = 1, double intakeC = 25,
        double? clutchFromRpm = null, double? brakeFromRpm = null, double throttleShare = 0.9)
    {
        var run = new Builder(adx, rate, coolantC, oilC, intakeC, throttleShare);
        for (var i = 0; ; i++)
        {
            var t = i / rate;
            if (t < holdSeconds - 1e-9)
            {
                run.Add(t, fromRpm, gear, fromRpm * kmhPer1000 / 1000, 10);
                continue;
            }

            var done = Fraction(t - holdSeconds, seconds);
            var rpm = fromRpm + (toRpm - fromRpm) * done;
            // The band is checked on the rpm the frame carries, so a test reading N sees knock exactly inside it.
            var knock = Math.Round(rpm) >= knockFromRpm && Math.Round(rpm) <= knockToRpm ? knockCyl4 : 0;
            run.Add(t, rpm, gear, rpm * kmhPer1000 / 1000, pedalPercent, knock,
                clutch: Math.Round(rpm) >= clutchFromRpm, brake: Math.Round(rpm) >= brakeFromRpm);
            if (done >= 1)
            {
                return run.Samples;
            }
        }
    }

    /// <summary>Parked and idling: 750 rpm in neutral, stopped, pedal released, from 0 to <paramref name="seconds"/> inclusive.</summary>
    public static IReadOnlyList<LiveSample> Idle(AdxDefinition ms43x, double seconds, double rate = 30, double coolantC = 92, double oilC = 88)
    {
        var run = new Builder(ms43x, rate, coolantC, oilC);
        var count = (int)Math.Round(seconds * rate);
        for (var i = 0; i <= count; i++)
        {
            run.Add(i / rate, 750, 0, 0, 0);
        }

        return run.Samples;
    }

    /// <summary>
    /// 2 s stopped (800 rpm, pedal released), then full pedal with the speed rising linearly to 100 km/h over
    /// <paramref name="seconds0to100"/>, then 1.5 s held at 100 km/h with the pedal released, so a run ends on its own. The gear
    /// reads 1st throughout, with a long ratio (15.4 km/h per 1000 rpm, the clutch slipping at 1500 rpm below 23 km/h) so the
    /// whole run stays under the 8191 rpm the frame's N field can carry.
    /// </summary>
    public static IReadOnlyList<LiveSample> Standing(AdxDefinition ms43x, double seconds0to100, double rate = 30)
    {
        var run = new Builder(ms43x, rate, 92, 88);
        for (var i = 0; ; i++)
        {
            var t = i / rate;
            if (t < 2)
            {
                run.Add(t, 800, 1, 0, 0);
                continue;
            }

            var done = Fraction(t - 2, seconds0to100);
            var kmh = 100 * done;
            var rpm = Math.Max(1500, kmh * 1000 / 15.4);
            run.Add(t, rpm, 1, kmh, done < 1 ? 100 : 0);
            if (t - 2 - seconds0to100 >= 1.5 - 1e-9)
            {
                return run.Samples;
            }
        }
    }

    // How far along a ramp is, held at 1 at its end; the tolerance keeps the last sample of an exact count at the target.
    private static double Fraction(double elapsed, double length) =>
        length <= 0 || elapsed / length > 1 - 1e-9 ? 1 : Math.Max(0, elapsed / length);

    private sealed class Builder(AdxDefinition adx, double rate, double coolantC, double oilC, double intakeC = 25, double throttleShare = 0.9)
    {
        private readonly Dictionary<string, (AdxValueDefinition Channel, double Offset, double Scale)> _linear =
            new(StringComparer.OrdinalIgnoreCase);

        // The stock v2.9 definitions name the channels apart and log the gear on a byte of its own.
        private readonly bool _stock = adx.Values.All(v => !string.Equals(v.Id, "N", StringComparison.OrdinalIgnoreCase));

        public List<LiveSample> Samples { get; } = [];

        public void Add(double t, double rpm, int gear, double speedKmh, double pedalPercent, double knockCyl4 = 0,
            bool clutch = false, bool brake = false)
        {
            var n = (long)Math.Round(rpm);
            if ((n is < 0 or > 8191 && !_stock) || gear is < 0 or > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(rpm), $"N carries 0 to 8191 rpm and gears 0 to 7 ({n} rpm, gear {gear}).");
            }

            var pedal = FullPedal * pedalPercent / 100;
            var values = _stock
                ? new List<(string, long)>
                {
                    ("RPM", n),
                    ("GEAR", gear),
                    ("VEHICLESPEED", Math.Clamp((long)Math.Round(speedKmh), 0, 255)),
                    ("ACCELERATORPED", Raw("ACCELERATORPED", pedal)),
                    ("TPS", Raw("TPS", pedal * throttleShare)),
                    ("COOLANTTEMP", Raw("COOLANTTEMP", coolantC)),
                    ("OILTEMP", Raw("OILTEMP", oilC)),
                    ("INTAKEAIRTEMP", Raw("INTAKEAIRTEMP", intakeC)),
                    ("HPA", Raw("HPA", 1013)),
                    ("BATTERYVOLTAGE", Raw("BATTERYVOLTAGE", 14)),
                }
                : new List<(string, long)>
                {
                    // GEAR is byte 0x05 / 32: the gear sits above N's 13 bits.
                    ("N", n | (long)gear << 13),
                    ("VS", Math.Clamp((long)Math.Round(speedKmh), 0, 255)),
                    ("PVS", Raw("PVS", pedal)),
                    ("TPS", Raw("TPS", pedal * throttleShare)),
                    ("TCO", Raw("TCO", coolantC)),
                    ("TOIL", Raw("TOIL", oilC)),
                    ("TIA", Raw("TIA", intakeC)),
                    ("AMP", Raw("AMP", 101.3)),
                    ("VB", Raw("VB", 14)),
                };

            for (var cylinder = 1; cylinder <= 6 && !_stock; cylinder++)
            {
                values.Add(($"IGA{cylinder}", Raw($"IGA{cylinder}", 30)));
                // Raw 128 is no correction; retard decodes negative.
                values.Add(($"IGAKNK{cylinder}", Raw($"IGAKNK{cylinder}", cylinder == 4 ? -knockCyl4 : 0)));
            }

            // Status bits as the definition lays them out: ms43x CSSTATE and BLSSTATE, the stock CLUTCHSWITCH and BRAKELIGHTSWIT.
            var bits = new List<(int Offset, byte Value)>();
            void Set(bool on, params string[] ids)
            {
                if (on && ids.Select(adx.FindBitmask).FirstOrDefault(bit => bit is not null) is { } bit)
                {
                    bits.Add((bit.PacketOffset, (byte)bit.Result));
                }
            }

            Set(clutch, "CSSTATE", "CLUTCHSWITCH");
            Set(brake, "BLSSTATE", "BRAKELIGHTSWIT");
            var payload = LivePayloads.Build(adx, bits.ToArray(), values.ToArray());
            var at = TimeSpan.FromSeconds(t);
            Samples.Add(new LiveSample(Start + at, Samples.Count, adx.DecodePayload(payload), payload, at,
                TimeSpan.FromSeconds(t + 0.25 / rate)));
        }

        // The raw value that decodes closest to value, for a channel whose equation is linear in its raw value.
        private long Raw(string id, double value)
        {
            if (!_linear.TryGetValue(id, out var line))
            {
                var channel = adx.Values.First(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
                var offset = Decode(channel, 0);
                line = (channel, offset, Decode(channel, 1) - offset);
                _linear[id] = line;
            }

            var max = (1L << line.Channel.SizeInBits) - 1;
            return Math.Clamp((long)Math.Round((value - line.Offset) / line.Scale), 0, max);
        }

        private double Decode(AdxValueDefinition channel, long raw) =>
            adx.DecodePayload(LivePayloads.Build(adx, (channel.Id, raw)), [channel])[0].Value;
    }
}
