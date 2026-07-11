using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Theme;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The gear cell's slots: 1 to 5 in Data 24 over a 3 px Track line; the engaged gear in Ink1 SemiBold over a
/// Now line (Ink3 once the link is not live). With no gear (neutral, clutch in, no value) no slot is lit and "n/a"
/// follows the slots, so the state never rests on colour alone.
/// </summary>
public sealed class GearSlots : Control
{
    public static readonly StyledProperty<int?> GearProperty = AvaloniaProperty.Register<GearSlots, int?>(nameof(Gear));
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<GearSlots, bool>(nameof(IsLive));

    private const int Slots = 5;
    private const double Slot = 24;
    private const double Gap = 4;
    private const double Line = 28;

    static GearSlots()
    {
        AffectsRender<GearSlots>(GearProperty, IsLiveProperty);
        GearProperty.Changed.AddClassHandler<GearSlots>((slots, _) =>
            AutomationProperties.SetName(slots, "Gear " + (slots.Gear?.ToString(CultureInfo.InvariantCulture) ?? "n/a")));
    }

    public GearSlots()
    {
        AutomationProperties.SetName(this, "Gear n/a");
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The engaged gear, 1 to 5, or null.</summary>
    public int? Gear { get => GetValue(GearProperty); set => SetValue(GearProperty, value); }

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }

    // The slots, then room for "n/a" in UI 16 8 px after them.
    protected override Size MeasureOverride(Size availableSize) => new(Slots * Slot + (Slots - 1) * Gap + 8 + 28, Line + 2 + 3);

    public override void Render(DrawingContext context)
    {
        var data = ThemeBrushes.Font(this, "DataFont");
        var ink1 = ThemeBrushes.Get(this, "Ink1Brush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var track = ThemeBrushes.Get(this, "TrackBrush");
        var lit = Gear is >= 1 and <= Slots ? Gear : null;
        for (var i = 0; i < Slots; i++)
        {
            var on = lit == i + 1;
            var left = i * (Slot + Gap);
            var number = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(data, FontStyle.Normal, on ? FontWeight.SemiBold : FontWeight.Normal), 24, on ? ink1 : ink3);
            number.SetFontFeatures(Typography.Tabular);
            context.DrawText(number, new Point(left + (Slot - number.Width) / 2, (Line - number.Height) / 2));
            var underline = on ? ThemeBrushes.Get(this, IsLive ? "NowBrush" : "Ink3Brush") : track;
            context.DrawRectangle(underline, null, new Rect(left, Line + 2, Slot, 3));
        }

        if (lit is null)
        {
            var note = new FormattedText("n/a", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(ThemeBrushes.Font(this, "UiFont")), 16, ink3);
            context.DrawText(note, new Point(Slots * Slot + (Slots - 1) * Gap + 8, (Line - note.Height) / 2));
        }
    }
}
