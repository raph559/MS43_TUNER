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

    /// <summary>First upward crossing of level, linear between the samples around it; null if never.</summary>
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

    /// <summary>Dry air density in kg/m³ from pressure (kPa) and intake air temperature (°C).</summary>
    public static double AirDensity(double pressureKpa, double intakeC) => pressureKpa * 1000 / (287.05 * (intakeC + 273.15));

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
}
