using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The click-through scripts of the gate (plan Task 15): every button, menu item, switch and segment of a screen,
/// pressed headlessly with its effect asserted. Connect is the one control not pressed: it opens a real serial port.
/// </summary>
internal static class GateClickThrough
{
    private const string Picker = "asks the system picker; headless, none is chosen and nothing changes";

    /// <summary>The header and status bar, on every screen; their script is "shell".</summary>
    public static readonly string[] ShellControls =
    [
        "TabLive", "TabTune", "TabCompare", "TabPerformance", "FileMenuButton", "SaveButton", "ConnectionButton", "ConnectButton", "SettingsButton",
        "NightButton", "DayButton", "ReadoutsTiles", "ReadoutsDials", "RescanButton", "MinimizeButton", "MaximizeButton", "CloseButton",
    ];

    public static readonly (string Screen, Func<bool, Task<GateClicks?>> Run)[] Screens =
    [
        ("shell", ShellAsync), ("live", LiveAsync), ("tune", TuneAsync), ("compare", CompareAsync), ("settings", SettingsAsync),
        ("dialogs", DialogsAsync),
    ];

    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");

    private static bool IsShell(Control control) => ShellControls.Contains(control.Name);

    private static bool NotShell(Control control) => !IsShell(control);

    private static (MainWindow Window, ShellViewModel Shell) Open(bool night, Func<string[]> ports, double width = 1024, double height = 680, string? folder = null)
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(folder ?? TestShell.NewFolder()), new MainWindowViewModel(new ManualTime(), ports));
        shell.Live.SelectedVariant = "ms43x";
        window.Width = width;
        window.Height = height;
        window.Show();
        shell.IsNight = night;
        shell.IsDay = !night;
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static void Theme(ShellViewModel shell, bool night)
    {
        shell.IsNight = night;
        shell.IsDay = !night;
        Dispatcher.UIThread.RunJobs();
    }

    private static Task<GateClicks?> ShellAsync(bool night)
    {
        var scans = 0;
        var (window, shell) = Open(night, () => { scans++; return ["COM1"]; }, 1440, 900);
        var c = new GateClicks(window, shell);

        c.Press("TabTune", "shows the Tune screen", () => shell.SelectedTab == ShellViewModel.TuneTab);
        c.Press("TabCompare", "shows the Compare screen", () => shell.SelectedTab == ShellViewModel.CompareTab);
        c.Press("TabPerformance", "shows the Performance screen", () => shell.SelectedTab == ShellViewModel.PerformanceTab && shell.Performance.IsVisible);
        c.Press("TabLive", "shows the Live screen", () => shell.SelectedTab == ShellViewModel.LiveTab);

        var file = c.Named<Button>("FileMenuButton");
        c.Press(file, "opens the file menu", () => file.Flyout is { IsOpen: true });
        var items = ((MenuFlyout)file.Flyout!).Items.OfType<MenuItem>().ToList();
        c.Press(items.Single(item => item.Name == "OpenBinMenuItem"), "Open bin… " + Picker, () => shell.Tune.Document is null);
        c.Press(items.Single(item => item.Name == "UseXdfMenuItem"), "Use another XDF… goes to Tune and " + Picker, () => shell.SelectedTab == ShellViewModel.TuneTab);
        var saveAs = items.Single(item => item.Name == "SaveAsMenuItem");
        Assert.False(saveAs.IsEnabled);
        c.Note(saveAs, "Save as… is off without a bin; pressed in the tune script");
        file.Flyout!.Hide();
        shell.SelectedTab = ShellViewModel.LiveTab;

        var before = scans;
        c.Press("RescanButton", "lists the COM ports again", () => scans == before + 1);
        c.Note(c.Named<Button>("ConnectButton"), "not pressed: it opens the machine's real serial port (its label and states: ConnectIsPrimaryOnlyOnLiveAndPerformance, LiveStateTests); Disconnect is pressed in the live script");
        c.Press("SettingsButton", "opens Settings", () => c.Dialogs.IsOpen && window.GetVisualDescendants().OfType<SettingsView>().Any());
        c.Dialogs.Close();
        c.Press("DayButton", "switches to Day and saves it", () => shell.IsDay && shell.Tune.Settings.Theme == ThemeMode.Day);
        c.Press("NightButton", "switches to Night and saves it", () => shell.IsNight && shell.Tune.Settings.Theme == ThemeMode.Night);
        Theme(shell, night);
        c.Press("ReadoutsDials", "shows the band as dials and saves it", () => shell.IsDials && shell.Tune.Settings.Readouts == ReadoutsMode.Dials);
        c.Press("ReadoutsTiles", "shows the band as tiles and saves it", () => shell.IsTiles && shell.Tune.Settings.Readouts == ReadoutsMode.Tiles);
        c.Press("MinimizeButton", "minimizes the window", () => window.WindowState == WindowState.Minimized);
        window.WindowState = WindowState.Normal;
        c.Press("MaximizeButton", "maximizes the window, then restores it", () => window.WindowState == WindowState.Maximized);
        GateClicks.Activate(c.Named<Button>("MaximizeButton"));
        Assert.Equal(WindowState.Normal, window.WindowState);
        c.Note(c.Named<Button>("CloseButton"), "closes the window through the unsaved-edits prompt; pressed in TitleBarTests.CloseCloses");
        c.Note(c.Named<Button>("SaveButton"), "hidden without a bin; pressed in the tune script");
        c.Audit(window, IsShell);

        // Below 1280 px the port, rescan and definition fold into one button.
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();
        var connection = c.Named<Button>("ConnectionButton");
        c.Press(connection, "opens the port, rescan and definition flyout (1024 px)", () => connection.Flyout is { IsOpen: true });
        var flyout = (Control)((Flyout)connection.Flyout!).Content!;
        var compact = Find.Named<Button>(flyout, "CompactRescanButton");
        before = scans;
        c.Press(compact, "lists the COM ports again", () => scans == before + 1);
        c.Audit(flyout);
        connection.Flyout!.Hide();
        c.Audit(window, IsShell);

        foreach (var name in ShellControls)
        {
            Assert.Contains(c.Lines, line => line.StartsWith($"- {name}:", StringComparison.Ordinal));
        }

        return Task.FromResult<GateClicks?>(c);
    }

    private static Task<GateClicks?> LiveAsync(bool night)
    {
        var scans = 0;
        var ports = new[] { "COM1" };
        var (window, shell) = Open(night, () => { scans++; return ports; });
        var live = shell.Live;
        var c = new GateClicks(window, shell);

        c.Press("CsvSwitch", "turns the CSV log on", () => live.EnableCsvLogging);
        var path = live.CsvPath;
        c.Press("BrowseCsvButton", "Browse… " + Picker, () => live.CsvPath == path);
        c.Press("PerformanceButton", "opens the Performance tab", () => shell.IsPerformance);
        shell.SelectedTab = ShellViewModel.LiveTab;
        Dispatcher.UIThread.RunJobs();

        c.Press("PinnedOnlySwitch", "lists the pinned channels only", () => live.ShowPinnedOnly);
        live.ShowPinnedOnly = false;
        Dispatcher.UIThread.RunJobs();

        var pin = GateClicks.Pressable(window).First(control => AutomationProperties.GetName(control)?.StartsWith("Pin ", StringComparison.Ordinal) == true);
        var pinned = AutomationProperties.GetName(pin)!["Pin ".Length..];
        c.Press(pin, "pins the channel (the first row's; every row's button is the same template)", () => live.PinnedValues.Any(value => value.Title == pinned));
        var unpin = GateClicks.Pressable(window).First(control => AutomationProperties.GetName(control)?.StartsWith("Unpin ", StringComparison.Ordinal) == true);
        var unpinned = AutomationProperties.GetName(unpin)!["Unpin ".Length..];
        c.Press(unpin, "unpins the channel (the first pinned row's)", () => live.OtherValues.Any(value => value.Title == unpinned));
        c.CoverRows(control => AutomationProperties.GetName(control) is { } name && (name.StartsWith("Pin ", StringComparison.Ordinal) || name.StartsWith("Unpin ", StringComparison.Ordinal)));
        c.Audit(window, NotShell);

        live.SearchText = "zzzz";
        Dispatcher.UIThread.RunJobs();
        var clear = GateClicks.Pressable(window).Single(control => control is Button { Content: "Clear filter" });
        c.PressLast(clear, window, "empties the filter (shown when nothing matches)", () => live.SearchText.Length == 0, NotShell);

        live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));
        Dispatcher.UIThread.RunJobs();
        c.PressLast(c.Named<Button>("ShowDetailsButton"), window, "opens the connection log", () => window.GetVisualDescendants().OfType<ConnectionLogView>().Any(), NotShell);
        c.Dialogs.Close();

        ports = [];
        live.RefreshPorts();
        Dispatcher.UIThread.RunJobs();
        var rescans = scans;
        c.PressLast(c.Named<Button>("BannerAction"), window, "Rescan ports (no port banner) lists the ports again", () => scans == rescans + 1, NotShell);
        ports = ["COM1"];
        live.RefreshPorts();

        live.IsConnected = true;
        Dispatcher.UIThread.RunJobs();
        c.Audit(window, NotShell);
        var disconnect = c.Named<Button>("ConnectButton");
        Assert.Equal("Disconnect", disconnect.Content);
        GateClicks.Activate(disconnect);
        Assert.False(live.IsConnected);
        c.Note("ConnectButton as Disconnect (while connected): stops logging, the button reads Connect again");

        return Task.FromResult<GateClicks?>(c);
    }

    private static async Task<GateClicks?> TuneAsync(bool night)
    {
        var c = await TuneMapAsync(night);
        if (c is null)
        {
            return null;
        }

        var shell = c.Shell;
        var tune = shell.Tune;

        // The other item kinds, in the same window.
        tune.SelectedItem = tune.Document!.Xdf.FindByTitle("c_lc_n_max");
        Dispatcher.UIThread.RunJobs();
        var raw = tune.Value!.Current.Raw;
        c.Press("SetValueButton", "stores the typed value, snapped to one the ECU can hold", () => tune.Value!.Current.Raw != raw, () => tune.Value!.Input = "6000");
        raw = tune.Value!.Current.Raw;
        c.Press("StepUpButton", "one step up", () => tune.Value!.Current.Raw > raw);
        raw = tune.Value!.Current.Raw;
        c.Press("StepDownButton", "one step down", () => tune.Value!.Current.Raw < raw);
        c.Audit(c.Window, NotShell);

        tune.SelectedItem = tune.Document.Xdf.FindByTitle("c_conf_load");
        Dispatcher.UIThread.RunJobs();
        var choice = c.Window.GetVisualDescendants().OfType<ListBoxItem>().First(row => row.DataContext is ChoiceItem item && item.Value != tune.Value!.Current.Raw);
        var chosen = ((ChoiceItem)choice.DataContext!).Value;
        choice.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(chosen, tune.Value!.Current.Raw);
        c.Note("Switch choices (list rows): choosing one writes it");
        c.Audit(c.Window, NotShell);

        tune.SelectedItem = tune.Document.Xdf.FindByTitle("lv_mil_ind_knk_lv_1");
        Dispatcher.UIThread.RunJobs();
        var on = tune.Value!.IsOn;
        c.Press("FlagSwitch", "flips the flag's bit", () => tune.Value!.IsOn != on);
        c.Audit(c.Window, NotShell);


        c.Merge(await TuneEmptyAsync(night));
        return c;
    }

    private static async Task<GateClicks?> TuneMapAsync(bool night)
    {
        if (!TuneScreen.Available)
        {
            return null;
        }

        var bin = TuneScreen.CopyCar(TestShell.NewFolder());
        var (window, shell) = await TuneScreen.OpenAsync("ip_iga_ron98_pl__n__maf", bin);
        window.Width = 1024;
        window.Height = 680;
        Theme(shell, night);
        var tune = shell.Tune;
        var item = tune.SelectedItem!;
        var c = new GateClicks(window, shell);

        c.Press("ModifiedOnlySwitch", "lists only the items this bin changed", () => tune.ModifiedOnly);
        tune.ModifiedOnly = false;
        c.Press("RailCollapseButton", "folds the rail to its strip and saves it", () => tune.RailCollapsed && tune.Settings.RailCollapsed);
        c.Press("RailExpandButton", "shows the rail again", () => !tune.RailCollapsed);
        tune.SetRailCollapsed(true);
        Dispatcher.UIThread.RunJobs();
        c.Press("RailSearchButton", "shows the rail on its search field", () => !tune.RailCollapsed && c.Named<TextBox>("SearchBox").IsFocused);
        c.Note(c.Named<Control>("RailResizeHandle"), "the rail's edge: dragged, arrow keys and double-click in RailPanelTests");
        var sort = c.Named<Button>("RailSortButton");
        c.Press(sort, "opens the sort menu", () => sort.Flyout is { IsOpen: true });
        var sorts = ((MenuFlyout)sort.Flyout!).Items.OfType<MenuItem>().ToList();
        foreach (var (name, key) in new[] { ("SortByAddress", ItemSort.Address), ("SortByKind", ItemSort.Kind), ("SortBySize", ItemSort.Size), ("SortByName", ItemSort.Name) })
        {
            c.Press(sorts.Single(item => item.Name == name), $"sorts the rail by {key.ToString().ToLowerInvariant()} and saves it",
                () => tune.Sort == key && tune.Settings.RailSort == key);
        }

        c.Press(sorts.Single(item => item.Name == "SortDescending"), "reverses the order", () => tune.SortDescending);
        c.Press(sorts.Single(item => item.Name == "SortAscending"), "puts it back", () => !tune.SortDescending);
        sort.Flyout!.Hide();
        c.Press("PinButton", "pins the item to the top of the rail", () => tune.Settings.IsPinned(item.Title));
        tune.TogglePin(item);

        var editor = tune.Editor!;
        double[,] Cells() => (double[,])editor.Values.Physical.Clone();
        bool Changed(double[,] before) => !Cells().Cast<double>().SequenceEqual(before.Cast<double>());
        editor.Selection = new CellRange(2, 2, 5, 5);
        tune.ToolValue = "1";
        var cells = Cells();
        c.Press("InterpolateTool", "fills the range between its corners", () => Changed(cells));
        cells = Cells();
        c.Press("SmoothTool", "averages each cell with its neighbours", () => Changed(cells));
        cells = Cells();
        c.Press("EditAxisX", "makes Interpolate and Smooth work along each row", () => tune.EditAxis == EditAxis.X && editor.Axis == EditAxis.X);
        c.Press("EditAxisY", "makes them work along each column", () => tune.EditAxis == EditAxis.Y && editor.Axis == EditAxis.Y);
        c.Press("EditAxisXY", "makes them work both ways again", () => tune.EditAxis == EditAxis.XY && editor.Axis == EditAxis.XY);
        c.Press("SetTool", "sets the selected cells to the value", () => Changed(cells));
        cells = Cells();
        c.Press("AddTool", "adds the value to the selected cells", () => Changed(cells));
        cells = Cells();
        c.Press("ScaleTool", "changes the selected cells by that many percent", () => Changed(cells), () => tune.ToolValue = "50");
        cells = Cells();
        c.Press("UndoTool", "takes back the last change", () => Changed(cells));
        cells = Cells();
        c.Press("RedoTool", "puts it back", () => Changed(cells));
        c.Press("DetailsToggle", "opens the inspector over the grid (1024 px)", () => tune.InspectorOpen);
        var surface = c.Named<Surface3D>("Surface");
        c.Press("SurfaceZoomInButton", "zooms the surface in", () => surface.Zoom > 1);
        c.Press("SurfaceZoomOutButton", "zooms it out", () => surface.Zoom < 1.0001);
        c.Press("SurfaceEnlargeButton", "opens the surface large in a dialog", () => c.Dialogs.IsOpen && window.GetVisualDescendants().OfType<SurfaceDialog>().Any());
        var large = c.Named<Surface3D>("LargeSurface");
        c.Press("LargeZoomInButton", "zooms the large surface in", () => large.Zoom > 1);
        c.Press("LargeZoomOutButton", "zooms it out", () => large.Zoom < 1.0001);
        c.Press("LargeResetButton", "puts turn, zoom and position back", () => large.Zoom == 1 && large.Pan == default);
        c.Press("CloseSurfaceButton", "closes the large surface", () => !c.Dialogs.IsOpen);
        c.Audit(window, NotShell);
        tune.InspectorOpen = false;
        Dispatcher.UIThread.RunJobs();

        var file = c.Named<Button>("FileMenuButton");
        GateClicks.Activate(file);
        var saveAs = ((MenuFlyout)file.Flyout!).Items.OfType<MenuItem>().Single(menu => menu.Name == "SaveAsMenuItem");
        c.Press(saveAs, "Save as… " + Picker, () => tune.Document!.Path == bin);
        file.Flyout!.Hide();
        var onDisk = File.ReadAllBytes(bin);
        c.Press("SaveButton", "writes the bin and reads Saved", () => !tune.Document!.IsModified && !File.ReadAllBytes(bin).SequenceEqual(onDisk));

        c.Audit(window, NotShell);
        tune.SearchText = "zzzz";
        Dispatcher.UIThread.RunJobs();
        c.PressLast(c.Named<Button>("ClearSearchButton"), window, "empties the search (shown when nothing matches)", () => tune.SearchText.Length == 0, NotShell);
        return c;
    }

    // No bin, then a bin that is not 512 KiB: each offers to open another.
    private static async Task<GateClicks> TuneEmptyAsync(bool night)
    {
        var (window, shell) = Open(night, () => ["COM1"]);
        shell.SelectedTab = ShellViewModel.TuneTab;
        Dispatcher.UIThread.RunJobs();
        var c = new GateClicks(window, shell);
        c.Press("OpenBinButton", "Open bin… (no bin) " + Picker, () => shell.Tune.Document is null);
        c.Press("OpenXdfButton", "Open XDF… (no bin) " + Picker, () => shell.Tune.Document is null);
        c.Audit(window, NotShell);

        var partial = Path.Combine(TestShell.NewFolder(), "backup.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        File.WriteAllBytes(partial, new byte[262144]);
        await shell.Tune.OpenAsync(partial, TuneScreen.Xdf ?? partial);
        Dispatcher.UIThread.RunJobs();
        c.PressLast(c.Named<Button>("ErrorActionButton"), window, "Open bin… (wrong size) " + Picker, () => shell.Tune.Document is null, NotShell);
        window.Close();
        return c;
    }

    private static async Task<GateClicks?> CompareAsync(bool night)
    {
        if (!TuneScreen.Available || Release is null)
        {
            return null;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: TuneScreen.CopyCar(TestShell.NewFolder()));
        window.Width = 1024;
        window.Height = 680;
        Theme(shell, night);
        shell.SelectedTab = ShellViewModel.CompareTab;
        Dispatcher.UIThread.RunJobs();
        var compare = shell.Compare;
        var c = new GateClicks(window, shell);

        var openB = GateClicks.Pressable(window).Single(control => control is Button { Content: "Open B…" });
        c.PressLast(openB, window, "Open B… (no B yet) " + Picker, () => compare.FileB is null, NotShell);
        Assert.True(compare.OpenB(Release));
        Dispatcher.UIThread.RunJobs();
        var map = compare.Result!.Items.Single(item => item.Item.Title == "ip_iga_ron98_pl__n__maf");
        compare.Selected = map;
        Dispatcher.UIThread.RunJobs();

        var fileB = compare.FileB;
        c.Press("ChangeBButton", "Change… " + Picker, () => compare.FileB == fileB);
        foreach (var (name, key) in new[] { ("KindMap", "map"), ("KindCurve", "curve"), ("KindAxis", "axis"), ("KindValue", "value"), ("KindAll", "all") })
        {
            c.Press(name, $"lists the {key} differences", () => compare.Kind == key);
        }

        var all = c.Named<ToggleButton>("KindAll");
        GateClicks.Activate(all);
        Assert.True(all.IsChecked == true && compare.Kind == "all");
        c.Note("KindAll pressed again: stays lit, since the filter has no \"none\"");

        compare.Selected = map;
        Dispatcher.UIThread.RunJobs();
        c.Press("ViewA", "shows A's values in the map colours", () => compare.View == "a");
        c.Press("ViewB", "shows B's values", () => compare.View == "b");
        c.Press("ViewDelta", "shows B − A", () => compare.View == "delta");
        c.Press("DeltaRaw", "reads B − A in stored counts and saves it", () => compare.DeltaMode == DeltaMode.Raw && compare.Settings.CompareDeltaMode == DeltaMode.Raw);
        c.Press("DeltaPercent", "reads B − A as a percentage of A", () => compare.DeltaMode == DeltaMode.Percent);
        c.Press("DeltaUnits", "reads B − A in the map's units", () => compare.DeltaMode == DeltaMode.Units);
        c.Press("SwapButton", "asks before swapping A and B", () => c.Dialogs.IsOpen && window.GetVisualDescendants().OfType<ConfirmActionDialog>().Any());
        c.Note(c.Named<Button>("ConfirmActionButton"), "Swap: B opens as A and A becomes B; pressed in CompareSwapTests on copies of both bins");
        c.Press("ConfirmCancelButton", "closes the question and swaps nothing", () => !c.Dialogs.IsOpen && compare.FileB == fileB);
        compare.Selected = map;
        Dispatcher.UIThread.RunJobs();
        c.Press("CompareCollapseButton", "folds the list to its strip and saves it", () => compare.RailCollapsed && compare.Settings.CompareRailCollapsed);
        c.Press("CompareExpandButton", "shows the list again", () => !compare.RailCollapsed);
        compare.SetRailCollapsed(true);
        Dispatcher.UIThread.RunJobs();
        c.Press("CompareSearchButton", "shows the list on its search field", () => !compare.RailCollapsed && c.Named<TextBox>("CompareSearchBox").IsFocused);
        c.Note(c.Named<Control>("CompareResizeHandle"), "the list's edge: dragged in CompareRailTests (the same RailSizer as the Tune rail's)");
        compare.Selected = map;
        Dispatcher.UIThread.RunJobs();
        c.Press("EditInTuneButton", "opens this item of A on Tune", () => shell.SelectedTab == ShellViewModel.TuneTab && shell.Tune.SelectedItem?.Title == map.Item.Title);
        // On Tune with a B open: the map as B or B − A, back to A, and B's values copied into the selection.
        Dispatcher.UIThread.RunJobs();
        c.Press("TuneViewB", "shows B's values of the map on Tune", () => shell.Tune.GridView == "b");
        c.Press("TuneViewDelta", "shows B − A on Tune", () => shell.Tune.GridView == "delta");
        c.Press("TuneDeltaRaw", "reads B − A in stored counts on Tune", () => shell.Tune.DeltaMode == DeltaMode.Raw && shell.Tune.ShownEditor!.DeltaMode == DeltaMode.Raw);
        c.Press("TuneDeltaPercent", "reads B − A as a percentage of A on Tune", () => shell.Tune.DeltaMode == DeltaMode.Percent);
        c.Press("TuneDeltaUnits", "reads B − A in the map's units on Tune", () => shell.Tune.DeltaMode == DeltaMode.Units);
        c.Press("TuneViewA", "shows A again, the one edited", () => shell.Tune.GridView == "a");
        c.Press("CopyFromBTool", "writes B's values into the selected cells", () => shell.Tune.UndoTip.StartsWith("Undo: Copy ", StringComparison.Ordinal),
            () => shell.Tune.Editor!.Selection = new MS43Logger.Core.Tuning.CellRange(3, 3, 3, 3));
        shell.Tune.Undo();
        shell.SelectedTab = ShellViewModel.CompareTab;
        compare.Selected = compare.Result!.Items.Single(item => item.Item.Title == map.Item.Title);
        Dispatcher.UIThread.RunJobs();
        c.PressLast(c.Named<Button>("CopyFromB"), window, "writes B's value of this item into A", () => shell.Tune.Document!.IsModified
            && compare.Result!.Items.All(item => item.Item.Title != map.Item.Title), NotShell);

        // Without a bin, Compare asks for one.
        var (empty, emptyShell) = Open(night, () => ["COM1"]);
        emptyShell.SelectedTab = ShellViewModel.CompareTab;
        Dispatcher.UIThread.RunJobs();
        var other = new GateClicks(empty, emptyShell);
        var openA = GateClicks.Pressable(empty).Single(control => control is Button { Content: "Open bin…" });
        other.PressLast(openA, empty, "Open bin… (Compare without a bin) " + Picker, () => emptyShell.Tune.Document is null, NotShell);
        c.Merge(other);
        empty.Close();
        return c;
    }

    private static Task<GateClicks?> SettingsAsync(bool night)
    {
        var (window, shell) = Open(night, () => ["COM1"]);
        var settings = shell.Tune.Settings;
        var c = new GateClicks(window, shell);
        GateClicks.Activate(c.Named<Button>("SettingsButton"));

        c.Press("ThemeDay", "Day, saved", () => settings.Theme == ThemeMode.Day && shell.IsDay);
        c.Press("ThemeFollow", "follows Windows, saved", () => settings.Theme == ThemeMode.FollowWindows);
        c.Press("ThemeNight", "Night, saved", () => settings.Theme == ThemeMode.Night && shell.IsNight);
        Theme(shell, night);
        c.Press("SettingsDials", "dials, saved", () => settings.Readouts == ReadoutsMode.Dials && shell.IsDials);
        c.Press("SettingsTiles", "tiles, saved", () => settings.Readouts == ReadoutsMode.Tiles && shell.IsTiles);
        c.Press("Trend30", "30 s trends, saved", () => settings.TrendSeconds == 30);
        c.Press("Trend60", "60 s trends, saved", () => settings.TrendSeconds == 60);
        c.Press("Trend10", "10 s trends, saved", () => settings.TrendSeconds == 10);
        var folder = settings.LogFolder;
        c.Press("LogFolderBrowse", "Browse… " + Picker, () => settings.LogFolder == folder);
        var start = settings.StartCsvOnConnect;
        c.Press("StartCsvSwitch", "starts the CSV log on connect, saved", () => settings.StartCsvOnConnect != start);
        c.Press("MapsRaw", "opens maps in raw values, saved", () => settings.OpenMapsRaw);
        c.Press("MapsPhysical", "opens maps in physical values, saved", () => !settings.OpenMapsRaw);
        c.Press("TrailOff", "no cursor trail, saved", () => settings.CursorTrail == TrailMode.Off);
        c.Press("TrailLong", "long cursor trail, saved", () => settings.CursorTrail == TrailMode.Long);
        c.Press("TrailShort", "short cursor trail, saved", () => settings.CursorTrail == TrailMode.Short);
        var reopen = settings.ReopenLastBin;
        c.Press("ReopenSwitch", "reopens the last bin at start, saved", () => settings.ReopenLastBin != reopen);
        var bak = settings.KeepBackup;
        c.Press("BakSwitch", "keeps a .bak before the first save, saved", () => settings.KeepBackup != bak);
        var log = settings.AppendEditLog;
        c.Press("EditLogSwitch", "adds changes to the edit log, saved", () => settings.AppendEditLog != log);
        c.PressLast(c.Named<Button>("CloseButton"), c.Dialogs, "closes Settings", () => !c.Dialogs.IsOpen);

        // A settings file that cannot be written: the banner's Try again.
        var parent = TestShell.NewFolder();
        Directory.CreateDirectory(parent);
        var blocker = Path.Combine(parent, "blocker");
        File.WriteAllText(blocker, string.Empty);
        var (blocked, blockedShell) = TestShell.Create(AppSettings.Load(Path.Combine(blocker, "settings")), new MainWindowViewModel(new ManualTime(), () => ["COM1"]));
        blocked.Show();
        Theme(blockedShell, night);
        var other = new GateClicks(blocked, blockedShell);
        GateClicks.Activate(other.Named<Button>("SettingsButton"));
        GateClicks.Activate(other.Named<ToggleButton>("Trend30"));
        Assert.True(other.Named<Border>("SaveErrorBanner").IsEffectivelyVisible);
        File.Delete(blocker);
        other.CoverRows(control => control.FindAncestorOfType<SettingsView>() is not null && control.Name != "TryAgainButton");
        other.PressLast(other.Named<Button>("TryAgainButton"), other.Dialogs, "writes the settings again and hides the banner (save failure)",
            () => !other.Named<Border>("SaveErrorBanner").IsEffectivelyVisible);
        c.Merge(other);
        blocked.Close();
        return Task.FromResult<GateClicks?>(c);
    }

    private static Task<GateClicks?> DialogsAsync(bool night)
    {
        var (window, shell) = Open(night, () => ["COM1"]);
        var c = new GateClicks(window, shell);
        foreach (var (name, choice) in new[] { ("CancelButton", SaveChoice.Cancel), ("DontSaveButton", SaveChoice.DontSave), ("SaveButton", SaveChoice.Save) })
        {
            var asked = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
            Dispatcher.UIThread.RunJobs();
            var dialog = window.GetVisualDescendants().OfType<ConfirmDialog>().Single();
            var button = Find.Named<Button>(dialog, name);
            if (choice == SaveChoice.Save)
            {
                c.PressLast(button, dialog, $"save prompt: answers {choice} and closes", () => asked.IsCompleted && asked.Result == choice && !c.Dialogs.IsOpen);
            }
            else
            {
                c.Press(button, $"save prompt: answers {choice} and closes", () => asked.IsCompleted && asked.Result == choice && !c.Dialogs.IsOpen);
            }
        }

        shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));
        shell.Live.ShowConnectionLog();
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<ConnectionLogView>().Single();
        c.PressLast(Find.Named<Button>(view, "CloseButton"), view, "connection log: closes", () => !c.Dialogs.IsOpen);
        return Task.FromResult<GateClicks?>(c);
    }
}
