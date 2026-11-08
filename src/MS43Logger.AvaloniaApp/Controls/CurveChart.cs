using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A curve (or an axis) as a chart (system 5.13): square points filled with their cell's map colour and an Ink2 edge,
/// the selected point framed in Ink1, an Ink2 line and axes, a hairline grid at round ticks. The round dot is kept for
/// the live cursor.
/// </summary>
public sealed class CurveChart : Control
{
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<CurveChart, TableEditorViewModel?>(nameof(Editor));

    private const double PointSize = 7;
    private const double Inset = 8;

    public CurveChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public TableEditorViewModel? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    /// <summary>Where point <paramref name="index"/> is drawn: a 7 px square on its breakpoint and value.</summary>
    public Rect PointRect(int index) =>
        Layout() is { } chart && index >= 0 && index < chart.Count && double.IsFinite(chart.Ys[index])
            ? new Rect(chart.At(index) - new Point(PointSize / 2, PointSize / 2), new Size(PointSize, PointSize))
            : default;

    /// <summary>The x axis's tick labels as drawn: breakpoint values, or "1", "5", "10" for an axis's numbered breakpoints.</summary>
    public IReadOnlyList<string> XTickLabels => Layout() is { } chart ? chart.XTicks.Select(tick => ChartScale.Format(tick, chart.XStep)).ToList() : [];

