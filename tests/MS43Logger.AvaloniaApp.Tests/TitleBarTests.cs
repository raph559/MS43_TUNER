using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Tests;

public class TitleBarTests
{
    private static (MainWindow Window, ShellViewModel Shell) Show()
    {
        var (window, shell) = TestShell.Create();
        window.Show();
        return (window, shell);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TheHeaderReplacesTheSystemTitleBar()
    {
        var (window, _) = Show();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(ExtendClientAreaChromeHints.NoChrome, window.ExtendClientAreaChromeHints);
        Assert.Equal("MS43 Tuner", window.Title);
        foreach (var (name, label) in new[] { ("MinimizeButton", "Minimize"), ("MaximizeButton", "Maximize"), ("CloseButton", "Close") })
        {
            var button = Find.Named<Button>(window, name);
            Assert.True(button.IsEffectivelyVisible, name);
            Assert.Equal(label, AutomationProperties.GetName(button));
            Assert.Equal(label, ToolTip.GetTip(button));
        }

        Screens.Save(window, "titlebar-night");
        window.FindControl<ToggleButton>("DayButton")!.IsChecked = true;
        Screens.Save(window, "titlebar-day");
    }

    [AvaloniaFact]
    public void MinimizeMinimizes()
    {
        var (window, _) = Show();

        Click(Find.Named<Button>(window, "MinimizeButton"));

        Assert.Equal(WindowState.Minimized, window.WindowState);
    }

    [AvaloniaFact]
    public void MaximizeTogglesAndSaysRestore()
    {
        var (window, _) = Show();
        var maximize = Find.Named<Button>(window, "MaximizeButton");

        Click(maximize);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal("Restore down", AutomationProperties.GetName(maximize));
        Assert.Equal("Restore down", ToolTip.GetTip(maximize));
        Assert.True(Find.Named<Control>(window, "RestoreGlyph").IsEffectivelyVisible);
        Assert.False(Find.Named<Control>(window, "MaximizeGlyph").IsEffectivelyVisible);

        Click(maximize);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal("Maximize", AutomationProperties.GetName(maximize));
        Assert.True(Find.Named<Control>(window, "MaximizeGlyph").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void DoubleClickOnTheHeaderMaximizes()
    {
        var (window, _) = Show();
        var mark = Find.Named<Control>(window, "Wordmark");
        var point = mark.TranslatePoint(new Point(mark.Bounds.Width / 2, mark.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [AvaloniaFact]
    public void DoubleClickOnAHeaderControlDoesNotMaximize()
    {
        var (window, _) = Show();
        var tab = Find.Named<Control>(window, "TabTune");
        var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public void CloseCloses()
    {
        var (window, _) = Show();

        Click(Find.Named<Button>(window, "CloseButton"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void WindowButtonsStayUsableOverADialog()
    {
        var (window, _) = Show();
        window.KeyPress(Key.OemComma, RawInputModifiers.Control, PhysicalKey.Comma, ",");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Find.Named<Shell.DialogHost>(window, "Dialogs").IsOpen);

        var close = Find.Named<Button>(window, "CloseButton");
        var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
        Assert.Same(close, window.InputHitTest(point) is Visual hit ? hit.FindAncestorOfType<Button>(includeSelf: true) : null);
    }
}
