using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>A curve (or an axis) as a line over its breakpoints, with the selected points highlighted.</summary>
public sealed class CurveChart : Control
{

    public CurveChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Enough decimals that neighbouring scale labels differ.</summary>
    private static string Format(double value, double span) =>
        value.ToString(span >= 40 ? "0" : span >= 4 ? "0.#" : "0.##", CultureInfo.InvariantCulture).Replace('-', '−');

    private static void Text(DrawingContext context, string text, Typeface font, IBrush brush, Point at, double align)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11.5, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width * align, at.Y));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
