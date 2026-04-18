using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>A 13 px symbol for an item's kind: four squares for a map, a line for a curve or axis, a dot for a value, a switch for a switch or flag.</summary>
public sealed class ItemGlyph : Control
{
    public static readonly StyledProperty<ItemKind> KindProperty = AvaloniaProperty.Register<ItemGlyph, ItemKind>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<ItemGlyph, IBrush?>(nameof(Foreground));

    static ItemGlyph()
    {
        AffectsRender<ItemGlyph>(KindProperty, ForegroundProperty);
    }

    public ItemGlyph()
    {
        Width = 13;
        Height = 13;
    }

    public ItemKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? ThemeBrushes.Get(this, "Ink3Brush");
        var pen = new Pen(brush, 1.4);
        switch (Kind)
        {
            case ItemKind.Map:
                context.DrawRectangle(brush, null, new Rect(1, 1, 5, 5), 1, 1);
                context.DrawRectangle(brush, null, new Rect(7, 7, 5, 5), 1, 1);
                context.DrawRectangle(new SolidColorBrush(Colors.Gray, 0.0), pen, new Rect(7.5, 1.5, 4, 4), 1, 1);
                context.DrawRectangle(new SolidColorBrush(Colors.Gray, 0.0), pen, new Rect(1.5, 7.5, 4, 4), 1, 1);
                break;
            case ItemKind.Curve:
            case ItemKind.Axis:
                var curve = new StreamGeometry();
                using (var g = curve.Open())
                {
                    g.BeginFigure(new Point(1, 11), false);
                    if (Kind == ItemKind.Axis)
                    {
                        g.LineTo(new Point(12, 2));
                    }
                    else
                    {
                        g.CubicBezierTo(new Point(4, 10), new Point(5, 3), new Point(8, 3));
                        g.CubicBezierTo(new Point(10, 3), new Point(11, 6), new Point(12, 6));
                    }

                    g.EndFigure(false);
                }

                context.DrawGeometry(null, pen, curve);
                break;
            case ItemKind.Switch:
            case ItemKind.Flag:
                context.DrawRectangle(null, pen, new Rect(1, 3.5, 11, 6), 3, 3);
                context.DrawEllipse(brush, null, new Point(9, 6.5), 1.6, 1.6);
                break;
            default:
                context.DrawEllipse(brush, null, new Point(6.5, 6.5), 3, 3);
                break;
        }
    }
}
