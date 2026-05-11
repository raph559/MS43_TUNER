using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The pinned tiles' grid: tiles at least 150 px wide fill each row, and the last row's tiles share its
/// width. The frame is the view's (a 1 px Hairline, 12 px radius); each tile draws its own left and top Hairline.
/// </summary>
public sealed class TileGrid : Panel
{
    private const double MinTile = 150;

    private (int PerRow, int Rows) Shape(double width)
    {
        var count = Children.Count(child => child.IsVisible);
        var perRow = Math.Max(1, Math.Min(count, (int)Math.Floor(width / MinTile)));
        return (perRow, count == 0 ? 0 : (count + perRow - 1) / perRow);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinTile * 8 : availableSize.Width;
        var (perRow, rows) = Shape(width);
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width / perRow, double.PositiveInfinity));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, rows * height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (perRow, rows) = Shape(finalSize.Width);
        var height = rows == 0 ? 0 : finalSize.Height / rows;
        var visible = Children.Where(child => child.IsVisible).ToList();
        for (var i = 0; i < visible.Count; i++)
        {
            var row = i / perRow;
            var inRow = Math.Min(perRow, visible.Count - row * perRow);
            var width = finalSize.Width / inRow;
            var column = i % perRow;
            visible[i].Arrange(new Rect(Math.Round(column * width), row * height, Math.Round((column + 1) * width) - Math.Round(column * width), height));
        }

        return finalSize;
    }
}
