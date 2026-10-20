using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// An item's kind as its icon from the app's set (system 6): the Kind.* geometry drawn exactly as a StrokeIcon draws it,
/// in the text colour of the row around it.
/// </summary>
public sealed class ItemGlyph : Control
{
    public static readonly StyledProperty<ItemKind> KindProperty = AvaloniaProperty.Register<ItemGlyph, ItemKind>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<ItemGlyph>();

    static ItemGlyph()
    {
        AffectsRender<ItemGlyph>(KindProperty, ForegroundProperty);
    }

    public ItemGlyph()
    {
        Width = 16;
        Height = 16;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public ItemKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        var key = "Kind." + Kind;
        StrokeIcon.Draw(
            context,
            this.TryFindResource(key, ActualThemeVariant, out var data) ? data as Geometry : null,
            this.TryFindResource(key + ".Fill", ActualThemeVariant, out var fill) ? fill as Geometry : null,
            Foreground ?? ThemeBrushes.Get(this, "Ink2Brush"),
            Bounds.Size);
    }
}
