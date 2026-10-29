using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>The axis arithmetic and 12 px text the charts share: the Tune curve and the Performance run charts.</summary>
internal static class ChartScale
{
    /// <summary>
    /// Round ticks over [min, max]: a step of 1, 2, 2.5, 3, 4 or 5 times a power of ten, about <paramref name="steps"/>
    /// steps across. The range runs from the first tick to the last.
    /// </summary>
    public static (double Min, double Max, double Step, IReadOnlyList<double> Ticks) Ticks(double min, double max, int steps = 4)
    {
        if (max - min < 1e-9)
        {
            (min, max) = (min - 1, max + 1);
        }

        var rough = (max - min) / steps;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = new[] { 1, 2, 2.5, 3, 4, 5, 10 }.Select(factor => factor * magnitude).First(candidate => candidate >= rough * 0.999);
        var low = Math.Floor(min / step + 1e-9) * step;
        var high = Math.Ceiling(max / step - 1e-9) * step;
        var ticks = new List<double>();
        for (var tick = low; tick <= high + step * 1e-6; tick += step)
        {
            ticks.Add(Math.Round(tick / step) * step);
        }

        return (low, high, step, ticks);
    }

    /// <summary>A tick value with no more decimals than its step needs, and an ASCII minus.</summary>
    public static string Format(double value, double step)
    {
        var decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : 2;
        var text = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return text.StartsWith('-') && text.AsSpan(1).IndexOfAnyExcept("0.") < 0 ? text[1..] : text;
    }

    /// <summary>12 px text with tabular figures, aligned on <paramref name="at"/> by fractions of its width and height.</summary>
    public static void Text(DrawingContext context, string? text, Typeface face, IBrush brush, Point at, double alignX, double alignY)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, brush);
        formatted.SetFontFeatures(Typography.Tabular);
        context.DrawText(formatted, new Point(at.X - formatted.Width * alignX, at.Y - formatted.Height * alignY));
    }

    /// <summary>Right-aligned 12 px text placed by its baseline.</summary>
    public static void Baseline(DrawingContext context, string? text, Typeface face, IBrush brush, Point at)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width, at.Y - formatted.Baseline));
    }
}
