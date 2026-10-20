using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Shell;

/// <summary>
/// The app's mark (system 5.1): a 20 px dial, a 270 degree arc open at the bottom and a needle, drawn in the icon style
/// (1.5 px, flat caps). At rest the arc is Ink3 and the needle Ink2 at 0 rpm; while logging both are Now and the needle
/// follows engine speed, redrawn with each reading and never tweened.
/// </summary>
public sealed class AppMark : Control
{
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<AppMark, bool>(nameof(IsLive));

    /// <summary>Needle position, 0 (rest) to 1 (8000 rpm).</summary>
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<AppMark, double>(nameof(Level));

    private const double ArcRadius = 8.25;
    private const double NeedleRadius = 5.5;
    private static readonly Point Centre = new(10, 11);

    static AppMark()
    {
        AffectsRender<AppMark>(IsLiveProperty, LevelProperty);
    }

    public AppMark()
    {
        Width = 20;
        Height = 20;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public bool IsLive
    {
        get => GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var arcPen = new Pen(ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink3Brush"), 1.5, lineCap: PenLineCap.Flat);
        var needlePen = new Pen(ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink2Brush"), 1.5, lineCap: PenLineCap.Flat);

        // 0 rpm sits at 135 degrees (down and left), 8000 rpm at 45 degrees, clockwise through the top.
        var arc = new StreamGeometry();
        using (var draw = arc.Open())
        {
            draw.BeginFigure(At(ArcRadius, 135), false);
            draw.ArcTo(At(ArcRadius, 45), new Size(ArcRadius, ArcRadius), 0, true, SweepDirection.Clockwise);
            draw.EndFigure(false);
        }

        context.DrawGeometry(null, arcPen, arc);
        context.DrawLine(needlePen, Centre, At(NeedleRadius, 135 + 270 * Math.Clamp(IsLive ? Level : 0, 0, 1)));
    }

    private static Point At(double radius, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        return new Point(Centre.X + radius * Math.Cos(angle), Centre.Y + radius * Math.Sin(angle));
    }
}
