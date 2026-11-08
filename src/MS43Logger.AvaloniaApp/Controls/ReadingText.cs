using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A reading and its unit on one baseline (system 3): the numerals in the block's own face and size, then the unit
/// after an en space as a Run.unit, styled as UI text. "n/a" stands alone. Styled as a TextBlock.
/// </summary>
public sealed class ReadingText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<ReadingText, string?>(nameof(Value));

    public static readonly StyledProperty<string?> UnitProperty = AvaloniaProperty.Register<ReadingText, string?>(nameof(Unit));

    static ReadingText()
    {
        ValueProperty.Changed.AddClassHandler<ReadingText>((text, _) => text.Build());
        UnitProperty.Changed.AddClassHandler<ReadingText>((text, _) => text.Build());
    }

    /// <summary>The reading as shown: "2480", "112.4", or "n/a".</summary>
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    protected override System.Type StyleKeyOverride => typeof(TextBlock);

    private void Build()
    {
        // The runs touch: no whitespace may sit between the numerals and the unit's own en space.
        var inlines = new InlineCollection { new Run(Value ?? string.Empty) };
        if (!string.IsNullOrEmpty(Unit) && Value != "n/a")
        {
            var unit = new Run(" " + Unit);
            unit.Classes.Add("unit");
            inlines.Add(unit);
        }

        Inlines = inlines;
    }
}
