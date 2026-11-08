using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// Live's readout band (layout B): five cells, rpm, load, advance, gear with speed, and knock,
/// framed by one 1 px Hairline with a 12 px radius and Hairline dividers. Wide, they stand in four columns (18: 12: 12: 14,
/// gear and speed over knock in the last, knock the taller); <see cref="Stacked"/> (below 1200 px) keeps rpm on the left
/// and puts load and advance over gear and speed beside knock.
/// </summary>
public sealed class ReadoutBand : Panel
{
    public static readonly StyledProperty<bool> StackedProperty = AvaloniaProperty.Register<ReadoutBand, bool>(nameof(Stacked));

    private const double WideHeight = 252;
    private const double StackedHeight = 280;
    private const double TopRow = 144;

    // Wide, the gear and speed cell takes this share of the last column; knock, the rest.
    private const double GearShare = 0.4;

    static ReadoutBand()
    {
        AffectsMeasure<ReadoutBand>(StackedProperty);
        AffectsRender<ReadoutBand>(StackedProperty);
    }

    private readonly BandLines _lines;

    public ReadoutBand()
    {
        // Panel's own Render is sealed: the frame and dividers are a child drawn under the cells.
        _lines = new BandLines(this);
        Children.Add(_lines);
    }

    private IReadOnlyList<Control> CellControls => Children.Where(child => !ReferenceEquals(child, _lines)).ToList();

    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1392 : availableSize.Width;
        var cells = Cells(width, double.PositiveInfinity);
        var height = Stacked ? StackedHeight : WideHeight;
        var children = CellControls;
        for (var i = 0; i < children.Count && i < cells.Length; i++)
        {
            children[i].Measure(new Size(cells[i].Width, double.PositiveInfinity));
        }

        if (children.Count >= 5)
        {
            height = Stacked
                ? Math.Max(height, Math.Max(children[0].DesiredSize.Height, TopRow + Math.Max(children[3].DesiredSize.Height, children[4].DesiredSize.Height)))
                : Math.Max(height, Math.Max(children.Take(3).Max(child => child.DesiredSize.Height), children[3].DesiredSize.Height + children[4].DesiredSize.Height));
        }

        _lines.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cells = Cells(finalSize.Width, finalSize.Height);
        var children = CellControls;
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Arrange(i < cells.Length ? cells[i] : default);
        }

        _lines.Arrange(new Rect(finalSize));
        _lines.InvalidateVisual();
        return finalSize;
    }

    // The cells' rectangles, rpm, load, advance, gear, speed, on whole pixels so the dividers stay crisp.
    private Rect[] Cells(double width, double height)
    {
        double R(double value) => Math.Round(value);
        if (Stacked)
        {
            var left = R(width * 0.395);
            var right = width - left;
            var half = R(right / 2);
            var gear = R(right * 0.5);
            var top = double.IsInfinity(height) ? TopRow : Math.Min(TopRow, height);
            var bottom = double.IsInfinity(height) ? double.PositiveInfinity : height - top;
            return
            [
                new Rect(0, 0, left, height),
                new Rect(left, 0, half, top),
                new Rect(left + half, 0, right - half, top),
                new Rect(left, top, gear, bottom),
                new Rect(left + gear, top, right - gear, bottom),
            ];
        }

        var unit = width / 56;
        var x1 = R(unit * 18);
        var x2 = R(unit * 30);
        var x3 = R(unit * 42);
        var halfHeight = double.IsInfinity(height) ? double.PositiveInfinity : R(height * GearShare);
        var lower = double.IsInfinity(height) ? double.PositiveInfinity : height - halfHeight;
        return
        [
            new Rect(0, 0, x1, height),
            new Rect(x1, 0, x2 - x1, height),
            new Rect(x2, 0, x3 - x2, height),
            new Rect(x3, 0, width - x3, double.IsInfinity(halfHeight) ? height : halfHeight),
            new Rect(x3, double.IsInfinity(halfHeight) ? 0 : halfHeight, width - x3, lower),
        ];
    }

    private void DrawLines(DrawingContext context, Size size)
    {
        var line = new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1);
        context.DrawRectangle(null, line, new RoundedRect(new Rect(size).Deflate(0.5), 12));
        var cells = Cells(size.Width, size.Height);

        // Dividers on the cells' left and top edges, inside the frame.
        void Vertical(double x, double top, double bottom) => context.DrawLine(line, new Point(x + 0.5, top), new Point(x + 0.5, bottom));
        void Horizontal(double y, double left, double right) => context.DrawLine(line, new Point(left, y + 0.5), new Point(right, y + 0.5));
        Vertical(cells[1].Left, 1, cells[1].Bottom);
        Vertical(cells[2].Left, 1, cells[2].Bottom);
        if (Stacked)
        {
            Vertical(cells[1].Left, cells[3].Top, size.Height - 1);
            Horizontal(cells[3].Top, cells[3].Left, size.Width - 1);
            Vertical(cells[4].Left, cells[4].Top, size.Height - 1);
        }
        else
        {
            Vertical(cells[3].Left, 1, size.Height - 1);
            Horizontal(cells[4].Top, cells[4].Left, size.Width - 1);
        }
    }

    // The band's frame and dividers, drawn under the cells.
    private sealed class BandLines : Control
    {
        private readonly ReadoutBand _band;

        public BandLines(ReadoutBand band)
        {
            _band = band;
            IsHitTestVisible = false;
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }

        public override void Render(DrawingContext context) => _band.DrawLines(context, Bounds.Size);
    }
}
