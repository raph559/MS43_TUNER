using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Shell;

/// <summary>The app logo: a small dial whose ring lights up and whose needle follows engine speed while logging.</summary>
public sealed class AppMark : Control
{
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<AppMark, bool>(nameof(IsLive));

    /// <summary>Needle position, 0 (rest) to 1 (8 000 rpm).</summary>
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<AppMark, double>(nameof(Level));

    static AppMark()
    {
        AffectsRender<AppMark>(IsLiveProperty, LevelProperty);
    }

    public AppMark()
    {
        Width = 26;
        Height = 26;
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
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var colour = Brush(IsLive ? "NeedleBrush" : "Ink3Brush");
        var hand = Brush(IsLive ? "NeedleBrush" : "Ink2Brush");
        context.DrawEllipse(null, new Pen(colour, 2), centre, size / 2 - 2, size / 2 - 2);

        var angle = (-225 + 270 * Math.Clamp(IsLive ? Level : 0, 0, 1)) * Math.PI / 180;
        var tip = new Point(centre.X + (size / 2 - 6) * Math.Cos(angle), centre.Y + (size / 2 - 6) * Math.Sin(angle));
        context.DrawLine(new Pen(hand, 2, lineCap: PenLineCap.Round), centre, tip);
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
