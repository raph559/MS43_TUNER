using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// Two runs against rpm or time on the Tune curve's frame, grid and axes: A in Ink1 2 px solid,
/// B in Ink2 1.5 px dashed, so shape carries the difference, with a legend line at the top left. A run still being
/// recorded draws A in Ink2 and ends it in the Now dot, an Ink3 ring once samples stop. Traces are data, never orange.
/// </summary>
public sealed class RunChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<Point>?> TraceAProperty = AvaloniaProperty.Register<RunChart, IReadOnlyList<Point>?>(nameof(TraceA));
    public static readonly StyledProperty<IReadOnlyList<Point>?> TraceBProperty = AvaloniaProperty.Register<RunChart, IReadOnlyList<Point>?>(nameof(TraceB));
    public static readonly StyledProperty<string?> LabelAProperty = AvaloniaProperty.Register<RunChart, string?>(nameof(LabelA));
    public static readonly StyledProperty<string?> LabelBProperty = AvaloniaProperty.Register<RunChart, string?>(nameof(LabelB));
    public static readonly StyledProperty<string?> XUnitsProperty = AvaloniaProperty.Register<RunChart, string?>(nameof(XUnits));
    public static readonly StyledProperty<string?> YUnitsProperty = AvaloniaProperty.Register<RunChart, string?>(nameof(YUnits));
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<RunChart, bool>(nameof(IsLive));
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<RunChart, bool>(nameof(IsRunning));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<RunChart, int>(nameof(Version));
    public static readonly StyledProperty<double?> XMinimumProperty = AvaloniaProperty.Register<RunChart, double?>(nameof(XMinimum));
    public static readonly StyledProperty<double?> XMaximumProperty = AvaloniaProperty.Register<RunChart, double?>(nameof(XMaximum));
    public static readonly StyledProperty<double?> YMinimumProperty = AvaloniaProperty.Register<RunChart, double?>(nameof(YMinimum));
    public static readonly StyledProperty<double?> YMaximumProperty = AvaloniaProperty.Register<RunChart, double?>(nameof(YMaximum));

    // Five steps across x, so a pull's 2000 to 7000 rpm is ticked every 1000 rpm; four up y, as the Tune curve.
    private const int XSteps = 5;
    private const double LegendSample = 16;

    static RunChart()
    {
        AffectsRender<RunChart>(TraceAProperty, TraceBProperty, LabelAProperty, LabelBProperty, XUnitsProperty, YUnitsProperty, IsLiveProperty, IsRunningProperty, VersionProperty,
            XMinimumProperty, XMaximumProperty, YMinimumProperty, YMaximumProperty);
    }

    public RunChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Run A: x is rpm or seconds from the start, y the chosen value.</summary>
    public IReadOnlyList<Point>? TraceA { get => GetValue(TraceAProperty); set => SetValue(TraceAProperty, value); }

    /// <summary>Run B, the one compared against (or the baseline behind a live run).</summary>
    public IReadOnlyList<Point>? TraceB { get => GetValue(TraceBProperty); set => SetValue(TraceBProperty, value); }

    public string? LabelA { get => GetValue(LabelAProperty); set => SetValue(LabelAProperty, value); }

    public string? LabelB { get => GetValue(LabelBProperty); set => SetValue(LabelBProperty, value); }

    /// <summary>The unit at the x axis's end ("rpm", "s").</summary>
    public string? XUnits { get => GetValue(XUnitsProperty); set => SetValue(XUnitsProperty, value); }

    /// <summary>The unit over the y axis ("kW").</summary>
    public string? YUnits { get => GetValue(YUnitsProperty); set => SetValue(YUnitsProperty, value); }

    /// <summary>Samples are arriving for A: it is drawn as a run being recorded and its newest point is the Now dot.</summary>
    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    /// <summary>A is a run being recorded even while no sample arrives: Ink2, its newest point an Ink3 ring unless live.</summary>
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }

    /// <summary>Raised when a trace list changed in place, so the chart draws it again.</summary>
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>
    /// Fixed ends of the axes, each null to fit the data: a run being recorded keeps its axes for the run, so the chart never
    /// rescales with each sample; a stored chart fits its traces.
    /// </summary>
    public double? XMinimum { get => GetValue(XMinimumProperty); set => SetValue(XMinimumProperty, value); }

    public double? XMaximum { get => GetValue(XMaximumProperty); set => SetValue(XMaximumProperty, value); }

    public double? YMinimum { get => GetValue(YMinimumProperty); set => SetValue(YMinimumProperty, value); }

    public double? YMaximum { get => GetValue(YMaximumProperty); set => SetValue(YMaximumProperty, value); }

    /// <summary>The x axis's tick labels as drawn, left to right.</summary>
    public IReadOnlyList<string> XTickLabels => Layout() is { } chart ? chart.XTicks.Select(tick => ChartScale.Format(tick, chart.XStep)).ToList() : [];

    /// <summary>The y axis's tick labels as drawn, bottom to top.</summary>
    public IReadOnlyList<string> YTickLabels => Layout() is { } chart ? chart.YTicks.Select(tick => ChartScale.Format(tick, chart.YStep)).ToList() : [];

    /// <summary>The data area: the first and last x ticks sit on its left and right edges, the first and last y ticks on its bottom and top.</summary>
    public Rect PlotRect => Layout()?.Plot ?? default;

    /// <summary>The legend's labels as drawn, A then B: each trimmed in the middle so the line fits over the plot.</summary>
    public IReadOnlyList<string> LegendLabels => Layout() is { } chart ? LegendItems(chart.Plot, UiFace(), ThemeBrushes.Get(this, "Ink2Brush")).Select(item => item.Label).ToList() : [];

    /// <summary>Where the legend line is drawn: from its first stroke sample to the end of its last label.</summary>
    public Rect LegendRect
    {
        get
        {
            if (Layout() is not { } chart || LegendItems(chart.Plot, UiFace(), ThemeBrushes.Get(this, "Ink2Brush")) is not { Count: > 0 } items)
            {
                return default;
            }

            var start = chart.Plot.Left + 8;
            return new Rect(start, chart.Plot.Top - 22, items[^1].X + items[^1].Text.Width - start, 16);
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var pane = ThemeBrushes.Color(this, "PaneBrush");
        context.DrawRectangle(new SolidColorBrush(pane), new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1), new RoundedRect(new Rect(size).Deflate(0.5), 8));
        if (Layout() is not { } chart)
        {
            return;
        }

        var ink1 = ThemeBrushes.Get(this, "Ink1Brush");
        var ink2 = ThemeBrushes.Get(this, "Ink2Brush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var data = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var ui = UiFace();
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
        ChartScale.Baseline(context, YUnits, ui, ink3, new Point(plot.Left - 6, plot.Top - 10));
        ChartScale.Text(context, XUnits, ui, ink3, new Point(plot.Right, plot.Bottom + 18), 1, 0);

        var recording = IsLive || IsRunning;
        var penA = new Pen(recording ? ink2 : ink1, 2, lineJoin: PenLineJoin.Round);
        var penB = new Pen(ink2, 1.5, new DashStyle([4, 2], 0), lineJoin: PenLineJoin.Round);

        // B behind A, so where they meet the solid run reads on top; a fixed range keeps what runs past it inside the plot.
        using (context.PushClip(plot.Inflate(2)))
        {
            Trace(context, TraceB, penB, chart);
            Trace(context, TraceA, penA, chart);
        }

        Legend(context, LegendItems(plot, ui, ink2), penA, penB, plot.Top - 10);

        if (recording && Newest(TraceA) is { } last)
        {
            // The newest sample: a round Now dot with a Pane halo; an Ink3 ring once samples stop (as CurveChart).
            var at = chart.At(last);
            var halo = new SolidColorBrush(pane);
            if (IsLive)
            {
                context.DrawEllipse(halo, null, at, 6, 6);
                context.DrawEllipse(ThemeBrushes.Get(this, "NowBrush"), null, at, 4, 4);
            }
            else
            {
                context.DrawEllipse(null, new Pen(halo, 5), at, 3.25, 3.25);
                context.DrawEllipse(null, new Pen(ink3, 1.5), at, 3.25, 3.25);
            }
        }
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static Point? Newest(IReadOnlyList<Point>? trace)
    {
        for (var i = (trace?.Count ?? 0) - 1; i >= 0; i--)
        {
            if (IsFinite(trace![i]))
            {
                return trace[i];
            }
        }

        return null;
    }

    // A gap in the samples (a NaN) breaks the line rather than bridging it.
    private static void Trace(DrawingContext context, IReadOnlyList<Point>? trace, Pen pen, Chart chart)
    {
        if (trace is null || trace.Count < 2)
        {
            return;
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            var open = false;
            foreach (var point in trace)
            {
                if (!IsFinite(point))
                {
                    if (open)
                    {
                        g.EndFigure(false);
                        open = false;
                    }

                    continue;
                }

                if (!open)
                {
                    g.BeginFigure(chart.At(point), false);
                    open = true;
                }
                else
                {
                    g.LineTo(chart.At(point));
                }
            }

            if (open)
            {
                g.EndFigure(false);
            }
        }

        context.DrawGeometry(null, pen, line);
    }

    // One line in UI 12 Ink2 on the units' baseline: for each named run, a 16 px sample of its stroke at mid x-height,
    // then its label.
    private static void Legend(DrawingContext context, IReadOnlyList<LegendItem> items, Pen penA, Pen penB, double baseline)
    {
        foreach (var item in items)
        {
            var middle = baseline - 4;
            context.DrawLine(item.IsA ? penA : penB, new Point(item.X - LegendSample - 6, middle), new Point(item.X - 6, middle));
            context.DrawText(item.Text, new Point(item.X, baseline - item.Text.Baseline));
        }
    }

    // The legend laid out over the plot: a label that does not fit is trimmed in the middle, the shorter one kept whole while
    // it fits in half the room. The full names stay in the A − B panel.
    private List<LegendItem> LegendItems(Rect plot, Typeface ui, IBrush ink)
    {
        var named = new[] { (Label: LabelA, IsA: true), (Label: LabelB, IsA: false) }.Where(pair => !string.IsNullOrEmpty(pair.Label)).ToList();
        FormattedText Text(string label) => new(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ui, 12, ink);
        var room = plot.Width - 8 - named.Count * (LegendSample + 6) - Math.Max(0, named.Count - 1) * 16;
        var widths = named.Select(pair => Text(pair.Label!).Width).ToArray();
        var limits = widths.ToArray();
        if (widths.Sum() > room)
        {
            for (var i = 0; i < named.Count; i++)
            {
                var others = widths.Where((_, j) => j != i).Sum();
                var half = room / named.Count;
                limits[i] = widths[i] <= half ? widths[i] : Math.Max(0, others <= half ? room - others : half);
            }
        }

        var items = new List<LegendItem>();
        var x = plot.Left + 8;
        for (var i = 0; i < named.Count; i++)
        {
            var label = Fit(named[i].Label!, limits[i], text => Text(text).Width);
            var text = Text(label);
            x += LegendSample + 6;
            items.Add(new LegendItem(label, text, x, named[i].IsA));
            x += text.Width + 16;
        }

        return items;
    }

    // The label as it fits in max: whole, or its start and end around an ellipsis.
    private static string Fit(string label, double max, Func<string, double> width)
    {
        if (width(label) <= max)
        {
            return label;
        }

        for (var keep = label.Length - 1; keep > 1; keep--)
        {
            var head = (keep + 1) / 2;
            var trimmed = label[..head].TrimEnd() + "…" + label[(label.Length - (keep - head))..].TrimStart();
            if (width(trimmed) <= max)
            {
                return trimmed;
            }
        }

        return "…";
    }

    private Typeface UiFace() => new(ThemeBrushes.Font(this, "UiFont"));

    private sealed record LegendItem(string Label, FormattedText Text, double X, bool IsA);

    private Chart? Layout()
    {
        if (Bounds.Width < 40 || Bounds.Height < 40)
        {
            return null;
        }

        var points = (TraceA ?? []).Concat(TraceB ?? []).Where(IsFinite).ToList();
        if (points.Count == 0)
        {
            return null;
        }

        var (xMin, xMax, xStep, xTicks) = ChartScale.Ticks(XMinimum ?? points.Min(point => point.X), XMaximum ?? points.Max(point => point.X), XSteps);
        var (yMin, yMax, yStep, yTicks) = ChartScale.Ticks(YMinimum ?? points.Min(point => point.Y), YMaximum ?? points.Max(point => point.Y));
        var plot = new Rect(44, 22, Math.Max(10, Bounds.Width - 60), Math.Max(10, Bounds.Height - 62));
        return new Chart(plot, xMin, xMax, yMin, yMax, xStep, yStep, xTicks, yTicks);
    }

    private sealed record Chart(Rect Plot, double XMin, double XMax, double YMin, double YMax, double XStep, double YStep,
        IReadOnlyList<double> XTicks, IReadOnlyList<double> YTicks)
    {
        public double X(double value) => Plot.Left + (value - XMin) / (XMax - XMin) * Plot.Width;

        public double Y(double value) => Plot.Bottom - (value - YMin) / (YMax - YMin) * Plot.Height;

        public Point At(Point value) => new(X(value.X), Y(value.Y));
    }
}
