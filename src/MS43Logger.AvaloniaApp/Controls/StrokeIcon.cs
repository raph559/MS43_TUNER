using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// One icon of the app's set: a geometry on a 16 px grid stroked at 1.5 px with flat caps and mitre joins,
/// so its terminals are cut square like Bahnschrift's strokes. It takes the text colour of the control around it.
/// </summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    /// <summary>Filled parts, such as a pinned pin's body.</summary>
    public static readonly StyledProperty<Geometry?> FillDataProperty = AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(FillData));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<StrokeIcon>();

    static StrokeIcon()
    {
        AffectsRender<StrokeIcon>(DataProperty, FillDataProperty, ForegroundProperty);
    }

    public StrokeIcon()
    {
        Width = 16;
        Height = 16;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public Geometry? FillData { get => GetValue(FillDataProperty); set => SetValue(FillDataProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Pen CreatePen(IBrush brush) => new(brush, 1.5, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter);

    public override void Render(DrawingContext context) =>
        Draw(context, Data, FillData, Foreground ?? ThemeBrushes.Get(this, "Ink2Brush"), Bounds.Size);

    /// <summary>Draws an icon of the set: the filled parts, then the 1.5 px flat-capped stroke (also used by ItemGlyph).</summary>
    internal static void Draw(DrawingContext context, Geometry? data, Geometry? fill, IBrush brush, Size size)
    {
        if (fill is not null)
        {
            context.DrawGeometry(brush, null, Scaled(fill, size));
        }

        if (data is not null)
        {
            context.DrawGeometry(null, new Pen(brush, 1.5, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter), Scaled(data, size));
        }
    }

    // The shape scales to the control's size; the stroke stays 1.5 px at every size.
    private static Geometry Scaled(Geometry geometry, Size size)
    {
        if (size.Width == 16 && size.Height == 16)
        {
            return geometry;
        }

        var copy = geometry.Clone();
        copy.Transform = new ScaleTransform(size.Width / 16, size.Height / 16);
        return copy;
    }
}
