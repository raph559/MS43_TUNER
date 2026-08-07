using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Tune rail can be dragged wider or narrower, collapsed to a strip, and remembers both.</summary>
public class RailPanelTests
{
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static double RailWidth(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return Find.Named<Control>(window, "RailHost").Bounds.Width;
    }

    private static Point Centre(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public async Task TheSearchIconSitsInsideTheField()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();
        var box = Find.Named<TextBox>(window, "SearchBox");
        var icon = box.GetVisualDescendants().OfType<StrokeIcon>().First();
        var left = icon.TranslatePoint(default, box)!.Value.X;

        Assert.InRange(left, 10, 12);
    }

    [AvaloniaFact]
    public async Task DraggingTheEdgeResizesAndIsRemembered()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        Assert.Equal(256, RailWidth(window));
        var start = Centre(Find.Named<Control>(window, "RailResizeHandle"), window);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(64, 0));
        Assert.Equal(320, RailWidth(window));
        window.MouseMove(start + new Point(900, 0));
        Assert.Equal(480, RailWidth(window));
        window.MouseMove(start + new Point(-900, 0));
        Assert.Equal(200, RailWidth(window));
        window.MouseMove(start + new Point(44, 0));
        window.MouseUp(start + new Point(44, 0), MouseButton.Left);

        Assert.Equal(300, RailWidth(window));
        Assert.Equal(300, shell.Tune.Settings.RailWidth);
        Assert.Equal(300, AppSettings.Load(shell.Tune.Settings.Folder).RailWidth);
    }

    [AvaloniaFact]
    public async Task ArrowKeysOnTheEdgeResizeAndDoubleClickResets()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var handle = Find.Named<Control>(window, "RailResizeHandle");
        handle.Focus(NavigationMethod.Tab);

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.Equal(272, RailWidth(window));
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal(240, RailWidth(window));

        var point = Centre(handle, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(256, RailWidth(window));
        Assert.Null(shell.Tune.Settings.RailWidth);
    }

    [AvaloniaFact]
    public async Task CollapsingLeavesAStripAndIsRemembered()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var list = Find.Named<ListBox>(window, "RailList");

        Click(Find.Named<Button>(window, "RailCollapseButton"));

        Assert.True(shell.Tune.RailCollapsed);
        Assert.Equal(48, RailWidth(window));
        Assert.False(list.IsEffectivelyVisible);
        Assert.True(Find.Named<Button>(window, "RailExpandButton").IsEffectivelyVisible);
        Assert.True(AppSettings.Load(shell.Tune.Settings.Folder).RailCollapsed);
        Screens.Save(window, "rail-collapsed");

        Click(Find.Named<Button>(window, "RailExpandButton"));

        Assert.False(shell.Tune.RailCollapsed);
        Assert.Equal(256, RailWidth(window));
        Assert.True(list.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task CtrlBTogglesTheRailAndCtrlKReopensItOnSearch()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Assert.True(shell.Tune.RailCollapsed);

        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Tune.RailCollapsed);
        Assert.True(Find.Named<TextBox>(window, "SearchBox").IsFocused);

        shell.Tune.SetRailCollapsed(true);
        Click(Find.Named<Button>(window, "RailSearchButton"));
        Assert.False(shell.Tune.RailCollapsed);
        Assert.True(Find.Named<TextBox>(window, "SearchBox").IsFocused);
    }

    [AvaloniaFact]
    public async Task ACustomWidthStaysAboveTheCompactStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        shell.Tune.SetRailWidth(300, save: true);
        window.Width = 1100;

        Assert.Equal(300, RailWidth(window));
    }
}
