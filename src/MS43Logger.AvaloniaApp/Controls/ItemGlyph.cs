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
}
