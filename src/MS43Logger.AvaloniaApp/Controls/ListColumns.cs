using System;
using Avalonia;
using Avalonia.Controls;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The Live list's two columns (system 5.6): side by side with a 24 px gap, each as tall as the room it is given, so each
/// list scrolls on its own; <see cref="Stacked"/> (below 1200 px) puts the second under the first at their full heights,
/// for one scroll area around both.
/// </summary>
public sealed class ListColumns : Panel
{
    public static readonly StyledProperty<bool> StackedProperty = AvaloniaProperty.Register<ListColumns, bool>(nameof(Stacked));

    private const double Gap = 24;

    static ListColumns()
    {
        AffectsMeasure<ListColumns>(StackedProperty);
    }

    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        if (Stacked)
        {
            var height = 0.0;
            foreach (var child in Children)
            {
                child.Measure(new Size(width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
            }

            return new Size(width, height);
        }

        var column = Math.Max(0, (width - Gap) / 2);
        var tallest = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(column, availableSize.Height));
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }

        return new Size(width, double.IsInfinity(availableSize.Height) ? tallest : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Stacked)
        {
            var top = 0.0;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(0, top, finalSize.Width, child.DesiredSize.Height));
                top += child.DesiredSize.Height;
            }

            return finalSize;
        }

        var column = Math.Max(0, (finalSize.Width - Gap) / 2);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i == 0 ? 0 : column + Gap, 0, column, finalSize.Height));
        }

        return finalSize;
    }
}
