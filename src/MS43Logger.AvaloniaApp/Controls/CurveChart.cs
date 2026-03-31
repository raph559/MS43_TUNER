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
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<CurveChart, TableEditorViewModel?>(nameof(Editor));

    public CurveChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public TableEditorViewModel? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is TableEditorViewModel old)
            {
                old.PropertyChanged -= OnEditorChanged;
            }

            if (change.NewValue is TableEditorViewModel editor)
            {
                editor.PropertyChanged += OnEditorChanged;
            }

            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.DrawRectangle(ThemeBrushes.Get(this, "SunkenBrush"), null, new Rect(size), 8, 8);
        if (Editor?.Values is not TableValues values)
        {
            return;
        }

        var vertical = values.Cols == 1;
        var count = vertical ? values.Rows : values.Cols;
        var ys = Enumerable.Range(0, count).Select(i => vertical ? values.Physical[i, 0] : values.Physical[0, i]).ToArray();
        var axis = vertical ? values.Y : values.X;
        var xs = axis.Values.Count == count && axis.Values.All(double.IsFinite) && axis.Values.Distinct().Count() == count
            ? axis.Values.ToArray()
            : Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        var finite = ys.Where(double.IsFinite).ToList();
        if (count < 1 || finite.Count == 0)
        {
            return;
        }

        double xMin = xs.Min(), xMax = xs.Max(), yMin = finite.Min(), yMax = finite.Max();
        if (xMax - xMin < 1e-9)
        {
            xMax = xMin + 1;
        }

        if (yMax - yMin < 1e-9)
        {
            yMin -= 1;
            yMax += 1;
        }

        var plot = new Rect(40, 14, Math.Max(10, size.Width - 56), Math.Max(10, size.Height - 40));
        Point At(int i) => new(plot.Left + (xs[i] - xMin) / (xMax - xMin) * plot.Width, plot.Bottom - (ys[i] - yMin) / (yMax - yMin) * plot.Height);

        var grid = new Pen(ThemeBrushes.Get(this, "BezelBrush"), 1);
        var font = new Typeface(ThemeBrushes.Font(this, "DisplayFont"));
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4;
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(context, Format(yMax - (yMax - yMin) * i / 4, yMax - yMin), font, ink3, new Point(plot.Left - 6, y - 7), 1);
        }

        Text(context, Format(xMin, xMax - xMin), font, ink3, new Point(plot.Left, plot.Bottom + 6), 0);
        Text(context, Format(xMax, xMax - xMin), font, ink3, new Point(plot.Right, plot.Bottom + 6), 1);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            var started = false;
            for (var i = 0; i < count; i++)
            {
                if (!double.IsFinite(ys[i]))
                {
                    continue;
                }

                if (!started)
                {
                    g.BeginFigure(At(i), false);
                    started = true;
                }
                else
                {
                    g.LineTo(At(i));
                }
            }

            if (started)
            {
                g.EndFigure(false);
            }
        }

        var ink2 = ThemeBrushes.Get(this, "Ink2Brush");
        context.DrawGeometry(null, new Pen(ink2, 2), line);
        for (var i = 0; i < count; i++)
        {
            if (!double.IsFinite(ys[i]))
            {
                continue;
            }

            var selected = Editor.Selection is CellRange range && (vertical ? range.Contains(i, 0) : range.Contains(0, i));
            context.DrawEllipse(selected ? ThemeBrushes.Get(this, "InkBrush") : ink2, null, At(i), selected ? 4.5 : 3, selected ? 4.5 : 3);
        }
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