    /// <summary>The unit at the x axis's end: the row axis's unit, or "breakpoint" for an axis.</summary>
    public string? XAxisUnits => Layout()?.AxisUnits;

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
        var pane = ThemeBrushes.Color(this, "PaneBrush");
        context.DrawRectangle(new SolidColorBrush(pane), new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1), new RoundedRect(new Rect(size).Deflate(0.5), 8));
        if (Editor is not { } editor || Layout() is not { } chart)
        {
            return;
        }

        var ink1 = ThemeBrushes.Get(this, "Ink1Brush");
        var ink2 = ThemeBrushes.Get(this, "Ink2Brush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var data = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var ui = new Typeface(ThemeBrushes.Font(this, "UiFont"));
        var plot = chart.Plot;

        var hairline = new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1);
        foreach (var tick in chart.YTicks)
        {
            var y = chart.Y(tick);
            context.DrawLine(hairline, new Point(plot.Left, y), new Point(plot.Right, y));
            ChartScale.Text(context, ChartScale.Format(tick, chart.YStep), data, ink3, new Point(plot.Left - 6, y), 1, 0.5);
        }

        foreach (var tick in chart.XTicks)
        {
            ChartScale.Text(context, ChartScale.Format(tick, chart.XStep), data, ink3, new Point(chart.X(tick), plot.Bottom + 4), 0.5, 0);
        }

        // The y axis rises 6 px above the top tick, and its unit sits over it with the baseline 10 px above that tick.
        var axes = new Pen(ink2, 1.5);
        context.DrawLine(axes, plot.BottomLeft, plot.TopLeft - new Point(0, 6));
        context.DrawLine(axes, plot.BottomLeft, plot.BottomRight);
        ChartScale.Baseline(context, editor.Units, ui, ink3, new Point(plot.Left - 6, plot.Top - 10));
        ChartScale.Text(context, chart.AxisUnits, ui, ink3, new Point(plot.Right, plot.Bottom + 18), 1, 0);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            var started = false;
            for (var i = 0; i < chart.Count; i++)
            {
                if (!double.IsFinite(chart.Ys[i]))
                {
                    continue;
                }

                if (!started)
                {
                    g.BeginFigure(chart.At(i), false);
                    started = true;
                }
                else
                {
                    g.LineTo(chart.At(i));
                }
            }

            if (started)
            {
                g.EndFigure(false);
            }
        }

        context.DrawGeometry(null, new Pen(ink2, 1.5, lineJoin: PenLineJoin.Round), line);

        var night = ThemeBrushes.IsNight(this);
        var range = editor.ColourRange;
        var edge = new Pen(ink2, 1);
        for (var i = 0; i < chart.Count; i++)
        {
            if (!double.IsFinite(chart.Ys[i]))
            {
                continue;
            }

            var cell = chart.Vertical ? (i, 0) : (0, i);
            var fill = range.Hatched.Contains(cell) ? pane
                : MapRamp.Color(range.IsConstant ? 0.5 : (chart.Ys[i] - range.Min) / (range.Max - range.Min), night);
            var square = PointRect(i);
            context.DrawRectangle(new SolidColorBrush(fill), edge, square.Deflate(0.5));
            if (editor.Selection is CellRange selection && selection.Contains(cell.Item1, cell.Item2))
            {
                context.DrawRectangle(null, new Pen(ink1, 2), square.Inflate(2));
            }
        }

        if (editor.Live is { } live && (chart.Vertical ? live.Fix.Row : live.Fix.Col) is AxisPosition position && double.IsFinite(live.Fix.Value))
        {
            // The engine's position: a round Now dot with a Pane halo; an Ink3 ring once samples stop.
            var at = new Point(chart.XAt(position.Position), chart.Y(Math.Clamp(live.Fix.Value, chart.YMin, chart.YMax)));
            var halo = new SolidColorBrush(pane);
            if (live.IsStale)
            {
                context.DrawEllipse(null, new Pen(halo, 5), at, 3.25, 3.25);
                context.DrawEllipse(null, new Pen(ink3, 1.5), at, 3.25, 3.25);
            }
            else
            {
                context.DrawEllipse(halo, null, at, 6, 6);
                context.DrawEllipse(ThemeBrushes.Get(this, live.IsUnused ? "Ink3Brush" : "NowBrush"), null, at, 4, 4);
            }
        }
    }

    private Chart? Layout()
    {
        if (Editor?.Values is not TableValues values || Bounds.Width < 40 || Bounds.Height < 40)
        {
            return null;
        }

        var vertical = values.Cols == 1;
        var count = vertical ? values.Rows : values.Cols;
        var ys = Enumerable.Range(0, count).Select(i => vertical ? values.Physical[i, 0] : values.Physical[0, i]).ToArray();
        var axis = vertical ? values.Y : values.X;

        // An axis's breakpoints are numbered 1 to n (system 5.13), as its grid's rows are.
        var numbered = vertical && Editor?.NumberRows == true;
        var xs = numbered ? Enumerable.Range(1, count).Select(i => (double)i).ToArray()
            : axis.Values.Count == count && axis.Values.All(double.IsFinite) && axis.Values.Distinct().Count() == count
            ? axis.Values.ToArray()
            : Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        var finite = ys.Where(double.IsFinite).ToList();
        if (count < 1 || finite.Count == 0)
        {
            return null;
        }

        var (yMin, yMax, yStep, yTicks) = ChartScale.Ticks(finite.Min(), finite.Max());
        var (xMin, xMax, xStep, xTicks) = numbered ? Breakpoints(count) : ChartScale.Ticks(xs.Min(), xs.Max());
        var plot = new Rect(44, 22, Math.Max(10, Bounds.Width - 60), Math.Max(10, Bounds.Height - 62));
        return new Chart(plot, xs, ys, count, vertical, xMin, xMax, yMin, yMax, xStep, yStep, xTicks, yTicks, numbered ? "breakpoint" : axis.Units);
    }

    // Numbered breakpoints run from 1 to n, ticked at 1 and then at round steps: 1, 5, 10, 15, 20.
    private static (double Min, double Max, double Step, IReadOnlyList<double> Ticks) Breakpoints(int count)
    {
        var step = ChartScale.Ticks(1, Math.Max(2, count)).Step;
        var ticks = new List<double> { 1 };
        for (var tick = step; tick <= count + 1e-9; tick += step)
        {
            if (tick > 1)
            {
                ticks.Add(tick);
            }
        }

        return (1, Math.Max(2, count), step, ticks);
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private sealed record Chart(Rect Plot, double[] Xs, double[] Ys, int Count, bool Vertical, double XMin, double XMax, double YMin, double YMax,
        double XStep, double YStep, IReadOnlyList<double> XTicks, IReadOnlyList<double> YTicks, string AxisUnits)
    {
        // The points keep 8 px from both ends of the x range, so the first one never sits on the y axis (mockup).
        public double X(double value) => Plot.Left + Inset + (value - XMin) / (XMax - XMin) * (Plot.Width - 2 * Inset);

        public double Y(double value) => Plot.Bottom - (value - YMin) / (YMax - YMin) * Plot.Height;

        public Point At(int index) => new(X(Xs[index]), Y(Ys[index]));

        /// <summary>The x of a fractional breakpoint position, between its two breakpoints.</summary>
        public double XAt(double position)
        {
            var i = Math.Clamp((int)Math.Floor(position), 0, Count - 1);
            var next = Math.Min(i + 1, Count - 1);
            return X(Xs[i] + (position - i) * (Xs[next] - Xs[i]));
        }
    }
}
