using System.Globalization;
using System.Text.RegularExpressions;

namespace MS43Logger.Core.Performance;

/// <summary>
/// The numeric pieces of a run's analysis. Times are seconds on the run clock; a point whose time or value is not a finite
/// number is stepped over, so bursty or partly missing samples give null or NaN, never a throw or an infinity.
/// </summary>
public static partial class RunMath
{
    private const double Gravity = 9.81;

    /// <summary>First upward crossing of level, linear between the samples around it (spec 4.6); null if never.</summary>
    public static double? CrossingTime(IReadOnlyList<double> t, IReadOnlyList<double> v, double level)
    {
        CheckLengths(t, v);
        var previous = -1;
        for (var i = 0; i < t.Count; i++)
        {
            if (!double.IsFinite(t[i]) || !double.IsFinite(v[i]))
            {
                continue;
            }

            if (previous >= 0 && v[previous] < level && v[i] >= level)
            {
                var fraction = (level - v[previous]) / (v[i] - v[previous]);
                return t[previous] + fraction * (t[i] - t[previous]);
            }

            previous = i;
        }

        return null;
    }

    /// <summary>
    /// Time at v = 0 from a least-squares line through the points with low ≤ v ≤ high (spec 6.4), taken up to the first
    /// point above high so a later slowdown through the band cannot bend the line; null with under 3 points or a slope ≤ 0.
    /// </summary>
    public static double? BackExtrapolatedStart(IReadOnlyList<double> t, IReadOnlyList<double> v, double low = 3, double high = 20)
    {
        CheckLengths(t, v);
        var times = new List<double>();
        var values = new List<double>();
        for (var i = 0; i < t.Count; i++)
        {
            if (!double.IsFinite(t[i]) || !double.IsFinite(v[i]))
            {
                continue;
            }

            if (v[i] > high)
            {
                break;
            }

            if (v[i] >= low)
            {
                times.Add(t[i]);
                values.Add(v[i]);
            }
        }

        if (times.Count < 3)
        {
            return null;
        }

        var tMean = times.Average();
        var vMean = values.Average();
        double sxx = 0, sxv = 0;
        for (var i = 0; i < times.Count; i++)
        {
            sxx += (times[i] - tMean) * (times[i] - tMean);
            sxv += (times[i] - tMean) * (values[i] - vMean);
        }

        if (sxx <= 0)
        {
            return null;
        }

        var slope = sxv / sxx;
        if (!(slope > 0))
        {
            return null;
        }

        var start = tMean - vMean / slope;
        return double.IsFinite(start) ? start : null;
    }

    /// <summary>
    /// dv/dt at each point from a least-squares quadratic over |t - t_i| ≤ halfWindow (spec 7); NaN where fewer than
    /// minPoints distinct times. Points need not be in time order, and several may share one time.
    /// </summary>
    public static double[] Derivative(IReadOnlyList<double> t, IReadOnlyList<double> v, double halfWindow = 0.35, int minPoints = 5)
    {
        CheckLengths(t, v);
        if (!(halfWindow > 0) || !double.IsFinite(halfWindow))
        {
            throw new ArgumentOutOfRangeException(nameof(halfWindow), halfWindow, "The half window must be a positive number of seconds.");
        }

        // Three distinct times are the least a quadratic needs.
        minPoints = Math.Max(minPoints, 3);
        var result = new double[t.Count];
        Array.Fill(result, double.NaN);

        // Points in time order, so each window is a run of neighbours; a stable sort keeps samples sharing a time together.
        var order = Enumerable.Range(0, t.Count)
            .Where(i => double.IsFinite(t[i]) && double.IsFinite(v[i]))
            .OrderBy(i => t[i])
            .ToArray();
        var lo = 0;
        var hi = 0;
        foreach (var i in order)
        {
            var ti = t[i];
            while (t[order[lo]] < ti - halfWindow)
            {
                lo++;
            }

            while (hi < order.Length && t[order[hi]] <= ti + halfWindow)
            {
                hi++;
            }

            // Fit v - v_i = c0 + c1 s + c2 s² with s = (t - t_i) / halfWindow: centred and scaled for a well-conditioned fit.
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, y0 = 0, y1 = 0, y2 = 0;
            var distinct = 0;
            var lastTime = double.NaN;
            for (var k = lo; k < hi; k++)
            {
                var j = order[k];
                if (t[j] != lastTime)
                {
                    distinct++;
                    lastTime = t[j];
                }

                var s = (t[j] - ti) / halfWindow;
                var y = v[j] - v[i];
                var ss = s * s;
                s0 += 1;
                s1 += s;
                s2 += ss;
                s3 += ss * s;
                s4 += ss * ss;
                y0 += y;
                y1 += y * s;
                y2 += y * ss;
            }

            if (distinct < minPoints)
            {
                continue;
            }

            var det = Det3(s0, s1, s2, s1, s2, s3, s2, s3, s4);
            if (!(Math.Abs(det) > 1e-12 * s0 * s0 * s0))
            {
                continue;
            }

            var slope = Det3(s0, y0, s2, s1, y1, s3, s2, y2, s4) / det / halfWindow;
            result[i] = double.IsFinite(slope) ? slope : double.NaN;
        }

        return result;
    }

    /// <summary>
    /// Rolling circumference in metres: 0.97 × the free circumference of "205/55 R16" (π × (rim × 25.4 + 2 × width × aspect
    /// / 100) mm); null if unparsable. "205/55R16" and a trailing load and speed index ("225/45 ZR17 91W") also read.
    /// </summary>
    public static double? TyreCircumference(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return null;
        }

        var match = TyrePattern().Match(size);
        if (!match.Success)
        {
            return null;
        }

