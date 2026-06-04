using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A dial of the Dials band: a 270° Track arc with a Now arc up to the value and a Now
/// tip across the track (Ink3 once the link is not live), Ink3 major ticks and Edge minor ticks, Data 12 Ink3 labels, the
/// rev-limit band as an Ink2 arc outside the track with its value, a cased zero mark on a scale that crosses zero, and the
/// reading inside. No face fill, no needle. The geometry follows the mockup's 220 px (large) and 184 px (small) dials and
/// scales with the control; strokes keep their width and labels stay 12 px.
/// </summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<string> UnitProperty = AvaloniaProperty.Register<Gauge, string>(nameof(Unit), string.Empty);
    public static readonly StyledProperty<string?> ScaleNoteProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(ScaleNote));
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> MajorStepProperty = AvaloniaProperty.Register<Gauge, double>(nameof(MajorStep), 10);
    public static readonly StyledProperty<double> MinorStepProperty = AvaloniaProperty.Register<Gauge, double>(nameof(MinorStep), 2);
    public static readonly StyledProperty<double> LabelDivisorProperty = AvaloniaProperty.Register<Gauge, double>(nameof(LabelDivisor), 1);
    public static readonly StyledProperty<int> DecimalsProperty = AvaloniaProperty.Register<Gauge, int>(nameof(Decimals));
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<Gauge, double?>(nameof(Value));
    public static readonly StyledProperty<double?> RedFromProperty = AvaloniaProperty.Register<Gauge, double?>(nameof(RedFrom));
    public static readonly StyledProperty<double?> ZeroAtProperty = AvaloniaProperty.Register<Gauge, double?>(nameof(ZeroAt));
    public static readonly StyledProperty<bool> IsLargeProperty = AvaloniaProperty.Register<Gauge, bool>(nameof(IsLarge));
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<Gauge, bool>(nameof(IsLive));

    private const double Start = 135;
    private const double Sweep = 270;

    static Gauge()
    {
        AffectsRender<Gauge>(UnitProperty, ScaleNoteProperty, MinimumProperty, MaximumProperty, MajorStepProperty, MinorStepProperty,
            LabelDivisorProperty, DecimalsProperty, ValueProperty, RedFromProperty, ZeroAtProperty, IsLargeProperty, IsLiveProperty);
    }

    public Gauge()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The reading's unit, under the numerals ("rpm").</summary>
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    /// <summary>"× 1000 rpm" under the top label of the large dial; hidden on a compact dial.</summary>
    public string? ScaleNote { get => GetValue(ScaleNoteProperty); set => SetValue(ScaleNoteProperty, value); }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double MajorStep { get => GetValue(MajorStepProperty); set => SetValue(MajorStepProperty, value); }

    public double MinorStep { get => GetValue(MinorStepProperty); set => SetValue(MinorStepProperty, value); }

    public double LabelDivisor { get => GetValue(LabelDivisorProperty); set => SetValue(LabelDivisorProperty, value); }

    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Start of the rev-limit band, or null for none.</summary>
    public double? RedFrom { get => GetValue(RedFromProperty); set => SetValue(RedFromProperty, value); }

    public double? ZeroAt { get => GetValue(ZeroAtProperty); set => SetValue(ZeroAtProperty, value); }

    /// <summary>The rpm dial: 220 px geometry and a Data 56 reading; otherwise 184 px and Data 40.</summary>
    public bool IsLarge { get => GetValue(IsLargeProperty); set => SetValue(IsLargeProperty, value); }

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var box = IsLarge ? 220 : 184;
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? box : availableSize.Width, double.IsInfinity(availableSize.Height) ? box : availableSize.Height);
        return new Size(side, side);
    }

    private double Angle(double value) => Start + Sweep * (Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0);

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0 || Maximum <= Minimum)
        {
            return;
        }

        // The mockup's numbers, in a box of 220 (large) or 184 (small), scaled to the control.
        var box = IsLarge ? 220.0 : 184.0;
        var compact = side < box - 20;
        var s = side / box;
        var track = IsLarge ? 82 : 68;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Point Polar(double degrees, double radius) =>
            new(centre.X + radius * s * Math.Cos(degrees * Math.PI / 180), centre.Y + radius * s * Math.Sin(degrees * Math.PI / 180));

        var ink1 = ThemeBrushes.Get(this, "Ink1Brush");
        var ink2 = ThemeBrushes.Get(this, "Ink2Brush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var pane = ThemeBrushes.Get(this, "PaneBrush");
        var data = ThemeBrushes.Font(this, "DataFont");
        var ui = ThemeBrushes.Font(this, "UiFont");
        var stroke = compact ? 5 : 6;

        context.DrawGeometry(null, new Pen(ThemeBrushes.Get(this, "TrackBrush"), stroke), Arc(Polar, Start, Start + Sweep, track));

        // Ticks: majors 7 px and minors 4 px in from the track's outer radius, labels outside the track.
        var majors = Majors().ToList();
        var outer = track + (IsLarge ? -5 : -5);
        var majorPen = new Pen(ink3, 1.5);
        var minorPen = new Pen(ThemeBrushes.Get(this, "EdgeBrush"), 1);
        foreach (var major in majors)
        {
            context.DrawLine(majorPen, Polar(Angle(major), outer), Polar(Angle(major), outer - 7));
        }

        if (MinorStep > 0)
        {
            for (var minor = Minimum + MinorStep; minor < Maximum - MinorStep / 2; minor += MinorStep)
            {
                if (!majors.Any(major => Math.Abs(major - minor) < 1e-9) && (ZeroAt is not double zero || Math.Abs(zero - minor) > 1e-9))
                {
                    context.DrawLine(minorPen, Polar(Angle(minor), outer), Polar(Angle(minor), outer - 4));
                }
            }
        }

        var labelRadius = IsLarge ? 103 : 84;
        foreach (var label in majors.Concat(ZeroAt is double at && at > Minimum && at < Maximum ? new[] { at } : Array.Empty<double>()))
        {
            Centred(context, (label / LabelDivisor).ToString("0.##", CultureInfo.InvariantCulture), data, 12, ink3, Polar(Angle(label), labelRadius), FontWeight.Normal);
        }

        if (IsLarge && !compact && !string.IsNullOrEmpty(ScaleNote))
        {
            Mixed(context, ScaleNote, data, ui, 11, ink3, new Point(centre.X, centre.Y + (56 - 110) * s));
        }

        if (RedFrom is double red && red < Maximum)
        {
            // The rev-limit band outside the track, and its value where it starts.
            var from = Angle(Math.Max(red, Minimum));
            context.DrawGeometry(null, new Pen(ink2, 3), Arc(Polar, from, Start + Sweep, IsLarge ? 90 : 76));
            var limit = new FormattedText(red.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(data, FontStyle.Normal, FontWeight.SemiBold), 12, ink2);
            limit.SetFontFeatures(Typography.Tabular);
            var at = Polar(from, IsLarge ? 95 : 81);
            context.DrawText(limit, new Point(at.X, at.Y - limit.Height / 2));
        }

        var hasValue = Value is double v && double.IsFinite(v);
        if (hasValue)
        {
            var value = Value!.Value;
            var now = ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink3Brush");
            var origin = ZeroAt is double zero ? Angle(zero) : Start;
            var end = Angle(value);
            if (Math.Abs(end - origin) > 0.01)
            {
                context.DrawGeometry(null, new Pen(now, stroke), Arc(Polar, Math.Min(origin, end), Math.Max(origin, end), track));
            }

            context.DrawLine(new Pen(now, 3), Polar(end, track - 8), Polar(end, track + 3));
        }

        if (ZeroAt is double zeroAt && zeroAt > Minimum && zeroAt < Maximum)
        {
            // The zero mark crosses the track with a Pane casing, so it holds 3:1 on the Now arc.
            var angle = Angle(zeroAt);
            context.DrawLine(new Pen(pane, 4), Polar(angle, track - 12), Polar(angle, track + 6));
            context.DrawLine(new Pen(ink2, 2), Polar(angle, track - 12), Polar(angle, track + 6));
        }

        // The reading: numerals centred 9 px under the centre (8 when compact), the unit under them.
        var size = IsLarge ? (compact ? 40 : 56) : (compact ? 24 : 40);
        var unitSize = compact ? 12 : 13;
        var reading = hasValue
            ? new FormattedText(NumberFormat.Format(Value, Decimals), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(data), size, IsLive ? ink1 : ink3)
            : new FormattedText("n/a", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(ThemeBrushes.Font(this, "UiDisplayFont")), compact ? 16 : 20, ink3);
        reading.SetFontFeatures(Typography.Tabular);
        var unit = hasValue && Unit.Length > 0
            ? new FormattedText(Unit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(ui), unitSize, ink3)
            : null;
        var block = reading.Height + (unit?.Height ?? 0);
        var top = centre.Y + (compact ? 8 : 9) - block / 2;
        context.DrawText(reading, new Point(centre.X - reading.Width / 2, top));
        if (unit is not null)
        {
            context.DrawText(unit, new Point(centre.X - unit.Width / 2, top + reading.Height));
        }
    }

    // The labelled ticks: every major step from the start, and the end.
    private IEnumerable<double> Majors()
    {
        if (MajorStep > 0)
        {
            for (var tick = Minimum; tick < Maximum - MajorStep / 2; tick += MajorStep)
            {
                yield return tick;
            }
        }

        yield return Maximum;
    }

    private static Geometry Arc(Func<double, double, Point> polar, double from, double to, double radius)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var start = polar(from, radius);
            var end = polar(to, radius);
            var size = polar(0, radius).X - polar(180, radius).X;
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(size / 2, size / 2), 0, to - from > 180, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        return geometry;
    }

    // A UI line with its numbers in Bahnschrift, centred on a point and set on one baseline: "× 1000 rpm".
    private static void Mixed(DrawingContext context, string text, FontFamily data, FontFamily ui, double size, IBrush brush, Point centre)
    {
        var runs = System.Text.RegularExpressions.Regex.Split(text, @"(\d+)").Where(part => part.Length > 0).Select(part =>
        {
            var digits = char.IsDigit(part[0]);
            var run = new FormattedText(part, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(digits ? data : ui), size, brush);
            if (digits)
            {
                run.SetFontFeatures(Typography.Tabular);
            }

            return run;
        }).ToList();
        if (runs.Count == 0)
        {
            return;
        }

        var baseline = runs.Max(run => run.Baseline);
        var top = centre.Y - runs.Max(run => run.Height) / 2;
        var x = centre.X - runs.Sum(run => run.WidthIncludingTrailingWhitespace) / 2;
        foreach (var run in runs)
        {
            context.DrawText(run, new Point(x, top + baseline - run.Baseline));
            x += run.WidthIncludingTrailingWhitespace;
        }
    }

    private static void Centred(DrawingContext context, string text, FontFamily family, double size, IBrush brush, Point centre, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, brush);
        formatted.SetFontFeatures(Typography.Tabular);
        context.DrawText(formatted, new Point(centre.X - formatted.Width / 2, centre.Y - formatted.Height / 2));
    }
}
