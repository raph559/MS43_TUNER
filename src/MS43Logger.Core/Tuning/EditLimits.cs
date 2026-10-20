namespace MS43Logger.Core.Tuning;

/// <summary>
/// What an item can hold: the physical range its raw element can store, the step between neighbouring raw values
/// (null when the equation is not linear), and each end of the XDF's own range that is narrower than the storable one.
/// </summary>
public sealed record EditLimits(double StorableMin, double StorableMax, double? Step, double? XdfLow, double? XdfHigh)
{
    /// <summary>Tables pass <see cref="XdfZ.Min"/> and <see cref="XdfZ.Max"/>, constants their range low and high.</summary>
    public static EditLimits For(ValueCodec codec, XdfMath math, XdfData data, double? xdfLow, double? xdfHigh)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var (min, max, step) = codec.Storable(math, data);

        // An XDF end equal to the storable end, give or take float rounding, narrows nothing.
        var slack = 1e-9 * Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max)));
        return new EditLimits(
            min,
            max,
            step,
            xdfLow is double low && low > min + slack ? low : null,
            xdfHigh is double high && high < max - slack ? high : null);
    }
}
