using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A channel's trend in the Live list: the samples of the trend window placed by time, an Ink2 1.25 px line
/// and an Ink1 dot at the newest sample, both Ink3 once the values are frozen. Never orange: a row is data, not now. A
/// flat channel stays flat because the vertical span is never less than its unit's minimum (5 °C, 1 V, 2 %, 100 rpm,
/// 10 mg/stk, 2 kPa).
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<SampleHistory?> HistoryProperty = AvaloniaProperty.Register<Sparkline, SampleHistory?>(nameof(History));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<Sparkline, int>(nameof(Version));
    public static readonly StyledProperty<string?> UnitsProperty = AvaloniaProperty.Register<Sparkline, string?>(nameof(Units));
    public static readonly StyledProperty<bool> IsStaleProperty = AvaloniaProperty.Register<Sparkline, bool>(nameof(IsStale));

    static Sparkline()
    {
        AffectsRender<Sparkline>(HistoryProperty, VersionProperty, UnitsProperty, IsStaleProperty);
    }

    public Sparkline()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public SampleHistory? History { get => GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }

    /// <summary>Bind to the history's version so the line redraws on every sample.</summary>
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>The channel's unit, which sets the smallest vertical span.</summary>
    public string? Units { get => GetValue(UnitsProperty); set => SetValue(UnitsProperty, value); }

    public bool IsStale { get => GetValue(IsStaleProperty); set => SetValue(IsStaleProperty, value); }

    /// <summary>The smallest span the line spreads over, by unit, so sensor noise does not fill the row.</summary>
    public static double MinimumSpan(string? units) => units?.Trim() switch
    {
        "°C" => 5,
        "V" => 1,
        "%" => 2,
        "rpm" => 100,
        "mg/stk" => 10,
        "kPa" => 2,
        _ => 0,
    };

    public override void Render(DrawingContext context)
    {
        if (History is not { Latest: { } latest } history || Bounds.Width <= 0 || Bounds.Height <= 4)
        {
            return;
        }

        var points = history.Window(latest);
        if (points.Count == 0)
        {
            return;
        }

        double low = points.Min(point => point.Value), high = points.Max(point => point.Value);
        var minimum = Math.Max(MinimumSpan(Units), Math.Max(Math.Abs(high), Math.Abs(low)) * 0.02);
        if (high - low < minimum)
        {
            var centre = (high + low) / 2;
            (low, high) = (centre - Math.Max(minimum, 1e-9) / 2, centre + Math.Max(minimum, 1e-9) / 2);
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        var from = latest - history.Span;
        Point At((DateTimeOffset Time, double Value) point) => new(
            Math.Clamp((point.Time - from).TotalSeconds / history.Span.TotalSeconds, 0, 1) * (width - 3) + 1,
            height - 2 - (point.Value - low) / (high - low) * (height - 4));

        var ink = ThemeBrushes.Get(this, IsStale ? "Ink3Brush" : "Ink2Brush");
        if (points.Count > 1)
        {
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(At(points[0]), false);
                foreach (var point in points.Skip(1))
                {
                    g.LineTo(At(point));
                }

                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(ink, 1.25, lineJoin: PenLineJoin.Round), line);
        }

        context.DrawEllipse(ThemeBrushes.Get(this, IsStale ? "Ink3Brush" : "Ink1Brush"), null, At(points[^1]), 2, 2);
    }
}
