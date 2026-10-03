using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The A − B key under the Compare grid: a 10 px bar with an Edge border showing only the diverging
/// mixes the item uses, from its lowest A − B to its highest, through the Pane colour at zero.
/// </summary>
public sealed class DeltaScale : Control
{
    public static readonly StyledProperty<double> LowProperty = AvaloniaProperty.Register<DeltaScale, double>(nameof(Low));
    public static readonly StyledProperty<double> HighProperty = AvaloniaProperty.Register<DeltaScale, double>(nameof(High));

    private const int Stops = 16;

    static DeltaScale()
    {
        AffectsRender<DeltaScale>(LowProperty, HighProperty);
    }

    public DeltaScale()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The lowest A − B as a signed fraction of the largest difference, -1 to 1.</summary>
    public double Low { get => GetValue(LowProperty); set => SetValue(LowProperty, value); }

    public double High { get => GetValue(HighProperty); set => SetValue(HighProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(200, 10);

    public override void Render(DrawingContext context)
    {
        var pane = ThemeBrushes.Color(this, "PaneBrush");
        var below = ThemeBrushes.Color(this, "DivBelowBrush");
        var above = ThemeBrushes.Color(this, "DivAboveBrush");
        var (low, high) = (Math.Clamp(Low, -1, 1), Math.Clamp(High, -1, 1));
        if (high < low)
        {
            (low, high) = (high, low);
        }

        var points = new System.Collections.Generic.List<(double At, Color Colour)>();
        for (var i = 0; i <= Stops; i++)
        {
            var at = (double)i / Stops;
            points.Add((at, MapRamp.Diverging(low + at * (high - low), pane, below, above)));
        }

        // Zero, when the range crosses it, is exactly the Pane colour, as on the grid.
        if (low < 0 && high > 0)
        {
            points.Add((-low / (high - low), pane));
        }

        var stops = new GradientStops();
        foreach (var (at, colour) in System.Linq.Enumerable.OrderBy(points, point => point.At))
        {
            stops.Add(new GradientStop(colour, at));
        }

        var fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = stops };
        context.DrawRectangle(fill, new Pen(ThemeBrushes.Get(this, "EdgeBrush"), 1), new RoundedRect(new Rect(Bounds.Size).Deflate(0.5), 4));
    }
}
