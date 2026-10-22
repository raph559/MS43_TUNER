using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// As in TunerPro, several maps can be edited at the same time. A map, curve or axis
/// opens in a window of its own, any number at once, each edited on its own through the bin's one undo history.
/// </summary>
public class MapWindowTests
{
    private const string Ignition = "ip_iga_ron98_pl__n__maf";

    private static XdfTable Table(ShellViewModel shell, string title) => (XdfTable)shell.Tune.Document!.Xdf.FindByTitle(title)!;

    private static IEnumerable<XdfTable> EditableMaps(ShellViewModel shell) => shell.Tune.Document!.Xdf.Tables
        .Where(table => table.Z.Data is { Rows: > 1, Cols: > 1, UnsupportedReason: null });

    // Another editable map, to have two windows open at once.
    private static XdfTable OtherMap(ShellViewModel shell) => EditableMaps(shell).First(table => table.Title != Ignition);

    private static Point Centre(Control row, Visual window) => row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;

    private static void DoubleClick(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task EachMapOpensInOneWindowOfItsOwn()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var table = Table(shell, Ignition);

        var map = shell.Tune.OpenMapWindow(table);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(map, Assert.Single(shell.Tune.MapWindows));
        var view = Assert.Single(window.MapWindows);
        Assert.Same(map, view.DataContext);
        Assert.True(view.IsVisible);
        Assert.Same(window, view.Owner);
        Assert.Equal(Ignition, view.Title);
        Assert.Same(map.Editor, Find.Named<MapGrid>(view, "MapGrid").Editor);
        Assert.Equal(Ignition, Find.Named<TextBlock>(view, "ItemTitle").Text);

        // Opening it again brings the same window forward, restored, instead of a second one.
        view.WindowState = WindowState.Minimized;
        Assert.Same(map, shell.Tune.OpenMapWindow(table));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(window.MapWindows);
        Assert.Equal(WindowState.Normal, view.WindowState);

        var other = shell.Tune.OpenMapWindow(OtherMap(shell));
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(map, other);
        Assert.Equal(2, shell.Tune.MapWindows.Count);
        Assert.Equal(2, window.MapWindows.Count);
    }

