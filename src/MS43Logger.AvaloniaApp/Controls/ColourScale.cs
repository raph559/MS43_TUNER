using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The colour scale (system 2.1, 2.5): an 8 px bar of the map ramp, from MapRamp so the stops live in one place, or of
/// the A - B pair (DivBelow, Pane, DivAbove), with its low, middle and high values in Data 12 under it.
/// </summary>
public sealed class ColourScale : Control
{
    public static readonly StyledProperty<bool> DivergingProperty = AvaloniaProperty.Register<ColourScale, bool>(nameof(Diverging));
    public static readonly StyledProperty<string?> MinLabelProperty = AvaloniaProperty.Register<ColourScale, string?>(nameof(MinLabel));
    public static readonly StyledProperty<string?> MidLabelProperty = AvaloniaProperty.Register<ColourScale, string?>(nameof(MidLabel));
    public static readonly StyledProperty<string?> MaxLabelProperty = AvaloniaProperty.Register<ColourScale, string?>(nameof(MaxLabel));

    private const double BarHeight = 8;
    private const double LabelGap = 4;
    private const double LabelHeight = 16;

    static ColourScale()
    {
        AffectsRender<ColourScale>(DivergingProperty, MinLabelProperty, MidLabelProperty, MaxLabelProperty);
    }

    public ColourScale()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public bool Diverging { get => GetValue(DivergingProperty); set => SetValue(DivergingProperty, value); }

    public string? MinLabel { get => GetValue(MinLabelProperty); set => SetValue(MinLabelProperty, value); }

    public string? MidLabel { get => GetValue(MidLabelProperty); set => SetValue(MidLabelProperty, value); }

    public string? MaxLabel { get => GetValue(MaxLabelProperty); set => SetValue(MaxLabelProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, BarHeight + LabelGap + LabelHeight);

    public override void Render(DrawingContext context)
    {
        var bar = new Rect(0, 0, Bounds.Width, BarHeight);
        IBrush brush = Diverging
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(ThemeBrushes.Color(this, "DivBelowBrush"), 0),
                    new GradientStop(ThemeBrushes.Color(this, "PaneBrush"), 0.5),
                    new GradientStop(ThemeBrushes.Color(this, "DivAboveBrush"), 1),
                },
            }
            : MapRamp.Gradient(ThemeBrushes.IsNight(this));
        context.DrawRectangle(brush, null, new RoundedRect(bar, BarHeight / 2));

        var face = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var ink = ThemeBrushes.Get(this, "Ink2Brush");
        Label(context, MinLabel, face, ink, 0);
        Label(context, MidLabel, face, ink, 0.5);
        Label(context, MaxLabel, face, ink, 1);
    }

    private void Label(DrawingContext context, string? text, Typeface face, IBrush ink, double align)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, ink);
        formatted.SetFontFeatures(Typography.Tabular);
        context.DrawText(formatted, new Point((Bounds.Width - formatted.Width) * align, BarHeight + LabelGap));
    }
}
