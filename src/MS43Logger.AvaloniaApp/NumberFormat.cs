using System;
using System.Globalization;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One number formatter for Live's readings (system 3.3): fixed decimals in the invariant culture, the ASCII minus, no
/// digit grouping, "n/a" for a missing value and never "-0".
/// </summary>
public static class NumberFormat
{
    public static string Format(double? value, int decimals)
    {
        if (value is not double number || !double.IsFinite(number))
        {
            return "n/a";
        }

        var text = number.ToString("F" + Math.Clamp(decimals, 0, 6), CultureInfo.InvariantCulture);

        // A small negative rounds to "-0" or "-0.0": there is nothing below zero to show.
        return text.StartsWith('-') && text.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? text[1..] : text;
    }
}
