using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A channel's trend in the Live list: the samples of the trend window placed by time, an Ink2 1.25 px line
/// and an Ink1 dot at the newest sample, both Ink3 once the values are frozen. Never orange: a row is data, not now. A
/// flat channel stays flat because the vertical span is never less than its unit's minimum (5 °C, 1 V, 2 %, 100 rpm,
/// 10 mg/stk, 2 kPa).
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<SampleHistory?> HistoryProperty = AvaloniaProperty.Register<Sparkline, SampleHistory?>(nameof(History));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<Sparkline, int>(nameof(Version));
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<Sparkline, bool>(nameof(IsLive));
    public static readonly StyledProperty<string?> UnitsProperty = AvaloniaProperty.Register<Sparkline, string?>(nameof(Units));
    public static readonly StyledProperty<bool> IsStaleProperty = AvaloniaProperty.Register<Sparkline, bool>(nameof(IsStale));

    static Sparkline()
    {
        AffectsRender<Sparkline>(HistoryProperty, VersionProperty, UnitsProperty, IsStaleProperty);
    }

    public Sparkline()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public SampleHistory? History { get => GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }

    /// <summary>Bind to the history's version so the line redraws on every sample.</summary>
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    /// <summary>The channel's unit, which sets the smallest vertical span.</summary>
    public string? Units { get => GetValue(UnitsProperty); set => SetValue(UnitsProperty, value); }

    public bool IsStale { get => GetValue(IsStaleProperty); set => SetValue(IsStaleProperty, value); }

    /// <summary>The smallest span the line spreads over, by unit, so sensor noise does not fill the row.</summary>
    public static double MinimumSpan(string? units) => units?.Trim() switch
    {
        "°C" => 5,
        "V" => 1,
        "%" => 2,
        "rpm" => 100,
        "mg/stk" => 10,
        "kPa" => 2,
        _ => 0,
    };

    public override void Render(DrawingContext context)
    {
        var values = History?.Values.ToArray();
        if (values is null || values.Length < 2 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double lo = values.Min(), hi = values.Max();
        if (hi - lo < 1e-9)
        {
            lo -= 1;
            hi += 1;
        }

        var w = Bounds.Width;
        var h = Bounds.Height;
        Point At(int i) => new(i * w / (SampleHistory.Capacity - 1), h - 3 - (values[i] - lo) / (hi - lo) * (h - 6));

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            g.BeginFigure(At(0), false);
            a.BeginFigure(new Point(0, h), true);
            a.LineTo(At(0));
            for (var i = 1; i < values.Length; i++)
            {
                g.LineTo(At(i));
                a.LineTo(At(i));
            }

            a.LineTo(new Point(At(values.Length - 1).X, h));
            g.EndFigure(false);
            a.EndFigure(true);
        }

        var ink = ThemeBrushes.Color(this, "Ink2Color");
        context.DrawGeometry(new SolidColorBrush(ink, 0.12), null, area);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(ink), 1.4), line);
        var end = At(values.Length - 1);
        context.DrawEllipse(IsLive ? ThemeBrushes.Get(this, "NeedleBrush") : new SolidColorBrush(ink), null, end, 2.6, 2.6);
    }
}
