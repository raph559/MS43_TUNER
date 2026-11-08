using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// Knock retard per cylinder (performance spec 9.2): six Track bars numbered 1 to 6 in Data 12 Ink3, filled Now while
/// live and Ink2 in a stored result. A cylinder at or past <see cref="WarnFrom"/> also gets the warning icon above its bar
/// in Caution, the colour of the run's knock flag (minor review, finding 34), and a line under the bars names it, never
/// colour alone. A cylinder without a value draws no fill and reads "n/a". With <see cref="ShowsAverage"/> (the Live tab,
/// owner 2026-10-28) a seventh bar, set apart, shows the average of the cylinders that have a value and reads "Avg".
/// </summary>
public sealed class CylinderBars : Control
{
    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty = AvaloniaProperty.Register<CylinderBars, IReadOnlyList<double?>?>(nameof(Values));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<CylinderBars, double>(nameof(Maximum), 4);
    public static readonly StyledProperty<double> WarnFromProperty = AvaloniaProperty.Register<CylinderBars, double>(nameof(WarnFrom), 1.5);
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<CylinderBars, bool>(nameof(IsLive));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<CylinderBars, int>(nameof(Version));
    public static readonly StyledProperty<bool> ShowsAverageProperty = AvaloniaProperty.Register<CylinderBars, bool>(nameof(ShowsAverage));
    public static readonly StyledProperty<double> BarHeightProperty = AvaloniaProperty.Register<CylinderBars, double>(nameof(BarHeight), 40);

    private const int Cylinders = 6;
    private const double Gap = 4;
    private const double AverageGap = 16;

    // A 16 px icon line over the bars, then a 4 px gap and a 16 px label line under them.
    private const double IconLine = 18;
    private const double LabelLine = 20;

    static CylinderBars()
    {
        AffectsRender<CylinderBars>(ValuesProperty, MaximumProperty, WarnFromProperty, IsLiveProperty, VersionProperty, ShowsAverageProperty);
        AffectsMeasure<CylinderBars>(BarHeightProperty);
    }

    public CylinderBars()
    {
        AutomationProperties.SetName(this, "Knock retard per cylinder");
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Degrees of retard for cylinders 1 to 6, positive; null where the run has no value.</summary>
    public IReadOnlyList<double?>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>The retard that fills a bar.</summary>
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>The retard from which a cylinder gets the warning icon.</summary>
    public double WarnFrom { get => GetValue(WarnFromProperty); set => SetValue(WarnFromProperty, value); }

    /// <summary>Samples are arriving: the fills are Now; otherwise Ink2, since orange means now (system 1.4).</summary>
    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    /// <summary>Raised when <see cref="Values"/> changed in place, so the bars draw again.</summary>
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>The tracks' height: 40 in Performance, shorter in Live's one-line strip.</summary>
    public double BarHeight { get => GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }

    /// <summary>A seventh bar after the six: their average.</summary>
    public bool ShowsAverage { get => GetValue(ShowsAverageProperty); set => SetValue(ShowsAverageProperty, value); }

    /// <summary>The average retard of the cylinders that have a value; null when none has.</summary>
    public double? Average
    {
        get
        {
            var known = Enumerable.Range(0, Cylinders).Select(Value).OfType<double>().ToArray();
            return known.Length > 0 ? known.Average() : null;
        }
    }

    private int Slots => ShowsAverage ? Cylinders + 1 : Cylinders;

    /// <summary>The labels under the bars as drawn: "1" to "6" and "Avg", or "n/a" for a bar without a value.</summary>
    public IReadOnlyList<string> Labels =>
        Enumerable.Range(0, Slots).Select(i => Value(i) is null ? "n/a" : i == Cylinders ? "Avg" : (i + 1).ToString(CultureInfo.InvariantCulture)).ToList();

    /// <summary>Where the track of cylinder <paramref name="index"/> + 1 (or, at 6, the average) is drawn.</summary>
    public Rect BarRect(int index)
    {
        var apart = ShowsAverage ? AverageGap - Gap : 0;
        var width = Math.Max(0, (Bounds.Width - Gap * (Slots - 1) - apart) / Slots);
        return new Rect(index * (width + Gap) + (index == Cylinders ? apart : 0), IconLine, width, Math.Max(0, Bounds.Height - IconLine - LabelLine));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, IconLine + BarHeight + LabelLine);

    public override void Render(DrawingContext context)
    {
        var track = ThemeBrushes.Get(this, "TrackBrush");
        var fill = ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink2Brush");
        var warn = ThemeBrushes.Get(this, "CautionBrush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var data = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var ui = new Typeface(ThemeBrushes.Font(this, "UiFont"));
        var icon = this.TryFindResource("Icon.Warn", out var found) ? found as Geometry : null;
        var labels = Labels;

        for (var i = 0; i < Slots; i++)
        {
            var bar = BarRect(i);
            var rounded = new RoundedRect(bar, 3);
            context.DrawRectangle(track, null, rounded);
            if (Value(i) is double value)
            {
                var height = Maximum > 0 ? Math.Clamp(value / Maximum, 0, 1) * bar.Height : 0;
                if (height > 0)
                {
                    using (context.PushClip(rounded))
                    {
                        context.DrawRectangle(fill, null, new Rect(bar.Left, bar.Bottom - height, bar.Width, height));
                    }
                }

                if (value >= WarnFrom && icon is not null)
                {
                    using (context.PushTransform(Matrix.CreateTranslation(bar.Center.X - 8, 0)))
                    {
                        StrokeIcon.Draw(context, icon, null, warn, new Size(16, 16));
                    }
                }
            }

            // Numbers in the data font; a missing value's "n/a" and the word "Avg" in UI text, as everywhere else (system 3.3).
            var missing = Value(i) is null || i == Cylinders;
            var text = new FormattedText(labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, missing ? ui : data, 12, ink3);
            text.SetFontFeatures(Typography.Tabular);
            context.DrawText(text, new Point(bar.Center.X - text.Width / 2, bar.Bottom + 4));
        }
    }

    private double? Value(int index) => index == Cylinders ? Average
        : Values is { } values && index < values.Count && values[index] is double value && double.IsFinite(value) ? value : null;
}
