using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// Runs one screen's click-through: each control is activated the way assistive technology activates it (its
/// automation peer's Invoke or Toggle, a menu item's Click), its effect is asserted, and one line is kept for the record.
/// An audit, taken while a part of the window is shown, lists what no line covers there.
/// </summary>
internal sealed class GateClicks(Window window, ShellViewModel? shell)
{
    private readonly List<string> _lines = new();
    private readonly List<string> _gaps = new();
    private readonly HashSet<Control> _covered = new();
    private readonly HashSet<string> _names = new();
    private readonly List<Func<Control, bool>> _rows = new();

    public Window Window => window;

    public ShellViewModel Shell => shell!;

    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Controls an audit found that no line covers.</summary>
    public IReadOnlyList<string> Gaps => _gaps;

    public DialogHost Dialogs => Find.Named<DialogHost>(window, "Dialogs");

    /// <summary>The visible buttons, toggles, switches and segments a person can press, outside templates.</summary>
    public static IEnumerable<Control> Pressable(Visual root) => root.GetVisualDescendants().OfType<Control>()
        .Where(control => control is Button or MenuItem && control.TemplatedParent is null && control.IsEffectivelyVisible);

    public static string Label(Control control) =>
        !string.IsNullOrEmpty(control.Name) ? control.Name!
        : AutomationProperties.GetName(control) is { Length: > 0 } name ? name
        : (control as ContentControl)?.Content as string ?? control.GetType().Name;

    public T Named<T>(string name)
        where T : Control => Find.Named<T>(window, name);

    /// <summary>Presses a named control and checks what it did.</summary>
    public void Press(string name, string effect, Func<bool> holds, Action? arrange = null) => Press(Named<Control>(name), effect, holds, arrange);

    public void Press(Control control, string effect, Func<bool> holds, Action? arrange = null)
    {
        arrange?.Invoke();
        Dispatcher.UIThread.RunJobs();
        Activate(control);
        Assert.True(holds(), $"{Label(control)}: expected \"{effect}\"");
        Note(control, effect);
    }

    /// <summary>Presses the control that closes or changes a part of the window, after auditing that part.</summary>
    public void PressLast(Control control, Visual scope, string effect, Func<bool> holds, Func<Control, bool>? inScope = null)
    {
        Cover(control);
        Audit(scope, inScope);
        Press(control, effect, holds);
    }

    /// <summary>Takes another script's lines and gaps, from a second window.</summary>
    public void Merge(GateClicks other)
    {
        foreach (var line in other.Lines)
        {
            Note(line[2..]);
        }

        _gaps.AddRange(other.Gaps.Where(gap => !_gaps.Contains(gap)));
    }

    /// <summary>Records a control that is covered without being pressed here, with the reason.</summary>
    public void Note(Control control, string effect)
    {
        Cover(control);
        var line = $"- {Label(control)}: {effect}";
        if (!_lines.Contains(line))
        {
            _lines.Add(line);
        }
    }

    public void Note(string line) => _lines.Add("- " + line);

    /// <summary>Rows built from one template: the row pressed stands for every row the match finds.</summary>
    public void CoverRows(Func<Control, bool> match) => _rows.Add(match);

    /// <summary>Lists the pressable controls under <paramref name="scope"/> that no line covers.</summary>
    public void Audit(Visual scope, Func<Control, bool>? inScope = null)
    {
        foreach (var control in Pressable(scope).Where(control => (inScope?.Invoke(control) ?? true) && !IsCovered(control)))
        {
            var label = Label(control);
            if (!_gaps.Contains(label))
            {
                _gaps.Add(label);
            }
        }
    }

    private void Cover(Control control)
    {
        _covered.Add(control);
        if (!string.IsNullOrEmpty(control.Name))
        {
            _names.Add(control.Name!);
        }
    }

    private bool IsCovered(Control control) =>
        _covered.Contains(control) || (!string.IsNullOrEmpty(control.Name) && _names.Contains(control.Name!)) || _rows.Any(match => match(control));

    public static void Activate(Control control)
    {
        Assert.True(control.IsEffectivelyEnabled, $"{Label(control)} is disabled");
        if (control is MenuItem item)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        else
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(control);
            switch (peer)
            {
                case IToggleProvider toggle:
                    toggle.Toggle();
                    break;
                case IInvokeProvider invoke:
                    invoke.Invoke();
                    break;
                default:
                    throw new InvalidOperationException($"{Label(control)} has no way to be pressed");
            }
        }

        Dispatcher.UIThread.RunJobs();
    }
}