        var width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var aspect = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var rim = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (width == 0 || aspect == 0 || rim == 0)
        {
            return null;
        }

        // A loaded tyre rolls about 3 % short of its free circumference.
        var diameterMm = rim * 25.4 + 2 * width * aspect / 100.0;
        return 0.97 * Math.PI * diameterMm / 1000;
    }

    /// <summary>Dry air density in kg/m³ from pressure (kPa) and intake air temperature (°C).</summary>
    public static double AirDensity(double pressureKpa, double intakeC) => pressureKpa * 1000 / (287.05 * (intakeC + 273.15));

    /// <summary>
    /// Wheel power (kW) and torque at the crank speed (Nm) per sample (spec 7). kmhPerRpm is km/h per rpm in this gear.
    /// Every entry is NaN when the tyre size cannot be read or the ratio is not positive, and NaN where the rpm slope is
    /// unknown; torque is NaN at 0 rpm.
    /// </summary>
    public static (double[] PowerKw, double[] TorqueNm) Power(IReadOnlyList<double> t, IReadOnlyList<double> rpm, double kmhPerRpm,
        CarValues car, double airDensity)
    {
        CheckLengths(t, rpm);
        ArgumentNullException.ThrowIfNull(car);
        var power = new double[rpm.Count];
        var torque = new double[rpm.Count];
        Array.Fill(power, double.NaN);
        Array.Fill(torque, double.NaN);
        if (TyreCircumference(car.TyreSize) is not { } circumference || !(kmhPerRpm > 0) || !double.IsFinite(kmhPerRpm))
        {
            return (power, torque);
        }

        var radius = circumference / (2 * Math.PI);
        // Engine rad/s per m/s of road speed: the gearing that makes the flywheel part of the mass.
        var engine = 2 * Math.PI / 60 * 3.6 / kmhPerRpm;
        var effectiveMass = car.MassKg + car.WheelInertia / (radius * radius) + car.EngineInertia * engine * engine;
        // Acceleration from the rpm slope, never from VS: its whole-km/h steps are too coarse (spec 4.5, 7).
        var rpmSlope = Derivative(t, rpm);
        for (var i = 0; i < rpm.Count; i++)
        {
            var n = rpm[i];
            var speed = kmhPerRpm * n / 3.6;
            var acceleration = kmhPerRpm / 3.6 * rpmSlope[i];
            var force = effectiveMass * acceleration + 0.5 * airDensity * car.CdA * speed * speed + car.Crr * car.MassKg * Gravity;
            var kw = force * speed / 1000;
            if (!double.IsFinite(kw))
            {
                continue;
            }

            power[i] = kw;
            torque[i] = n > 0 ? kw * 1000 * 60 / (2 * Math.PI * n) : double.NaN;
        }

        return (power, torque);
    }

    /// <summary>
    /// Mean (or max) of values in rpm bins of width centred on from, from + width, … up to to. A bin covers its centre ± half
    /// the width, the upper edge belonging to the next bin; readings past the last bin's upper edge, up to to, belong to the
    /// last bin, so the top of a band that is not a whole number of widths is kept. Empty bins are left out.
    /// </summary>
    public static IReadOnlyList<(double Rpm, double Value)> Bin(IReadOnlyList<double> rpm, IReadOnlyList<double> values, double from, double to,
        double width = 250, bool max = false)
    {
        CheckLengths(rpm, values);
        if (!(width > 0) || !double.IsFinite(width))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The bin width must be a positive number of rpm.");
        }

        if (!double.IsFinite(from) || !double.IsFinite(to) || to < from)
        {
            return [];
        }

        var count = BinCount(from, to, width);
        var sums = new double[count];
        var tops = new double[count];
        var counts = new int[count];
        Array.Fill(tops, double.NegativeInfinity);
        for (var i = 0; i < rpm.Count; i++)
        {
            if (!double.IsFinite(values[i]) || BinOf(rpm[i], from, to, width) is not { } b)
            {
                continue;
            }

            sums[b] += values[i];
            tops[b] = Math.Max(tops[b], values[i]);
            counts[b]++;
        }

        return Enumerable.Range(0, count)
            .Where(b => counts[b] > 0)
            .Select(b => (from + b * width, max ? tops[b] : sums[b] / counts[b]))
            .ToArray();
    }

    /// <summary>The bin of <see cref="Bin"/> a reading falls in, null for one in none; the bins must be valid (to ≥ from, width > 0).</summary>
    internal static int? BinOf(double rpm, double from, double to, double width)
    {
        if (!double.IsFinite(rpm))
        {
            return null;
        }

        var count = BinCount(from, to, width);
        var bin = Math.Floor((rpm - (from - width / 2)) / width);
        return bin < 0 ? null
            : bin < count ? (int)bin
            : rpm <= to + 1e-9 ? count - 1
            : null;
    }

    // The small allowance keeps a to that is a whole number of widths from from, despite rounding.
    private static int BinCount(double from, double to, double width) => (int)Math.Floor((to - from) / width + 1e-9) + 1;

    /// <summary>The middle value, or the mean of the two middle ones; NaN values are left out, and NaN when none remain.</summary>
    public static double Median(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static void CheckLengths(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException($"The two series differ in length ({x.Count} and {y.Count}).");
        }
    }

    private static double Det3(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
        a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

    // Width / aspect, an optional R, ZR or dash, the rim, then an optional load and speed index.
    [GeneratedRegex(@"^\s*(\d{3})\s*/\s*(\d{2})\s*(?:Z?R|-)?\s*(\d{2})(?:\s+\d{2,3}\s*[A-Z]{1,2})?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TyrePattern();
}
