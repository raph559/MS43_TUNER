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
