using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

public class NumberTextTests
{
    private static List<(string Text, string Class)> Runs(NumberText text) =>
        text.Inlines!.OfType<Run>().Select(run => (run.Text ?? string.Empty, run.Classes.FirstOrDefault() ?? string.Empty)).ToList();

    [AvaloniaFact]
    public void NumbersAndHexValuesGetTheirFaces()
    {
        var text = new NumberText { Value = "33.4 °CRK, raw 152 (0x98)" };

        Assert.Equal(
            new[] { ("33.4", "num"), (" °CRK, raw ", string.Empty), ("152", "num"), (" (", string.Empty), ("0x98", "id"), (")", string.Empty) },
            Runs(text));
    }

    [AvaloniaFact]
    public void MinusAndRangesStayWithTheirNumbers()
    {
        var text = new NumberText { Value = "-23.6 to 72.0 °CRK" };

        Assert.Equal(new[] { ("-23.6", "num"), (" to ", string.Empty), ("72.0", "num"), (" °CRK", string.Empty) }, Runs(text));
    }

    [AvaloniaFact]
    public void IdsInsideTextKeepTheirDigits()
    {
        // An XDF id is set whole in Cascadia, as the key line's mockup sets it; its digits are not numbers of their own.
        var text = new NumberText { Value = "Undo: Step 2 cells of ip_iga_ron98_pl__n__maf by 1 raw" };

        Assert.Equal(
            new[] { ("Undo: Step ", string.Empty), ("2", "num"), (" cells of ", string.Empty), ("ip_iga_ron98_pl__n__maf", "id"), (" by ", string.Empty), ("1", "num"), (" raw", string.Empty) },
            Runs(text));
    }

    [AvaloniaFact]
    public void AnIdIsSetWhole()
    {
        var text = new NumberText { Value = "ldpm_n_14", IsId = true };

        Assert.Equal(new[] { ("ldpm_n_14", "id") }, Runs(text));
    }

    [AvaloniaFact]
    public void TextBlockStylesApply()
    {
        var text = new NumberText { Value = "2 cells changed since the last save", Classes = { "label" } };
        var window = new Avalonia.Controls.Window { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Content = text };
        window.Show();

        Assert.Equal(12, text.FontSize);
        Assert.Equal(Avalonia.Media.Color.Parse("#AEB4BD"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(text.Foreground).Color);
    }

    [AvaloniaFact]
    public void ValueChangesRebuildTheRuns()
    {
        var text = new NumberText { Value = "1 cell changed since the last save" };

        text.Value = "12 cells changed since the last save";

        Assert.Equal(new[] { ("12", "num"), (" cells changed since the last save", string.Empty) }, Runs(text));
        Assert.Equal("12 cells changed since the last save", text.Value);
    }
}
