using System.Globalization;

namespace MS43Logger.Core.Performance;

public enum WarmUpLevel
{
    Info,
    Caution,
    Danger,
}

/// <summary>
/// One warm-up or condition warning (performance spec 5). <see cref="Code"/> is "coolant-cold", "oil-cold", "oil-unreported",
/// "coolant-hot", "oil-hot" or "overheat"; <see cref="Text"/> carries the latest reading in whole degrees.
/// </summary>
public sealed record WarmUpBanner(WarmUpLevel Level, string Code, string Text);

/// <summary>
/// Watches coolant, oil and the overheat lamp sample by sample and keeps the banners of performance spec 5. Cold clears only
/// after 10 s at 80 °C or more and returns below 75 °C; a missing reading or bit changes nothing, except that a frame without
/// an oil reading counts as oil not reported.
/// </summary>
public sealed class WarmUpMonitor
{
    private const double OperatingC = 80;
    private const double ColdAgainC = 75;
    private static readonly TimeSpan WarmHold = TimeSpan.FromSeconds(10);
    // What to do about a hot reading (system 9.1: what happened, then what to do).
    private const string CoolDown = "Ease off and let it idle to cool.";

    private readonly ColdWatch _coolantCold = new();
    private readonly ColdWatch _oilCold = new();
    private readonly HotWatch _coolantHot = new(115, 110);
    private readonly HotWatch _oilHot = new(130, 125);
    private readonly FrozenOilWatch _frozenOil = new();
    private double? _coolant;
    private double? _oil;
    private bool _oilMissing;
    private bool _oilImplausible;
    private bool _overheat;
    private WarmUpBanner[] _banners = [];

    /// <summary>Danger first, then caution, then info.</summary>
    public IReadOnlyList<WarmUpBanner> Banners => _banners;

    /// <summary>Coolant below operating temperature: full throttle must wait (spec 5).</summary>
    public bool CoolantCold => _coolantCold.Cold;

    /// <summary>Oil below working temperature; false while oil is not reported.</summary>
    public bool OilCold => !OilUnreported && _oilCold.Cold;

    /// <summary>
    /// No oil reading, a reading at or below -40 °C or at or above 150 °C, or one that stayed within 2 °C while the coolant
    /// rose 20 °C warming up to operating temperature (spec 5).
    /// </summary>
    public bool OilUnreported => _oilMissing || _oilImplausible || _frozenOil.Frozen;

    /// <summary>Raised only when <see cref="Banners"/> changes, text included.</summary>
    public event EventHandler? Changed;

    public void Push(PerfFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.CoolantC is { } coolant)
        {
            _coolant = coolant;
            _coolantCold.Push(coolant, frame.At);
            _coolantHot.Push(coolant);
        }

        _oilMissing = frame.OilC is null;
        if (frame.OilC is { } oil)
        {
            _oilImplausible = oil is <= -40 or >= 150;
            if (!_oilImplausible)
            {
                _oil = oil;
                _oilCold.Push(oil, frame.At);
                _oilHot.Push(oil);
                _frozenOil.Push(oil, frame.CoolantC);
            }
        }

        if (frame.OverheatLamp is { } lamp)
        {
            _overheat = lamp;
        }

