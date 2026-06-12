using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;
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
        "TabLive", "TabTune", "TabCompare", "FileMenuButton", "SaveButton", "ConnectionButton", "ConnectButton", "SettingsButton",
        "NightButton", "DayButton", "ReadoutsTiles", "ReadoutsDials", "RescanButton",
    ];

    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");

    private static bool IsShell(Control control) => ShellControls.Contains(control.Name);

    private static bool NotShell(Control control) => !IsShell(control);

    private static (MainWindow Window, ShellViewModel Shell) Open(bool night, Func<string[]> ports, double width = 1024, double height = 680)
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(TestShell.NewFolder()), new MainWindowViewModel(new ManualTime(), ports));
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
        c.Note(c.Named<Button>("ConnectButton"), "not pressed: it opens the machine's real serial port (its label and states: ConnectIsPrimaryOnlyOnLive, LiveStateTests); Disconnect is pressed in the live script");
        c.Press("SettingsButton", "opens Settings", () => c.Dialogs.IsOpen && window.GetVisualDescendants().OfType<SettingsView>().Any());
        c.Dialogs.Close();
        c.Press("DayButton", "switches to Day and saves it", () => shell.IsDay && shell.Tune.Settings.Theme == ThemeMode.Day);
        c.Press("NightButton", "switches to Night and saves it", () => shell.IsNight && shell.Tune.Settings.Theme == ThemeMode.Night);
        Theme(shell, night);
        c.Press("ReadoutsDials", "shows the band as dials and saves it", () => shell.IsDials && shell.Tune.Settings.Readouts == ReadoutsMode.Dials);
        c.Press("ReadoutsTiles", "shows the band as tiles and saves it", () => shell.IsTiles && shell.Tune.Settings.Readouts == ReadoutsMode.Tiles);
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
        c.Press("PerformanceButton", "opens the Performance tests window", () => window.OwnedWindows.OfType<PerformanceWindow>().Any());
        foreach (var owned in window.OwnedWindows.ToList())
        {
            owned.Close();
        }

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
}
