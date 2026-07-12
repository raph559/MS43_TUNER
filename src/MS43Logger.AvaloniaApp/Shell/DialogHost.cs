using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Shell;

/// <summary>
/// Shows one modal dialog inside the main window (system 5.9): a scrim over the whole window and the dialog card
/// centred on it. Esc closes it, Enter presses its primary button, Tab stays inside it, and focus goes back to where
/// it was. While a dialog is open the window's shortcuts stand still (MainWindow checks <see cref="IsOpen"/>).
/// </summary>
public sealed class DialogHost : Panel
{
    private readonly Border _card;
    private TaskCompletionSource<object?>? _result;
    private IInputElement? _returnFocus;

    public DialogHost()
    {
        IsVisible = false;
        var scrim = new Border();
        scrim.Bind(Border.BackgroundProperty, this.GetResourceObservable("ScrimBrush"));
        _card = new Border
        {
            Classes = { "dialog" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(scrim);
        Children.Add(_card);
        AddHandler(KeyDownEvent, OnTab, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Bubble);
    }

    public bool IsOpen => _result is not null;

    /// <summary>
    /// Opens <paramref name="content"/> as the dialog; the task ends with what <see cref="Close"/> was given. A dialog
    /// that is already open is closed first and its task ends with null, as if cancelled (the save prompt can be asked
    /// for by the window's close button while Settings is open). Opened from the keyboard, pass
    /// <see cref="NavigationMethod.Tab"/> so the first control shows the focus ring.
    /// </summary>
    public Task<object?> ShowAsync(Control content, NavigationMethod focusWith = NavigationMethod.Unspecified)
    {
        Close();

        // The caller resumes after Close has finished, never inside it (Close runs from key and click handlers).
        _result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        _card.MaxHeight = Math.Max(0, Bounds.Height - 48);
        _card.Child = content;
        IsVisible = true;
        Dispatcher.UIThread.Post(() => FocusFirst(focusWith), DispatcherPriority.Loaded);
        return _result.Task;
    }

    public void Close(object? result = null)
    {
        if (_result is not { } pending)
        {
            return;
        }

        _result = null;
        IsVisible = false;
        _card.Child = null;
        _returnFocus?.Focus();
        _returnFocus = null;
        pending.SetResult(result);
    }

    /// <summary>Closes the dialog that holds <paramref name="content"/>.</summary>
    public static void CloseFrom(Visual content, object? result = null) =>
        content.FindAncestorOfType<DialogHost>()?.Close(result);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _card.MaxHeight = Math.Max(0, e.NewSize.Height - 48);
    }

    // A dialog opens on its first control, where typing starts. One with nothing to type in opens on its primary
    // button instead: a focused button takes Enter for itself, and Enter must do what the dialog is for (5.9).
    private void FocusFirst(NavigationMethod method)
    {
        var stops = TabStops();
        var primary = stops.Any(stop => stop is TextBox { IsReadOnly: false }) ? null : stops.OfType<Button>().LastOrDefault(IsPrimary);
        (primary ?? stops.FirstOrDefault())?.Focus(method);
    }

    // The dialog's own controls in visual order, without the parts inside their templates.
    private List<InputElement> TabStops() => _card.GetVisualDescendants().OfType<InputElement>()
        .Where(element => element.TemplatedParent is null && element.Focusable && element.IsTabStop
                          && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)
        .ToList();

    // Tab and Shift Tab go round the dialog's controls and never leave it. Avalonia's Cycle tab mode is not used:
    // it hung the headless test run in this dialog.
    private void OnTab(object? sender, KeyEventArgs e)
    {
        if (!IsOpen || e.Key != Key.Tab)
        {
            return;
        }

        e.Handled = true;
        var stops = TabStops();
        if (stops.Count == 0)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var index = stops.FindIndex(stop => focused is not null && (stop == focused || stop.IsVisualAncestorOf(focused)));
        var next = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? (index <= 0 ? stops.Count - 1 : index - 1)
            : (index + 1) % stops.Count;
        stops[next].Focus(NavigationMethod.Tab);
    }

    // Esc cancels. Enter that no focused control took (a focused button takes its own) presses the primary (5.9).
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter
                 && _card.GetVisualDescendants().OfType<Button>().LastOrDefault(IsPrimary) is { } primary
                 && ControlAutomationPeer.CreatePeerForElement(primary) is IInvokeProvider press)
        {
            press.Invoke();
            e.Handled = true;
        }
    }

    private static bool IsPrimary(Button button) =>
        button.Classes.Contains("primary") && button.IsEffectivelyVisible && button.IsEffectivelyEnabled;
}
