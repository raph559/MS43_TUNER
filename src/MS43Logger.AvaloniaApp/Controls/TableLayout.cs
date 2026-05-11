using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A table's axis line, its scrolling grid and its key line, top to bottom. The grid's scroll viewer takes the
/// height it needs up to what the other rows leave, and the key line follows right under it: it never scrolls away, and
/// it never floats far below a short curve.
/// </summary>
public sealed class TableLayout : Panel
{
    /// <summary>The space left for the grid's scroll viewer at the last measure; MapGrid fits its rows to it.</summary>
    public Size Fit { get; private set; } = new(double.PositiveInfinity, double.PositiveInfinity);

    protected override Size MeasureOverride(Size availableSize)
    {
        var scroller = Children.OfType<ScrollViewer>().FirstOrDefault();
        double others = 0, width = 0;
        foreach (var child in Children.Where(child => child != scroller))
        {
            child.Measure(availableSize);
            others += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }

        if (scroller is null)
        {
            return new Size(width, others);
        }

        var fit = new Size(availableSize.Width, Math.Max(0, availableSize.Height - others));
        if (fit != Fit)
        {
            // The grid inside measures with unbounded space and reads the fit instead, so it has to measure again.
            Fit = fit;
            (scroller.Content as Layoutable)?.InvalidateMeasure();
        }

        scroller.Measure(fit);
        return new Size(Math.Max(width, scroller.DesiredSize.Width), others + scroller.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }

        return finalSize;
    }
}
