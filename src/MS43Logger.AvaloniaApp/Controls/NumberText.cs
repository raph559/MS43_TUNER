using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// UI text with its numbers set in Bahnschrift with tabular figures (Run.num), and hex values and XDF ids in Cascadia
/// (Run.id), as system 3.1 sets them; an id keeps its digits ("ip_iga_ron98_pl__n__maf" is one run). <see cref="IsId"/>
/// sets the whole value as an id ("ldpm_n_14").
/// </summary>
public sealed partial class NumberText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<NumberText, string?>(nameof(Value));

    public static readonly StyledProperty<bool> IsIdProperty = AvaloniaProperty.Register<NumberText, bool>(nameof(IsId));

    static NumberText()
    {
        ValueProperty.Changed.AddClassHandler<NumberText>((text, _) => text.Build());
        IsIdProperty.Changed.AddClassHandler<NumberText>((text, _) => text.Build());
    }

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public bool IsId { get => GetValue(IsIdProperty); set => SetValue(IsIdProperty, value); }

    // Styled as a TextBlock: every TextBlock rule (label, body, the key line's 12 px Ink2) applies to it.
    protected override System.Type StyleKeyOverride => typeof(TextBlock);

    // An id is a word with an underscore, as every XDF id here has one; then hex; then a number with its minus.
    [GeneratedRegex(@"(?<id>\b[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]*)+|0x[0-9A-Fa-f]+)|-?\d+(?:[.,]\d+)*")]
    private static partial Regex Parts();

    private void Build()
    {
        var text = Value ?? string.Empty;
        var inlines = new InlineCollection();
        if (IsId)
        {
            inlines.Add(Styled(text, "id"));
        }
        else
        {
            var at = 0;
            foreach (Match match in Parts().Matches(text))
            {
                if (match.Index > at)
                {
                    inlines.Add(new Run(text[at..match.Index]));
                }

                inlines.Add(Styled(match.Value, match.Groups["id"].Success ? "id" : "num"));
                at = match.Index + match.Length;
            }

            if (at < text.Length)
            {
                inlines.Add(new Run(text[at..]));
            }
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