        Publish();
    }

    /// <summary>Forgets every reading, as on a disconnect: the next reading is a first reading again.</summary>
    public void Reset()
    {
        _coolantCold.Reset();
        _oilCold.Reset();
        _coolantHot.Reset();
        _oilHot.Reset();
        _frozenOil.Reset();
        _coolant = null;
        _oil = null;
        _oilMissing = false;
        _oilImplausible = false;
        _overheat = false;
        Publish();
    }

    private void Publish()
    {
        var banners = new List<WarmUpBanner>();
        if (_overheat)
        {
            banners.Add(new WarmUpBanner(WarmUpLevel.Danger, "overheat", "The ECU reports engine overheating."));
        }

        if (_coolant is { } coolant)
        {
            if (_coolantCold.Cold)
            {
                // While a warm reading is held for its 10 s, "not at operating temperature" would contradict the reading.
                banners.Add(new WarmUpBanner(WarmUpLevel.Danger, "coolant-cold", _coolantCold.Holding
                    ? $"Coolant {Degrees(coolant)} °C: nearly at operating temperature. Do not use full throttle yet."
                    : $"Coolant {Degrees(coolant)} °C: engine not at operating temperature ({Degrees(OperatingC)} °C). Do not use full throttle; results will not be valid."));
            }

            if (_coolantHot.Hot)
            {
                banners.Add(new WarmUpBanner(WarmUpLevel.Caution, "coolant-hot", $"Coolant {Degrees(coolant)} °C: running hot. {CoolDown}"));
            }
        }

        if (OilUnreported)
        {
            // A reading that never moves may be the DME's model rather than a sensor; no reading at all is not reported.
            banners.Add(new WarmUpBanner(WarmUpLevel.Info, "oil-unreported", !_oilMissing && !_oilImplausible
                ? "Oil temperature does not change: the ECU may not measure it."
                : "Oil temperature not reported by this ECU."));
        }
        else if (_oil is { } oil)
        {
            if (_oilCold.Cold)
            {
                banners.Add(new WarmUpBanner(WarmUpLevel.Caution, "oil-cold", _oilCold.Holding
                    ? $"Oil {Degrees(oil)} °C: nearly at working temperature, holding {Degrees(WarmHold.TotalSeconds)} s."
                    : $"Oil {Degrees(oil)} °C: not at working temperature yet ({Degrees(OperatingC)} °C)."));
            }

            if (_oilHot.Hot)
            {
                banners.Add(new WarmUpBanner(WarmUpLevel.Caution, "oil-hot", $"Oil {Degrees(oil)} °C: running hot. {CoolDown}"));
            }
        }

        // OrderByDescending is stable, so banners of one level keep the order above.
        var next = banners.OrderByDescending(banner => banner.Level).ToArray();
        if (next.SequenceEqual(_banners))
        {
            return;
        }

        _banners = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Degrees(double celsius) => celsius.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Cold below 80 °C; warm again only after 10 s at 80 °C or more, cold again below 75 °C.</summary>
    private sealed class ColdWatch
    {
        private TimeSpan? _warmSince;
        private bool? _cold;

        public bool Cold => _cold == true;

        /// <summary>Still cold, with a reading at 80 °C or more held since <c>_warmSince</c>: the 10 s are running.</summary>
        public bool Holding => Cold && _warmSince is not null;

        public void Push(double celsius, TimeSpan at)
        {
            if (_cold is null)
            {
                // Warm at the first reading needs no wait (spec 5): the 10 s hold guards a recovery from cold, not a fresh start.
                _cold = celsius < OperatingC;
                return;
            }

            if (_cold == false)
            {
                _cold = celsius < ColdAgainC;
                return;
            }

            if (celsius < OperatingC)
            {
                _warmSince = null;
                return;
            }

            _warmSince ??= at;
            if (at - _warmSince.Value >= WarmHold)
            {
                _cold = false;
                _warmSince = null;
            }
        }

        public void Reset()
        {
            _cold = null;
            _warmSince = null;
        }
    }

    /// <summary>Hot above <c>on</c>, clears below <c>off</c>.</summary>
    private sealed class HotWatch(double on, double off)
    {
        public bool Hot { get; private set; }

        public void Push(double celsius) => Hot = Hot ? celsius >= off : celsius > on;

        public void Reset() => Hot = false;
    }

    /// <summary>
    /// Oil that stays within 2 °C of where the watch began while the coolant rises 20 °C from its lowest since then is a
    /// frozen or modelled value (spec 5, 11); oil moving more than 2 °C begins the watch again and clears it. The rise counts
    /// only below operating temperature, where it is a warm-up: on a warm engine the map thermostat swings the coolant 20 °C
    /// while the oil holds steady.
    /// </summary>
    private sealed class FrozenOilWatch
    {
        private double? _anchor;
        private double? _coolantLow;

        public bool Frozen { get; private set; }

        public void Push(double oil, double? coolant)
        {
            if (_anchor is not { } anchor || Math.Abs(oil - anchor) > 2)
            {
                _anchor = oil;
                _coolantLow = coolant is { } first ? Math.Min(first, OperatingC) : null;
                Frozen = false;
                return;
            }

            if (coolant is not { } reading)
            {
                return;
            }

            var c = Math.Min(reading, OperatingC);
            _coolantLow = Math.Min(_coolantLow ?? c, c);
            if (c - _coolantLow.Value >= 20)
            {
                Frozen = true;
            }
        }

        public void Reset()
        {
            _anchor = null;
            _coolantLow = null;
            Frozen = false;
        }
    }
}
