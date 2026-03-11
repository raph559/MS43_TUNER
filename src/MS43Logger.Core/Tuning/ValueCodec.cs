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
            return "—";
        }

        var text = outputType == 2
            ? Math.Round(physical, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : physical.ToString("F" + Math.Clamp(decimalPlaces ?? 2, 0, 15), CultureInfo.InvariantCulture);

        // "-0.0" reads as a different value than "0.0"; TunerPro shows zero without a sign.
        return text.StartsWith('-') && text.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? text[1..] : text;
    }

    private static (long Min, long Max) RawRange(XdfData data)
    {
        var bits = data.ElementBits;
        return data.Signed ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1) : (0, (1L << bits) - 1);
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
}
