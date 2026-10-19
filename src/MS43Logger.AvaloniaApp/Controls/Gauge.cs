using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>A cluster dial in the style of the E46 instruments: 270° scale, white ticks, red zone, orange needle.</summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<Gauge, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> UnitProperty = AvaloniaProperty.Register<Gauge, string>(nameof(Unit), string.Empty);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> MajorStepProperty = AvaloniaProperty.Register<Gauge, double>(nameof(MajorStep), 10);
    public static readonly StyledProperty<double> MinorStepProperty = AvaloniaProperty.Register<Gauge, double>(nameof(MinorStep), 2);
    public static readonly StyledProperty<double> LabelDivisorProperty = AvaloniaProperty.Register<Gauge, double>(nameof(LabelDivisor), 1);
    public static readonly StyledProperty<int> DecimalsProperty = AvaloniaProperty.Register<Gauge, int>(nameof(Decimals));
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<Gauge, double?>(nameof(Value));
    public static readonly StyledProperty<double?> RedFromProperty = AvaloniaProperty.Register<Gauge, double?>(nameof(RedFrom));
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<Gauge, bool>(nameof(IsLive));

    static Gauge()
    {
        AffectsRender<Gauge>(TitleProperty, UnitProperty, MinimumProperty, MaximumProperty, MajorStepProperty, MinorStepProperty,
            LabelDivisorProperty, DecimalsProperty, ValueProperty, RedFromProperty, IsLiveProperty);
    }

    public Gauge()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double MajorStep { get => GetValue(MajorStepProperty); set => SetValue(MajorStepProperty, value); }

    public double MinorStep { get => GetValue(MinorStepProperty); set => SetValue(MinorStepProperty, value); }

    public double LabelDivisor { get => GetValue(LabelDivisorProperty); set => SetValue(LabelDivisorProperty, value); }

    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double? RedFrom { get => GetValue(RedFromProperty); set => SetValue(RedFromProperty, value); }

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0 || Maximum <= Minimum)
        {
            return;
        }

        // Drawn in a 320-unit square, like the mockup's SVG, then scaled.
        var s = side / 320;
        var origin = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        Point P(double x, double y) => new(origin.X + x * s, origin.Y + y * s);
        Point Polar(double degrees, double radius) => P(160 + radius * Math.Cos(degrees * Math.PI / 180), 160 + radius * Math.Sin(degrees * Math.PI / 180));
        double A(double value) => GaugeMath.Angle(Minimum, Maximum, value);

        var display = ThemeBrushes.Font(this, "DisplayFont");
        var ui = ThemeBrushes.Font(this, "UiFont");
        var ink = ThemeBrushes.Get(this, "InkBrush");
        var ink2 = ThemeBrushes.Get(this, "Ink2Brush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var needle = ThemeBrushes.Get(this, "NeedleBrush");

        context.DrawEllipse(ThemeBrushes.Get(this, "FaceBrush"), new Pen(ThemeBrushes.Get(this, "Bezel2Brush"), 1.5 * s), P(160, 160), 146 * s, 146 * s);

        if (RedFrom is double red && red < Maximum)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                var from = A(Math.Max(red, Minimum));
                var to = A(Maximum);
                g.BeginFigure(Polar(from, 129), false);
                g.ArcTo(Polar(to, 129), new Size(129 * s, 129 * s), 0, to - from > 180, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(ThemeBrushes.Get(this, "RedZoneBrush"), 7 * s), arc);
        }

        var majorPen = new Pen(ThemeBrushes.Get(this, "TickBrush"), 2.4 * s);
        var minorPen = new Pen(ink3, 1.2 * s);
        var steps = (int)Math.Round((Maximum - Minimum) / MinorStep);
        for (var i = 0; i <= steps; i++)
        {
            var value = Minimum + i * MinorStep;
            var ratio = (value - Minimum) / MajorStep;
            var major = Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
            context.DrawLine(major ? majorPen : minorPen, Polar(A(value), major ? 120 : 126), Polar(A(value), 134));
            if (major)
            {
                var label = (value / LabelDivisor).ToString("0.##", CultureInfo.InvariantCulture).Replace('-', '−');
                DrawCentred(context, label, display, 15 * s, ink2, Polar(A(value), 104));
            }
        }

        DrawCentred(context, Title, ui, 13 * s, ink2, P(160, 104), FontWeight.SemiBold);
        var read = Value is double v ? Format(v) : "–";
        DrawCentred(context, read, display, 40 * s, IsLive && Value is not null ? needle : ink, P(160, 216));
        DrawCentred(context, Unit, ui, 12.5 * s, ink3, P(160, 246));
        if (RedFrom is double limit)
        {
            DrawCentred(context, "limiter " + Format(limit) + " rpm", ui, 11 * s, ink3, P(160, 294));
        }

        var tip = Polar(A(Value ?? Minimum), 116);
        var glow = ThemeBrushes.Color(this, "NeedleGlowColor");
        context.DrawLine(new Pen(new SolidColorBrush(glow), 8 * s, lineCap: PenLineCap.Round), P(160, 160), tip);
        context.DrawLine(new Pen(needle, 3.2 * s, lineCap: PenLineCap.Round), P(160, 160), tip);
        context.DrawEllipse(ThemeBrushes.Get(this, "RaisedBrush"), new Pen(ThemeBrushes.Get(this, "Bezel2Brush"), 1 * s), P(160, 160), 9 * s, 9 * s);
    }

    private string Format(double value) =>
        Decimals == 0
            ? Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ')
            : value.ToString("F" + Decimals, CultureInfo.InvariantCulture).Replace('-', '−');

    private static void DrawCentred(DrawingContext context, string text, FontFamily family, double size, IBrush brush, Point centre, FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(text) || size <= 0)
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, brush);
        context.DrawText(formatted, new Point(centre.X - formatted.Width / 2, centre.Y - formatted.Height / 2));
    }
}
