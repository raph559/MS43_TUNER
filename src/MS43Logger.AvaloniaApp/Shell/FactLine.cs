using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace MS43Logger.AvaloniaApp.Shell;

/// <summary>
/// One row of facts, left to right. When the row runs short of width its last fact gives way first (it trims, down to
/// its MinWidth); when even that does not fit, the whole row steps aside instead of overlapping or clipping, so its
/// facts must also be reachable elsewhere (the file block repeats them in its tooltip).
/// </summary>
public sealed class FactLine : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FactLine, double>(nameof(Spacing));

    static FactLine()
    {
        AffectsMeasure<FactLine>(SpacingProperty);

        // A row that stepped aside arranges its facts outside its own box; the clip keeps them from drawing.
        ClipToBoundsProperty.OverrideDefaultValue<FactLine>(true);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>False when the row stepped aside at the last measure.</summary>
    public bool Fits { get; private set; } = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Children.Where(child => child.IsVisible).ToList();
        if (shown.Count == 0)
        {
            Fits = true;
            return default;
        }

        var used = 0.0;
        var height = 0.0;
        foreach (var child in shown.Take(shown.Count - 1))
        {
            child.Measure(Size.Infinity);
            used += child.DesiredSize.Width + Spacing;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var last = shown[^1];
        var room = availableSize.Width - used;
        Fits = room >= last.MinWidth + last.Margin.Left + last.Margin.Right;
        if (!Fits)
        {
            last.Measure(Size.Infinity);
            return default;
        }

        last.Measure(new Size(room, availableSize.Height));
        return new Size(used + last.DesiredSize.Width, Math.Max(height, last.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(child => child.IsVisible).ToList();
        var x = 0.0;
        for (var i = 0; i < shown.Count; i++)
        {
            var child = shown[i];
            if (!Fits)
            {
                // Out of sight past the right edge, whatever size the parent gives the row; the clip hides it.
                child.Arrange(new Rect(new Point(finalSize.Width + Spacing, 0), child.DesiredSize));
                continue;
            }

            var width = i == shown.Count - 1 ? Math.Clamp(finalSize.Width - x, 0, child.DesiredSize.Width) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }
}
