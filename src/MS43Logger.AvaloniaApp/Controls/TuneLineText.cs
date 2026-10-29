using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// A result's tune line, "Tune: CAR.bin · 43X001 · unsaved edits in 3 items", each part in its own face:
/// the bin's file name as plain UI text, the firmware id in Cascadia (Run.id), the
/// count in Bahnschrift (Run.num); "No bin open" without a tune. Styled as a TextBlock.
/// </summary>
public sealed partial class TuneLineText : TextBlock
{
    public static readonly StyledProperty<TuneIdentity?> TuneProperty = AvaloniaProperty.Register<TuneLineText, TuneIdentity?>(nameof(Tune));

    static TuneLineText()
    {
        TuneProperty.Changed.AddClassHandler<TuneLineText>((line, _) => line.Build());
    }

    public TuneLineText()
    {
        Build();
    }

    public TuneIdentity? Tune { get => GetValue(TuneProperty); set => SetValue(TuneProperty, value); }

    protected override System.Type StyleKeyOverride => typeof(TextBlock);

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    private void Build()
    {
        var inlines = new InlineCollection();
        if (Tune is not { } tune)
        {
            inlines.Add(new Run(ResultText.TuneLine(null)));
            Inlines = inlines;
            return;
        }

        var (bin, firmware, edits) = ResultText.TuneParts(tune);
        inlines.Add(new Run("Tune: "));
        inlines.Add(new Run(bin));
        inlines.Add(new Run(" · "));
        inlines.Add(firmware is null ? new Run("unknown firmware") : Styled(firmware, "id"));
        inlines.Add(new Run(" · "));
        var at = 0;
        foreach (Match match in Digits().Matches(edits))
        {
            if (match.Index > at)
            {
                inlines.Add(new Run(edits[at..match.Index]));
            }

            inlines.Add(Styled(match.Value, "num"));
            at = match.Index + match.Length;
        }

        if (at < edits.Length)
        {
            inlines.Add(new Run(edits[at..]));
        }

        Inlines = inlines;
    }

    private static Run Styled(string text, string kind)
    {
        var run = new Run(text);
        run.Classes.Add(kind);
        return run;
    }
}
