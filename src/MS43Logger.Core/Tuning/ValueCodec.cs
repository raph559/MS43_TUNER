using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <summary>Converts XDF raw values to physical values and back, and formats them the way TunerPro shows them.</summary>
public sealed class ValueCodec
{
    private const int MaxLinkDepth = 4;
    private readonly XdfDefinition _xdf;
    private readonly BinImage _image;
    private readonly Dictionary<string, double[]> _tables = new();

    public ValueCodec(XdfDefinition xdf, BinImage image)
    {
        _xdf = xdf ?? throw new ArgumentNullException(nameof(xdf));
        _image = image ?? throw new ArgumentNullException(nameof(image));
    }

    /// <summary>Physical value of <paramref name="raw"/>, or NaN when the equation cannot be evaluated or is not finite.</summary>
    public double ToPhysical(XdfMath math, long raw) => ToPhysical(math, raw, LinkValues(math, 0));

    /// <summary>The raw value whose physical value is closest to <paramref name="physical"/>, clamped to the element range.</summary>
    public long ToRaw(XdfMath math, XdfData data, double physical)
    {
        ArgumentNullException.ThrowIfNull(math);
        ArgumentNullException.ThrowIfNull(data);
        if (!double.IsFinite(physical))
        {
            throw new ArgumentException("The target value must be a finite number.", nameof(physical));
        }

        var links = LinkValues(math, 0);
        var (min, max) = RawRange(data);
        if (TryLinear(math, links, min, max, out var at0, out var slope))
        {
            // Clamp before the cast: huge targets would otherwise saturate the long and wrap to the wrong end.
            var exact = Math.Round(min + (physical - at0) / slope);
            if (exact <= min || exact >= max)
            {
                return exact <= min ? min : max;
            }

            var guess = (long)exact;
            return Nearest(math, links, physical, Math.Max(guess - 1, min), Math.Min(guess + 1, max));
        }

        if (data.ElementBits > 16)
        {
            throw new NotSupportedException($"Converting back through '{math.Equation}' is only supported for linear 32-bit values.");
        }

        var table = Table(math, links, min, max);
        long best = min;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < table.Length; i++)
        {
            var distance = Math.Abs(table[i] - physical);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = min + i;
            }
        }

        if (double.IsPositiveInfinity(bestDistance))
        {
            throw new InvalidOperationException($"'{math.Equation}' gives no finite value for any raw value, so {physical} cannot be stored.");
        }

        return best;
    }

    public static string Format(double physical, long raw, int? decimalPlaces, int? outputType, int elementBits)
    {
        switch (outputType)
        {
            case 3:
                var mask = elementBits >= 64 ? ulong.MaxValue : (1UL << elementBits) - 1;
                return "0x" + ((ulong)raw & mask).ToString("X" + Math.Max(1, elementBits / 4), CultureInfo.InvariantCulture);
            case 4:
                return raw is >= 0x20 and <= 0x7E ? ((char)raw).ToString() : "?";
        }

        if (double.IsNaN(physical))
        {
            return "n/a";
        }

        var text = outputType == 2
            ? Math.Round(physical, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : physical.ToString("F" + Math.Clamp(decimalPlaces ?? 2, 0, 15), CultureInfo.InvariantCulture);

        // "-0.0" reads as a different value than "0.0"; TunerPro shows zero without a sign.
        return text.StartsWith('-') && text.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? text[1..] : text;
    }

    internal static (long Min, long Max) RawRange(XdfData data)
    {
        var bits = data.ElementBits;
        return data.Signed ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1) : (0, (1L << bits) - 1);
    }

    /// <summary>
    /// The physical values <paramref name="data"/> can hold. A linear equation gives the values at both raw ends and
    /// the step between neighbouring raws; any other equation is scanned over every raw value (up to 16 bits) and has
    /// no step. The ends are NaN when the equation cannot be evaluated.
    /// </summary>
    internal (double Min, double Max, double? Step) Storable(XdfMath math, XdfData data)
    {
        ArgumentNullException.ThrowIfNull(math);
        ArgumentNullException.ThrowIfNull(data);
        var links = LinkValues(math, 0);
        var (min, max) = RawRange(data);
        if (TryLinear(math, links, min, max, out var at0, out var slope))
        {
            var atMax = ToPhysical(math, max, links);
            return (Math.Min(at0, atMax), Math.Max(at0, atMax), Math.Abs(slope));
        }

        if (data.ElementBits > 16)
        {
            var low = ToPhysical(math, min, links);
            var high = ToPhysical(math, max, links);
            return (Math.Min(low, high), Math.Max(low, high), null);
        }

        // Table() stores values that cannot be evaluated as +infinity.
        var finite = Table(math, links, min, max).Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? (double.NaN, double.NaN, null) : (finite.Min(), finite.Max(), null);
    }

    private double ToPhysical(XdfMath math, long raw, IReadOnlyDictionary<string, double>? links)
    {
        ArgumentNullException.ThrowIfNull(math);
        if (links is null)
        {
            return double.NaN;
        }

        var variables = new Dictionary<string, double>(links, StringComparer.OrdinalIgnoreCase);
        foreach (var variable in math.Variables.Where(variable => variable.Kind == XdfVariableKind.Raw))
        {
            variables[variable.Id] = raw;
        }

        if (!variables.ContainsKey("X"))
        {
            variables["X"] = raw;
        }

        try
        {
            var value = ExpressionEvaluator.Evaluate(math.Equation, variables);
            return double.IsFinite(value) ? value : double.NaN;
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
    }

    /// <summary>Values of the linked variables, or null when one cannot be resolved.</summary>
    private Dictionary<string, double>? LinkValues(XdfMath math, int depth)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in math.Variables)
        {
            switch (variable.Kind)
            {
                case XdfVariableKind.Unsupported:
                    return null;
                case XdfVariableKind.Link:
                    if (depth >= MaxLinkDepth ||
                        variable.LinkId is not int id ||
                        _xdf.FindConstantById(id) is not XdfConstant constant ||
                        !constant.Data.IsSupported ||
                        !_image.Contains(constant.Data))
                    {
                        return null;
                    }

                    var value = ToPhysical(constant.Math, _image.ReadElement(constant.Data, 0), LinkValues(constant.Math, depth + 1));
                    if (double.IsNaN(value))
                    {
                        return null;
                    }

                    values[variable.Id] = value;
                    break;
            }
        }

        return values;
    }

    private bool TryLinear(XdfMath math, IReadOnlyDictionary<string, double>? links, long min, long max, out double at0, out double slope)
    {
        // The slope comes from the end points: two neighbouring raws lose precision across a 32-bit range.
        at0 = ToPhysical(math, min, links);
        var atMax = ToPhysical(math, max, links);
        slope = (atMax - at0) / (max - min);
        if (!double.IsFinite(at0) || !double.IsFinite(atMax) || slope == 0)
        {
            return false;
        }

        var tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(at0), Math.Abs(atMax)));
        foreach (var point in new[] { min + 1, min + (max - min) / 2, max - 1 })
        {
            var expected = at0 + slope * (point - min);
            var actual = ToPhysical(math, point, links);
            if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private long Nearest(XdfMath math, IReadOnlyDictionary<string, double>? links, double physical, long from, long to)
    {
        var best = from;
        var bestDistance = double.PositiveInfinity;
        for (var raw = from; raw <= to; raw++)
        {
            var distance = Math.Abs(ToPhysical(math, raw, links) - physical);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = raw;
            }
        }

        return best;
    }

    private double[] Table(XdfMath math, IReadOnlyDictionary<string, double>? links, long min, long max)
    {
        var key = string.Join('|', math.Equation, min, max, string.Join(',', links?.Select(pair => pair.Key + "=" + pair.Value.ToString("R", CultureInfo.InvariantCulture)) ?? Array.Empty<string>()));
        if (!_tables.TryGetValue(key, out var table))
        {
            table = new double[max - min + 1];
            for (var i = 0; i < table.Length; i++)
            {
                var value = ToPhysical(math, min + i, links);
                table[i] = double.IsNaN(value) ? double.PositiveInfinity : value;
            }

            _tables[key] = table;
        }

        return table;
    }
}