    // A window opens to edit its grid: the grid has the keys, so typing a number and Enter sets the cell.
    [AvaloniaFact]
    public async Task TheGridHasTheKeysWhenTheWindowOpens()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);
        Assert.True(Find.Named<MapGrid>(view, "MapGrid").IsFocused);

        view.KeyTextInput("12");
        view.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(12, map.Editor.Values.Physical[0, 0], 0.5);
        Assert.True(shell.Tune.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task AnEditInAWindowShowsInTheMainPaneAndUndoesFromEither()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);
        var before = shell.Tune.Editor!.Values.Physical[0, 0];

        // Through the window's own toolbar: the value box, then Add.
        map.Editor.Selection = new CellRange(0, 0, 0, 0);
        Find.Named<TextBox>(view, "ToolValue").Text = "1";
        Find.Named<Button>(view, "AddTool").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var after = map.Editor.Values.Physical[0, 0];
        Assert.NotEqual(before, after);
        Assert.Equal(after, shell.Tune.Editor!.Values.Physical[0, 0]);
        Assert.True(map.CanUndo);
        Assert.StartsWith("Undo: Add 1", map.UndoTip);
        Assert.Contains(Ignition, map.UndoTip);

        // The main window's undo takes back the window's edit, and the window's redo puts it back.
        shell.Tune.Undo();
        Assert.Equal(before, map.Editor.Values.Physical[0, 0]);
        Assert.Equal(before, shell.Tune.Editor!.Values.Physical[0, 0]);
        map.Redo();
        Assert.Equal(after, shell.Tune.Editor!.Values.Physical[0, 0]);

        // Ctrl Z and Ctrl Y in the map window reach the same history.
        view.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, map.Editor.Values.Physical[0, 0]);
        view.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(after, map.Editor.Values.Physical[0, 0]);
    }

    [AvaloniaFact]
    public async Task CtrlSInAMapWindowSavesTheBin()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "ms43-map-window-" + Guid.NewGuid().ToString("N"));
        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(folder));
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);
        map.Editor.Selection = new CellRange(0, 0, 0, 0);
        map.ToolValue = "1";
        map.RunTool(EditKind.Add);
        Assert.True(shell.Tune.HasUnsavedChanges);

        view.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
        Dispatcher.UIThread.RunJobs();

        Assert.False(shell.Tune.HasUnsavedChanges);
    }

    // A save from a map window that fails says so in that window, not only on the Tune tab behind it.
    [AvaloniaFact]
    public async Task ASaveThatFailsSaysSoInTheWindow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "ms43-map-window-" + Guid.NewGuid().ToString("N"));
        var bin = TuneScreen.CopyCar(folder);
        var (window, shell) = await TuneScreen.OpenAsync(binPath: bin);
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);
        map.Editor.Selection = new CellRange(0, 0, 0, 0);
        map.ToolValue = "1";
        map.RunTool(EditKind.Add);
        File.SetAttributes(bin, FileAttributes.ReadOnly);
        try
        {
            view.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            Dispatcher.UIThread.RunJobs();

            Assert.True(shell.Tune.HasUnsavedChanges);
            Assert.NotNull(map.ErrorMessage);
            Assert.True(Find.Named<Border>(view, "ErrorBanner").IsEffectivelyVisible);
            Assert.Equal(map.ErrorMessage, Find.Named<TextBlock>(view, "EditError").Text);
        }
        finally
        {
            File.SetAttributes(bin, FileAttributes.Normal);
        }
    }

    [AvaloniaFact]
    public async Task WindowsCloseWhenAnotherBinOpens()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        shell.Tune.OpenMapWindow(OtherMap(shell));
        Dispatcher.UIThread.RunJobs();
        var views = window.MapWindows.ToList();
        Assert.Equal(2, views.Count);
        var held = new List<bool>();
        map.PropertyChanged += (_, e) => held.AddRange(e.PropertyName == nameof(MapWindowViewModel.IsHeld) ? [map.IsHeld] : []);

        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        Dispatcher.UIThread.RunJobs();

        // The windows took no edits while the new bin was read, then closed with the old one.
        Assert.Contains(true, held);
        Assert.Empty(shell.Tune.MapWindows);
        Assert.Empty(window.MapWindows);
        Assert.All(views, view => Assert.False(view.IsVisible));
        Assert.True(map.IsClosed);
    }

    // between "Open bin" and the new bin being read (the save prompt, then the file picker, which only blocks the
    // main window) an edit in a map window went into the old bin and was dropped without a question.
    [AvaloniaFact]
    public async Task TheWindowsTakeNoEditsWhileAnotherBinIsChosen()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);
        var pane = Find.Named<DockPanel>(view, "MapPane");
        map.Editor.Selection = new CellRange(0, 0, 0, 0);
        map.ToolValue = "1";
        map.RunTool(EditKind.Add);
        bool? heldAtPrompt = null;
        bool? enabledAtPrompt = null;
        shell.Tune.AskToSave = _ =>
        {
            heldAtPrompt = map.IsHeld;
            enabledAtPrompt = pane.IsEffectivelyEnabled;
            return Task.FromResult(SaveChoice.Cancel);
        };

        await window.GetVisualDescendants().OfType<TuneView>().Single().OpenBinAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(heldAtPrompt);
        Assert.False(enabledAtPrompt);
        Assert.False(map.IsHeld);
        Assert.True(pane.IsEffectivelyEnabled);
        Assert.False(map.IsClosed);
    }

    // the main window's prompts are drawn inside it, and an owned map window stays above it, so the save prompt on
    // closing could sit hidden under a map. While a prompt is open the map windows step aside.
    [AvaloniaFact]
    public async Task MapWindowsStepAsideForTheMainWindowsPrompts()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        shell.Tune.OpenMapWindow(Table(shell, Ignition));
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(window.MapWindows);

        var prompt = window.Dialogs.ShowAsync(new ConfirmDialog("Save changes?", "Detail"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(view.IsVisible);

        window.Dialogs.Close(SaveChoice.Cancel);
        await prompt;
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.IsVisible);
        Assert.Same(window, view.Owner);
        Assert.Single(window.MapWindows);
    }

    [AvaloniaFact]
    public async Task ClosingAWindowForgetsIt()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var table = Table(shell, Ignition);
        var map = shell.Tune.OpenMapWindow(table);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(window.MapWindows).Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(shell.Tune.MapWindows);
        Assert.Empty(window.MapWindows);
        Assert.True(map.IsClosed);
        Assert.NotSame(map, shell.Tune.OpenMapWindow(table));
    }

    [AvaloniaFact]
    public async Task TheLiveCursorShowsInEveryWindowOnAnyTab()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var map = shell.Tune.OpenMapWindow(Table(shell, Ignition));
        var other = shell.Tune.OpenMapWindow(Table(shell, "ip_iga_ron91_pl__n__maf"));
        Dispatcher.UIThread.RunJobs();
        var view = window.MapWindows.Single(found => ReferenceEquals(found.DataContext, map));
        shell.SelectedTab = ShellViewModel.LiveTab;
        Assert.False(shell.Tune.LiveCursor.IsVisible);

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        map.LiveCursor.Tick();
        other.LiveCursor.Tick();
        Dispatcher.UIThread.RunJobs();

        var live = map.Editor.Live!;
        Assert.Equal((5, 1), (live.Fix.Cells[0].Row, live.Fix.Cells[0].Col));
        Assert.NotNull(other.Editor.Live);
        Assert.StartsWith("Live 2480 rpm · ", map.LiveCursor.Readout);
        Assert.Equal(map.LiveCursor.Readout, Find.Named<TextBlock>(view, "LiveReadout").Text);

        // The Settings trail reaches the windows too.
        shell.Tune.SetCursorTrail(TrailMode.Long);
        Assert.Equal(TrailMode.Long, map.LiveCursor.Trail);
        Assert.Equal(TrailMode.Long, other.LiveCursor.Trail);

        // The logger stopping drops the cursor in the windows too.
        shell.Live.IsConnected = false;
        Assert.Null(map.Editor.Live);
        Assert.Null(other.Editor.Live);
    }

    [AvaloniaFact]
    public async Task TheTitleRowOpensTheMapInAWindow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var button = Find.Named<Button>(window, "OpenWindowButton");
        Assert.True(button.IsEffectivelyVisible);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Ignition, Assert.Single(shell.Tune.MapWindows).Table.Title);
        Assert.Single(window.MapWindows);

        // Values, switches and flags stay in the main pane.
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.Constants.First();
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task DoubleClickingARailMapOpensItsWindow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null);
        var tune = shell.Tune;
        tune.ToggleCategory("Ignition");
        Dispatcher.UIThread.RunJobs();
        var list = Find.Named<ListBox>(window, "RailList");
        var index = tune.Rail.Select((entry, i) => (entry, i)).First(pair => pair.entry is RailItem { Item.Title: Ignition }).i;
        list.ScrollIntoView(index);
        Dispatcher.UIThread.RunJobs();

        // The first click shows the map and rebuilds the rail (Recent), so the second lands on a new row: a double-tap
        // gesture, which wants both presses on one element, never came.
        DoubleClick(window, Centre(list.ContainerFromIndex(index)!, window));

        Assert.Equal(Ignition, Assert.Single(tune.MapWindows).Table.Title);
    }

    // with the rail at its top, the first click moves a Recent row to the top of Recent and the row above slides
    // under the pointer; the second press opened that one. The window is the map the first click picked.
    [AvaloniaFact]
    public async Task DoubleClickingARecentRowOpensThatMap()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null);
        var tune = shell.Tune;
        var maps = EditableMaps(shell).Take(4).ToList();
        foreach (var picked in Enumerable.Reverse(maps))
        {
            tune.SelectedItem = picked;
        }

        Dispatcher.UIThread.RunJobs();
        var list = Find.Named<ListBox>(window, "RailList");
        list.ScrollIntoView(0);
        Dispatcher.UIThread.RunJobs();
        var target = maps[2];
        var index = tune.Rail.Select((entry, i) => (entry, i)).First(pair => pair.entry is RailItem row && ReferenceEquals(row.Item, target)).i;

        DoubleClick(window, Centre(list.ContainerFromIndex(index)!, window));

        Assert.Same(target, Assert.Single(tune.MapWindows).Table);
        Assert.Same(target, tune.SelectedItem);
    }

    // a right press selected the row, the rail rebuilt under the pointer and the menu then opened on another row,
    // or not at all. The menu is the pressed row's, and the main pane keeps its map.
    [AvaloniaFact]
    public async Task RightClickingARailRowOffersOpenInWindowForThatRow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;
        var other = OtherMap(shell);
        var list = Find.Named<ListBox>(window, "RailList");
        tune.SearchText = other.Title;
        Dispatcher.UIThread.RunJobs();
        var index = tune.Rail.Select((entry, i) => (entry, i)).First(pair => pair.entry is RailItem row && ReferenceEquals(row.Item, other)).i;
        list.ScrollIntoView(index);
        Dispatcher.UIThread.RunJobs();
        var at = Centre(list.ContainerFromIndex(index)!, window);

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        var menu = window.GetVisualDescendants().OfType<TuneView>().Single().RowMenu;
        Assert.True(menu.IsOpen);
        Assert.Equal(Ignition, tune.SelectedItem?.Title);
        var open = menu.Items.OfType<MenuItem>().Single(item => item.Name == "OpenWindowItem");
        Assert.Equal("Open in window", open.Header);
        Assert.True(open.IsVisible);
        Assert.Equal("Pin", menu.Items.OfType<MenuItem>().Single(item => item.Name == "PinItem").Header);

        open.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(other, Assert.Single(tune.MapWindows).Table);
    }

    // Shift F10 and the menu key raise the request on the focused row, which the menu never heard.
    [AvaloniaFact]
    public async Task TheRowMenuOpensFromTheKeyboard()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;
        var list = Find.Named<ListBox>(window, "RailList");
        var index = tune.Rail.Select((entry, i) => (entry, i)).First(pair => pair.entry is RailItem { Item.Title: Ignition }).i;
        list.ScrollIntoView(index);
        Dispatcher.UIThread.RunJobs();
        var row = list.ContainerFromIndex(index)!;
        row.Focus(NavigationMethod.Tab);

        row.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        var menu = window.GetVisualDescendants().OfType<TuneView>().Single().RowMenu;
        Assert.True(menu.IsOpen);
        menu.Items.OfType<MenuItem>().Single(item => item.Name == "PinItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(tune.Settings.IsPinned(Ignition));

        // A value's row offers Pin only.
        var value = tune.Document!.Xdf.Constants.First();
        tune.SearchText = value.Title;
        Dispatcher.UIThread.RunJobs();
        var valueIndex = tune.Rail.Select((entry, i) => (entry, i)).First(pair => pair.entry is RailItem found && ReferenceEquals(found.Item, value)).i;
        list.ScrollIntoView(valueIndex);
        Dispatcher.UIThread.RunJobs();
        list.ContainerFromIndex(valueIndex)!.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.Items.OfType<MenuItem>().Single(item => item.Name == "OpenWindowItem").IsVisible);
    }
}
