using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A row whose children each take their own width plus an equal share of the room left (CSS flex: 1 1 auto), so the
/// segments of Compare's kind filter fit "All" and "Curves" alike; when the row is too narrow, every child gives up
/// width in proportion.
/// </summary>
public sealed class FlexRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FlexRow, double>(nameof(Spacing));

    static FlexRow()
    {
        AffectsMeasure<FlexRow>(SpacingProperty);
    }

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        var width = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        width += Spacing * Math.Max(0, Children.Count - 1);
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var natural = Children.Sum(child => child.DesiredSize.Width);
        var room = finalSize.Width - Spacing * (Children.Count - 1);
        var share = room >= natural ? (room - natural) / Children.Count : 0;
        var scale = room >= natural || natural <= 0 ? 1 : room / natural;
        var x = 0.0;
        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width * scale + share;
            child.Arrange(new Rect(Math.Round(x), 0, Math.Round(x + width) - Math.Round(x), finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }
}
