using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class HeaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-header-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static (MainWindow Window, ShellViewModel Shell) Show(double width = 1440, double height = 900)
    {
        var (window, shell) = TestShell.Create();
        window.Width = width;
        window.Height = height;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    [AvaloniaFact]
    public async Task SaveStateChipAndSaveFollowEdits()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var chip = Find.Named<TextBlock>(window, "SaveStateText");
        var save = Find.Named<Button>(window, "SaveButton");
        Assert.Equal("Saved", chip.Text);
        Assert.False(save.IsEffectivelyEnabled);

        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("1 item not saved", chip.Text);
        Assert.True(save.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void FileMenuHoldsOpenAndSaveAs()
    {
        var (window, _) = Show();

        var menu = Assert.IsType<MenuFlyout>(Find.Named<Button>(window, "FileMenuButton").Flyout);
        var headers = menu.Items.OfType<MenuItem>().Select(item => (item.Name, item.Header as string, item.InputGesture?.ToString())).ToList();

        Assert.Equal(
            new (string?, string?, string?)[]
            {
                ("OpenBinMenuItem", "Open bin…", "Ctrl+O"),
                ("UseXdfMenuItem", "Use another XDF…", null),
                ("SaveAsMenuItem", "Save as…", "Ctrl+Shift+S"),
            },
            headers);
    }

    [AvaloniaFact]
    public void CompactHeaderAt1024()
    {
        var (window, _) = Show(1024, 680);

        Assert.False(Find.Named<TextBlock>(window, "Wordmark").IsEffectivelyVisible);
        Assert.False(Find.Named<Control>(window, "ConnectionPickers").IsEffectivelyVisible);
        Assert.True(Find.Named<Button>(window, "ConnectionButton").IsEffectivelyVisible);

        window.Width = 1440;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Find.Named<TextBlock>(window, "Wordmark").IsEffectivelyVisible);
        Assert.True(Find.Named<Control>(window, "ConnectionPickers").IsEffectivelyVisible);
        Assert.False(Find.Named<Button>(window, "ConnectionButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task LongFileNameIsTrimmedNotClipped()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var name = "CAR_current_43X001_SP95_STAGE1_with_the_launch_control_and_cold_start_changes_v7.bin";
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.Copy(TuneScreen.Car!, path);
        var (window, _) = await TuneScreen.OpenAsync(binPath: path);
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();

        Assert.StartsWith(name + "\n", ToolTip.GetTip(Find.Named<Button>(window, "FileMenuButton")) as string);
        var outside = window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0)
            .Where(control => control.TranslatePoint(new Point(control.Bounds.Width, 0), window) is { } right && right.X > window.ClientSize.Width + 0.5)
            .Select(control => control.Name ?? control.GetType().Name)
            .ToList();
        Assert.Empty(outside);
        Screens.Save(window, "restyle-shell-night-1024-longname");
    }

    private const string LongName = "CAR_current_43X001_SP95_STAGE1_with_the_launch_control_and_cold_start_changes_v7.bin";

    private const string LongLog = "ms43-20261019-2225-pull-in-third-gear-from-2000-rpm-after-the-ignition-and-cold-start-change.csv";

    private static readonly string[] HeaderParts =
    {
        "Brand", "Tabs", "FileMenuButton", "SaveStateChip", "SaveButton", "RateText",
        "PortPicker", "RescanButton", "DefinitionPicker", "ConnectionButton", "ConnectButton", "SettingsButton",
    };

    [AvaloniaFact]
    public async Task HeaderAndStatusBarNeverOverlap()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var problems = new List<string>();
        Directory.CreateDirectory(_folder);
        foreach (var name in new[] { "CAR.bin", LongName })
        {
            var path = Path.Combine(_folder, name);
            File.Copy(TuneScreen.Car!, path, overwrite: true);
            var (window, shell) = await TuneScreen.OpenAsync(binPath: path);
            foreach (var dirty in new[] { false, true })
            {
                if (dirty)
                {
                    shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
                    shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
                }

                foreach (var logging in new[] { false, true })
                {
                    Log(shell.Live, logging);
                    foreach (var tab in new[] { ShellViewModel.TuneTab, ShellViewModel.LiveTab })
                    {
                        shell.SelectedTab = tab;
                        foreach (var (width, height) in new[] { (1024, 680), (1280, 800), (1366, 768), (1440, 900) })
                        {
                            window.Width = width;
                            window.Height = height;
                            Dispatcher.UIThread.RunJobs();
                            var state = $"{name.Length}-character name, {tab}, {width} px, {(dirty ? "dirty" : "clean")}, {(logging ? "logging" : "not connected")}";
                            problems.AddRange(RowProblems(window, HeaderParts.Select(part => Find.Named<Control>(window, part))).Select(problem => $"{state}: {problem}"));
                            problems.AddRange(RowProblems(window, StatusBarParts(window)).Select(problem => $"{state}: {problem}"));
                        }
                    }
                }
            }

            window.Close();
        }

        Assert.True(problems.Count == 0, $"{problems.Count} problems, first ones:\n" + string.Join("\n", problems.Take(30)));
    }

    /// <summary>Connected and logging with the widest rate ("slow link") and a long status line, or not connected.</summary>
    private static void Log(MainWindowViewModel live, bool logging)
    {
        live.SelectedPort = "COM1";
        live.SelectedVariant = "ms43x";
        live.IsConnected = logging;
        live.EnableCsvLogging = logging;
        live.CsvPath = @"C:\Users\owner\Documents\MS43 Logs\" + LongLog;
        live.ElapsedTime = "00:04:12";
        live.SampleCount = 9576;
        live.ShowRate(logging ? new RateStats(8, 125, 110, 140, 100) : null);
    }

    private static IEnumerable<Control> StatusBarParts(Window window) => new[]
    {
        (Control)Find.Named<Control>(window, "ConnectionDot").GetVisualParent()!,
        Find.Named<Control>(window, "ConnectionText"),
        Find.Named<Control>(window, "ReadoutsSeg"),
        window.GetVisualDescendants().OfType<Border>().Single(border => Avalonia.Automation.AutomationProperties.GetName(border) == "Theme"),
    };

    /// <summary>Parts of one row that overlap, run past the window, or draw content (or text) outside their own box.</summary>
    private static IEnumerable<string> RowProblems(Window window, IEnumerable<Control> parts)
    {
        var shown = parts.Where(part => part.IsEffectivelyVisible && part.Bounds.Width > 0).Select(part => (Part: part, Box: InWindow(part, window))).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var (part, box) = shown[i];
            if (box.Left < -0.5 || box.Right > window.ClientSize.Width + 0.5)
            {
                yield return $"{Label(part)} runs past the window ({box.Left:0} to {box.Right:0})";
            }

            for (var j = i + 1; j < shown.Count; j++)
            {
                var other = shown[j];
                if (box.Left < other.Box.Right - 0.5 && other.Box.Left < box.Right - 0.5)
                {
                    yield return $"{Label(part)} ({box.Left:0} to {box.Right:0}) overlaps {Label(other.Part)} ({other.Box.Left:0} to {other.Box.Right:0})";
                }
            }

            // A Popup element draws nothing itself; its content lives in its own top level.
            foreach (var inner in part.GetVisualDescendants().OfType<Control>().Where(control => control is not Popup && control.IsEffectivelyVisible))
            {
                var drawn = Clipped(inner, InWindow(inner, window), window);
                if (drawn.Width > 0 && drawn.Height > 0 && (drawn.Left < box.Left - 0.5 || drawn.Right > box.Right + 0.5))
                {
                    yield return $"{Label(inner)} ({drawn.Left:0} to {drawn.Right:0}) is drawn outside {Label(part)} ({box.Left:0} to {box.Right:0})";
                }
            }

            foreach (var text in part.GetSelfAndVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
            {
                var textBox = InWindow(text, window);
                var full = new Rect(textBox.X, textBox.Y, Math.Max(textBox.Width, text.TextLayout.Width), Math.Max(textBox.Height, text.TextLayout.Height));
                var ink = Clipped(text, full, window);
                if (ink.Width <= 0 || ink.Height <= 0)
                {
                    continue;
                }

                if (text.TextLayout.Width > text.Bounds.Width + 0.5)
                {
                    yield return $"\"{text.Text}\" in {Label(part)} is {text.TextLayout.Width:0} px wide in a {text.Bounds.Width:0} px box";
                }
                else if (ink.Width < full.Width - 0.5)
                {
                    yield return $"\"{text.Text}\" in {Label(part)} is cut off by a clip ({ink.Width:0} of {full.Width:0} px shown)";
                }
            }
        }
    }

    /// <summary>A box in window coordinates cut by every ancestor that clips; empty when none of it is drawn.</summary>
    private static Rect Clipped(Visual visual, Rect box, Visual window)
    {
        for (var parent = visual.GetVisualParent(); parent is not null && parent != window; parent = parent.GetVisualParent())
        {
            if (parent.ClipToBounds)
            {
                box = box.Intersect(InWindow(parent, window));
            }
        }

        return box;
    }

    private static Rect InWindow(Visual visual, Visual window) => new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(window)!.Value);

    private static string Label(Control control) => control.Name ?? control.GetType().Name;

    [AvaloniaFact]
    public async Task FileButtonTooltipHoldsTheXdfLine()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();

        Assert.Equal(
            "CAR_current_43X001_SP95_STAGE1.bin\nFirmware MS43X001 · XDF Siemens_MS43_MS43X001_512K.xdf",
            ToolTip.GetTip(Find.Named<Button>(window, "FileMenuButton")));
    }

    [AvaloniaFact]
    public void StatusLineTooltipHoldsTheWholeLine()
    {
        var (window, shell) = Show(1024, 680);

        Log(shell.Live, logging: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            "Connected to COM1 · ms43x · Logging 00:04:12 to " + LongLog + " · 9576 rows",
            ToolTip.GetTip(Find.Named<TextBlock>(window, "ConnectionText")));
    }

    [AvaloniaFact]
    public void AppMarkIsOrangeOnlyWhileLogging()
    {
        var mark = new AppMark();
        var window = new Window { Width = 40, Height = 40, RequestedThemeVariant = ThemeVariant.Dark, Content = mark };
        window.Show();
        var now = Color.Parse("#FF7A1A");

        Assert.False(HasColour(window, now));

        mark.IsLive = true;
        mark.Level = 0.31;

        Assert.True(HasColour(window, now));
    }

    [AvaloniaFact]
    public void StatusBarSaysNotConnected()
    {
        var (window, _) = Show();

        Assert.Equal("Not connected", Find.Named<TextBlock>(window, "ConnectionText").Inlines!.OfType<Avalonia.Controls.Documents.Run>().Aggregate(string.Empty, (text, run) => text + run.Text));
    }

    [AvaloniaFact]
    public void StatusLineDescribesTheSession()
    {
        var (_, shell) = Show();
        var live = shell.Live;
        live.SelectedPort = "COM1";
        live.SelectedVariant = "ms43x";
        live.IsConnected = true;
        live.EnableCsvLogging = true;
        live.CsvPath = @"C:\logs\ms43-20261019-2225.csv";
        live.ElapsedTime = "00:04:12";
        live.SampleCount = 9576;

        Assert.Equal(" to COM1 · ms43x · Logging 00:04:12 to ms43-20261019-2225.csv · 9576 rows", live.ConnectionDetail);
        Assert.Contains(live.StatusParts, part => part is { Text: "Connected", IsNow: true });
        Assert.Contains(live.StatusParts, part => part is { Text: "9576", IsNumber: true });

        live.EnableCsvLogging = false;

        Assert.Equal(" to COM1 · ms43x · Not writing a log", live.ConnectionDetail);
    }

    [AvaloniaFact]
    public void ReadoutsSegShowsOnLiveOnly()
    {
        var (window, shell) = Show();

        Assert.True(Find.Named<Control>(window, "ReadoutsSeg").IsEffectivelyVisible);

        shell.SelectedTab = ShellViewModel.TuneTab;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Find.Named<Control>(window, "ReadoutsSeg").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void ConnectIsPrimaryOnlyOnLive()
    {
        var (window, shell) = Show();
        var connect = Find.Named<Button>(window, "ConnectButton");

        Assert.Contains("primary", connect.Classes);

        shell.SelectedTab = ShellViewModel.TuneTab;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("primary", connect.Classes);
    }

    [AvaloniaFact]
    public void RateSaysNoSamplesYetThenTheRate()
    {
        var (window, shell) = Show();
        var rate = Find.Named<Control>(window, "RateText");
        Assert.False(rate.IsEffectivelyVisible);

        shell.Live.IsConnected = true;
        shell.Live.ShowRate(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(rate.IsEffectivelyVisible);
        Assert.True(shell.Live.HasNoSamplesYet);

        shell.Live.ShowRate(new RateStats(38.2, 26.2, 24, 31, 100));

        Assert.False(shell.Live.HasNoSamplesYet);
        Assert.Equal("38", shell.Live.RateValue);
        Assert.False(shell.Live.IsSlowLink);
        Assert.Equal("Between samples: 24 to 31 ms", shell.Live.RateDetail);

        shell.Live.ShowRate(new RateStats(8, 125, 110, 140, 100));

        Assert.True(shell.Live.IsSlowLink);
    }

    [AvaloniaFact]
    public void NoBinShowsNoBinOpen()
    {
        var (window, _) = Show();

        Assert.Equal("No bin open", Find.Named<TextBlock>(window, "PlateFile").Text);
        Assert.False(Find.Named<Control>(window, "SaveStateChip").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task ShellScreensForReview()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
        foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
        {
            if (night)
            {
                shell.IsNight = true;
            }
            else
            {
                shell.IsDay = true;
            }

            foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
            {
                window.Width = width;
                window.Height = height;
                Dispatcher.UIThread.RunJobs();
                Screens.Save(window, $"restyle-shell-{theme}-{width}");
            }
        }

        // The crowded case: logging on a slow link with unsaved edits at 1280 px, where the XDF line steps aside.
        shell.IsNight = true;
        Log(shell.Live, logging: true);
        window.Width = 1280;
        window.Height = 800;
        Dispatcher.UIThread.RunJobs();
        Screens.Save(window, "restyle-shell-night-1280-logging");
        Log(shell.Live, logging: false);
    }

    private static bool HasColour(TopLevel window, Color colour)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var pixel = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                var (r, g, b) = buffer.Format == PixelFormat.Rgba8888
                    ? ((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
                    : ((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
                if (Math.Abs(r - colour.R) <= 30 && Math.Abs(g - colour.G) <= 30 && Math.Abs(b - colour.B) <= 30)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
