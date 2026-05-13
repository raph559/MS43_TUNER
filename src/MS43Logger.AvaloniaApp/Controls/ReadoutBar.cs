using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A readout's range bar in the Tiles band: a Track bar with a Now fill (Ink3 once the link is not live),
/// the rev-limit zone hatched in Ink2 with an Ink1 limit tick, a zero mark for a scale that crosses zero, and the scale's
/// labels under it in Data 12 Ink3. Marks that sit on the fill carry a 1 px Pane casing on each side. The fill clamps at
/// the ends; the reading above it prints the true value.
/// </summary>
public sealed class ReadoutBar : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<ReadoutBar, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ReadoutBar, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<ReadoutBar, double?>(nameof(Value));
    public static readonly StyledProperty<double?> LimitFromProperty = AvaloniaProperty.Register<ReadoutBar, double?>(nameof(LimitFrom));
    public static readonly StyledProperty<double?> ZeroAtProperty = AvaloniaProperty.Register<ReadoutBar, double?>(nameof(ZeroAt));
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<ReadoutBar, double>(nameof(Step));
    public static readonly StyledProperty<double> BarHeightProperty = AvaloniaProperty.Register<ReadoutBar, double>(nameof(BarHeight), 6);
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<ReadoutBar, bool>(nameof(IsLive));

    // The limit tick reaches 4 px past the bar; the labels sit 6 px under it on a 16 px line.
    private const double Overhang = 4;
    private const double LabelGap = 6;
    private const double LabelLine = 16;

    static ReadoutBar()
    {
        AffectsRender<ReadoutBar>(MinimumProperty, MaximumProperty, ValueProperty, LimitFromProperty, ZeroAtProperty, StepProperty, BarHeightProperty, IsLiveProperty);
        AffectsMeasure<ReadoutBar>(BarHeightProperty);
    }

    public ReadoutBar()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Start of the rev-limit zone, or null for none (no bin open).</summary>
    public double? LimitFrom { get => GetValue(LimitFromProperty); set => SetValue(LimitFromProperty, value); }

    /// <summary>A scale that crosses zero fills from zero and marks it.</summary>
    public double? ZeroAt { get => GetValue(ZeroAtProperty); set => SetValue(ZeroAtProperty, value); }

    /// <summary>Labelled ticks every this much from <see cref="Minimum"/>; the ends are always labelled.</summary>
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public double BarHeight { get => GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }

    /// <summary>Samples are arriving: the fill is Now; otherwise Ink3, since orange means now.</summary>
    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    /// <summary>Where the value's end of the fill sits, 0 to 1 along the bar; 0 without a value.</summary>
    public double FillFraction => Value is double value && double.IsFinite(value) ? Fraction(value) : 0;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, Overhang + BarHeight + LabelGap + LabelLine);

    private double Fraction(double value) => Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private double X(double value) => Fraction(value) * Bounds.Width;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var bar = new Rect(0, Overhang, width, BarHeight);
        var track = new RoundedRect(bar, 2);
        var pane = ThemeBrushes.Get(this, "PaneBrush");
        context.DrawRectangle(ThemeBrushes.Get(this, "TrackBrush"), null, track);

        var limit = LimitFrom is double from && from > Minimum && from < Maximum ? Math.Round(X(from)) : (double?)null;
        using (context.PushClip(track))
        {
            if (limit is double start)
            {
                // The hatch of the storage limit on Tune: 1.5 px Ink2 stripes every 4 px.
                using (context.PushClip(new Rect(start, bar.Top, width - start, bar.Height)))
                {
                    var stripe = new Pen(ThemeBrushes.Get(this, "Ink2Brush"), 1.5);
                    for (var x = start - bar.Height; x < width + bar.Height; x += 4)
                    {
                        context.DrawLine(stripe, new Point(x, bar.Bottom), new Point(x + bar.Height, bar.Top));
                    }
                }
            }

            if (Value is double value && double.IsFinite(value))
            {
                var origin = ZeroAt is double zero ? X(zero) : 0;
                var end = X(value);
                var fill = ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink3Brush");
                context.DrawRectangle(fill, null, new Rect(Math.Min(origin, end), bar.Top, Math.Abs(end - origin), bar.Height));
                if (limit is double inZone && end > inZone + 1)
                {
                    // A fill that runs into the limit zone ends on a Pane casing, apart from the stripes.
                    context.DrawRectangle(pane, null, new Rect(end, bar.Top, 1, bar.Height));
                }
            }
        }

        if (limit is double tick)
        {
            Mark(context, tick, Overhang, ThemeBrushes.Get(this, "Ink1Brush"), pane, bar);
        }

        if (ZeroAt is double zeroAt && zeroAt > Minimum && zeroAt < Maximum)
        {
            Mark(context, Math.Round(X(zeroAt)), 3, ThemeBrushes.Get(this, "Ink2Brush"), pane, bar);
        }

        var face = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        foreach (var (value, align) in Labels())
        {
            var text = new FormattedText(value.ToString("0.##", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, ink3);
            text.SetFontFeatures(Typography.Tabular);
            context.DrawText(text, new Point(X(value) - text.Width * align, bar.Bottom + LabelGap));
        }
    }

    // A 2 px line across the bar with a 1 px Pane casing on each side, reaching past the bar by the overhang.
    private static void Mark(DrawingContext context, double x, double overhang, IBrush brush, IBrush casing, Rect bar)
    {
        context.DrawRectangle(casing, null, new Rect(x - 2, bar.Top - overhang, 4, bar.Height + 2 * overhang));
        context.DrawRectangle(brush, null, new Rect(x - 1, bar.Top - overhang, 2, bar.Height + 2 * overhang));
    }

    // The ends, every step from the start, and zero: the start left-aligned, the end right-aligned, the rest centred.
    private IEnumerable<(double Value, double Align)> Labels()
    {
        if (Maximum <= Minimum)
        {
            yield break;
        }

        yield return (Minimum, 0);
        var zeroLabelled = ZeroAt is not double zero || zero <= Minimum || zero >= Maximum;
        if (Step > 0)
        {
            for (var tick = Minimum + Step; tick < Maximum - Step / 2; tick += Step)
            {
                zeroLabelled |= ZeroAt is double at && Math.Abs(tick - at) < 1e-9;
                yield return (tick, 0.5);
            }
        }

        if (!zeroLabelled && ZeroAt is double mid)
        {
            yield return (mid, 0.5);
        }

        yield return (Maximum, 1);
    }
}
