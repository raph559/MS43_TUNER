using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The value slider: a 4 px track with square ends across the control, Ink2 from the span's start to the
/// value and Edge beyond it; each XDF end on the span a short Ink2 tick with a 1 px Pane casing; a 20 px Ink1 thumb with a
/// 2 px Pane ring, which is also the gap of the focus ring; the span's ends labelled under the track. The thumb overhangs
/// the ends by its radius, as in the mockup, so the track reaches the − and + buttons' gap. It writes nothing itself: it
/// asks for steps, jumps and drags, and the value editor writes them through the undo history.
/// </summary>
public sealed class ValueSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Value));
    public static readonly StyledProperty<double?> PendingProperty = AvaloniaProperty.Register<ValueSlider, double?>(nameof(Pending));
    public static readonly StyledProperty<double?> TickLowProperty = AvaloniaProperty.Register<ValueSlider, double?>(nameof(TickLow));
    public static readonly StyledProperty<double?> TickHighProperty = AvaloniaProperty.Register<ValueSlider, double?>(nameof(TickHigh));
    public static readonly StyledProperty<string?> MinLabelProperty = AvaloniaProperty.Register<ValueSlider, string?>(nameof(MinLabel));
    public static readonly StyledProperty<string?> MaxLabelProperty = AvaloniaProperty.Register<ValueSlider, string?>(nameof(MaxLabel));

    // The 32 px band every control has, the track on its centre line, then the end labels 2 px under it.
    private const double Band = 32;
    private const double TrackY = 16;
    private const double ThumbRadius = 10;
    private const double LabelTop = Band + 2;
    private const double LabelHeight = 16;

    // The thumb's ring is part of what can be taken; the value moves only once the pointer has moved this far.
    private const double ThumbHit = 12;
    private const double DragThreshold = 3;
    private bool _held;
    private bool _dragging;
    private double _grab;
    private double _pressedX;

    static ValueSlider()
    {
        AffectsRender<ValueSlider>(MinimumProperty, MaximumProperty, ValueProperty, PendingProperty, TickLowProperty, TickHighProperty,
            MinLabelProperty, MaxLabelProperty);
        FocusableProperty.OverrideDefaultValue<ValueSlider>(true);
    }

    public ValueSlider()
    {
        // The ring is drawn round the thumb, not round the whole control.
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public event EventHandler<int>? StepRequested;

    public event EventHandler<bool>? JumpRequested;

    public event EventHandler<double>? DragUpdated;

    public event EventHandler? DragCommitted;

    public event EventHandler? DragCancelled;

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>The value under the thumb during a drag; the thumb sits there until the drag ends.</summary>
    public double? Pending { get => GetValue(PendingProperty); set => SetValue(PendingProperty, value); }

    public double? TickLow { get => GetValue(TickLowProperty); set => SetValue(TickLowProperty, value); }

    public double? TickHigh { get => GetValue(TickHighProperty); set => SetValue(TickHighProperty, value); }

    public string? MinLabel { get => GetValue(MinLabelProperty); set => SetValue(MinLabelProperty, value); }

    public string? MaxLabel { get => GetValue(MaxLabelProperty); set => SetValue(MaxLabelProperty, value); }

    /// <summary>The thumb's centre in the control's coordinates.</summary>
    internal Point ThumbCentre => new(X(Pending ?? Value), TrackY);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width, LabelTop + LabelHeight);

    private double X(double value) =>
        Maximum > Minimum && double.IsFinite(value) ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) * Bounds.Width : 0;

    private double ValueAt(double x) =>
        Minimum + (Bounds.Width > 0 ? Math.Clamp(x / Bounds.Width, 0, 1) : 0) * (Maximum - Minimum);

    // An end label, "8160 rpm": the number in Bahnschrift, the unit in Segoe UI, as every number in a UI line.
    private static void Label(DrawingContext context, string? text, Typeface data, Typeface ui, IBrush brush, double x, bool alignRight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var split = text.IndexOf(' ', StringComparison.Ordinal);
        var number = new FormattedText(split < 0 ? text : text[..split], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, data, 12, brush);
        number.SetFontFeatures(Typography.Tabular);
        var units = split < 0 ? null : new FormattedText(text[split..], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ui, 12, brush);
        var width = number.WidthIncludingTrailingWhitespace + (units?.WidthIncludingTrailingWhitespace ?? 0);
        var left = alignRight ? x - width : x;

        // The two fonts have different ascents, so each is placed by its baseline, not by its top.
        var baseline = Math.Max(number.Baseline, units?.Baseline ?? 0);
        context.DrawText(number, new Point(left, LabelTop + baseline - number.Baseline));
        if (units is not null)
        {
            context.DrawText(units, new Point(left + number.WidthIncludingTrailingWhitespace, LabelTop + baseline - units.Baseline));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty)
        {
            InvalidateVisual();
        }
    }

    // The ring shows and hides with the focus, which changes no property the control renders from.
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_dragging)
        {
            _dragging = false;
            DragCancelled?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }
}
