using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// Two runs against rpm or time (performance spec 9.2) on the Tune curve's frame, grid and axes: A in Ink1 2 px solid,
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
    /// rescales with each sample (minor review, finding 49); a stored chart fits its traces.
    /// </summary>
    public double? XMinimum { get => GetValue(XMinimumProperty); set => SetValue(XMinimumProperty, value); }

    public double? XMaximum { get => GetValue(XMaximumProperty); set => SetValue(XMaximumProperty, value); }

    public double? YMinimum { get => GetValue(YMinimumProperty); set => SetValue(YMinimumProperty, value); }

    public double? YMaximum { get => GetValue(YMaximumProperty); set => SetValue(YMaximumProperty, value); }

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
    // it fits in half the room (minor review, finding 48). The full names stay in the B − A panel.
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
    }
}
