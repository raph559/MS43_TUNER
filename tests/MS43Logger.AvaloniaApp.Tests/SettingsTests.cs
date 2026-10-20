using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-settings-ui-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private (MainWindow Window, ShellViewModel Shell) Show(AppSettings? settings = null)
    {
        var (window, shell) = TestShell.Create(settings ?? AppSettings.Load(_folder));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static DialogHost Host(MainWindow window) => Find.Named<DialogHost>(window, "Dialogs");

    private static SettingsView OpenSettings(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<SettingsView>().Single();
    }

    private static T Control<T>(SettingsView view, string name) where T : Control => Find.Named<T>(view, name);

    [AvaloniaFact]
    public async Task SettingsScreensForReview()
    {
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, string.Empty);
        foreach (var (name, failing) in new[] { ("ok", false), ("notsaved", true) })
        {
            var (window, shell) = Show(AppSettings.Load(failing ? Path.Combine(blocker, "settings") : _folder));
            if (TuneScreen.Available)
            {
                shell.SelectedTab = ShellViewModel.TuneTab;
                await shell.Tune.OpenAsync(TuneScreen.CopyCar(Path.Combine(_folder, name)), TuneScreen.Xdf);
                shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf");
            }

            var view = OpenSettings(window);
            if (failing)
            {
                Control<ToggleButton>(view, "Trend30").IsChecked = true;
            }

            foreach (var theme in new[] { "night", "day" })
            {
                if (theme == "night")
                {
                    shell.IsNight = true;
                }
                else
                {
                    shell.IsDay = true;
                }

                foreach (var (width, height) in failing ? new[] { (1440, 900) } : new[] { (1440, 900), (1024, 680) })
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    Screens.Save(window, $"restyle-settings-{theme}-{width}{(failing ? "-notsaved" : string.Empty)}");
                }
            }

            shell.IsNight = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CtrlCommaOpensSettingsAndEscCloses()
    {
        var (window, _) = Show();

        OpenSettings(window);
        Assert.True(Host(window).IsOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Host(window).IsOpen);
        Assert.Empty(window.GetVisualDescendants().OfType<SettingsView>());
    }

    [AvaloniaFact]
    public void ScrollbarShowsWhenTheBodyScrolls()
    {
        var (window, _) = Show();
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();

        var view = OpenSettings(window);

        var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 1, "the body should scroll at 1024 x 680");
        var bar = scroller.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .Single(scrollBar => scrollBar.TemplatedParent == scroller && scrollBar.Orientation == Avalonia.Layout.Orientation.Vertical);
        var thumb = bar.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single(part => part.TemplatedParent == bar);

        // What the owner sees: the drawn thumb against the dialog surface (its padding, right of the bar), 3:1 like any
        // non-text cue.
        using var frame = Screens.Capture(window)!;
        var drawn = Pixel(frame, thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value);
        var surface = Pixel(frame, bar.TranslatePoint(new Point(bar.Bounds.Width + 3, bar.Bounds.Height / 2), window)!.Value);
        Assert.True(Contrast(drawn, surface) >= 3, $"the thumb ({drawn}) is {Contrast(drawn, surface):0.00}:1 against the dialog ({surface})");
    }

    private static Color Pixel(Avalonia.Media.Imaging.WriteableBitmap frame, Point at)
    {
        using var buffer = frame.Lock();
        var pixel = System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address, (int)at.Y * buffer.RowBytes + (int)at.X * 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }

    /// <summary>WCAG 2 contrast ratio.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [AvaloniaFact]
    public void KeyboardOpenShowsTheFocusRing()
    {
        var (window, _) = Show();

        OpenSettings(window);

        var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        Assert.Contains(":focus-visible", focused.Classes);
    }

    [AvaloniaFact]
    public void TabGoesFromTheLogFolderFieldToBrowse()
    {
        var (window, _) = Show();
        var view = OpenSettings(window);
        Control<TextBox>(view, "LogFolderBox").Focus(NavigationMethod.Tab);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Control<Button>(view, "LogFolderBrowse"), window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void GearOpensSettings()
    {
        var (window, _) = Show();

        Find.Named<Button>(window, "SettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(Host(window).IsOpen);
        Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
    }

    [AvaloniaFact]
    public void ThemeChoicesApplyAtOnce()
    {
        var (window, _) = Show();
        var view = OpenSettings(window);
        Color Pane() => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "PaneBrush")!).Color;

        Control<ToggleButton>(view, "ThemeDay").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Color.Parse("#FFFFFF"), Pane());
        Assert.Equal(ThemeMode.Day, AppSettings.Load(_folder).Theme);

        Control<ToggleButton>(view, "ThemeFollow").IsChecked = true;

        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        Assert.Equal(ThemeMode.FollowWindows, AppSettings.Load(_folder).Theme);
    }

    [AvaloniaFact]
    public void StatusBarAndDialogStayInSync()
    {
        var (window, shell) = Show();
        var view = OpenSettings(window);
        Control<ToggleButton>(view, "ThemeFollow").IsChecked = true;

        shell.IsDay = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ThemeMode.Day, AppSettings.Load(_folder).Theme);
        Assert.True(Control<ToggleButton>(view, "ThemeDay").IsChecked);
        Assert.False(Control<ToggleButton>(view, "ThemeFollow").IsChecked);

        Control<ToggleButton>(view, "SettingsDials").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.IsDials);
        Assert.True(Find.Named<ToggleButton>(window, "ReadoutsDials").IsChecked);
    }

    [AvaloniaFact]
    public void EverySettingIsSaved()
    {
        var (window, _) = Show();
        var view = OpenSettings(window);

        Control<ToggleButton>(view, "SettingsDials").IsChecked = true;
        Control<ToggleButton>(view, "Trend60").IsChecked = true;
        Control<TextBox>(view, "LogFolderBox").Focus();
        Control<TextBox>(view, "LogFolderBox").Text = @"D:\logs";
        Control<Button>(view, "LogFolderBrowse").Focus();
        Control<ToggleSwitch>(view, "StartCsvSwitch").IsChecked = true;
        Control<ToggleButton>(view, "MapsRaw").IsChecked = true;
        Control<ToggleButton>(view, "TrailLong").IsChecked = true;
        Control<ToggleSwitch>(view, "ReopenSwitch").IsChecked = false;
        Control<ToggleSwitch>(view, "BakSwitch").IsChecked = false;
        Control<ToggleSwitch>(view, "EditLogSwitch").IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        var saved = AppSettings.Load(_folder);
        Assert.Equal(ReadoutsMode.Dials, saved.Readouts);
        Assert.Equal(60, saved.TrendSeconds);
        Assert.Equal(@"D:\logs", saved.LogFolder);
        Assert.True(saved.StartCsvOnConnect);
        Assert.True(saved.OpenMapsRaw);
        Assert.Equal(TrailMode.Long, saved.CursorTrail);
        Assert.False(saved.ReopenLastBin);
        Assert.False(saved.KeepBackup);
        Assert.False(saved.AppendEditLog);
    }

    [AvaloniaFact]
    public void LogFolderIsSavedWhenTheFieldIsLeft()
    {
        var (window, _) = Show();
        var view = OpenSettings(window);
        var box = Control<TextBox>(view, "LogFolderBox");
        box.Focus();

        // A path half typed is not a setting yet (checkpoint: partial paths were saved per keystroke).
        box.Text = @"D:\l";
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(@"D:\l", AppSettings.Load(_folder).LogFolder);

        box.Text = @"D:\logs";
        Control<Button>(view, "LogFolderBrowse").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"D:\logs", AppSettings.Load(_folder).LogFolder);
    }

    [AvaloniaFact]
    public void EmptiedLogFolderShowsTheSavedFolderAgain()
    {
        var (window, _) = Show();
        var view = OpenSettings(window);
        var box = Control<TextBox>(view, "LogFolderBox");
        var saved = box.Text;
        box.Focus();

        // Checkpoint M9: an emptied box must not pretend the folder is gone; the saved folder stays and shows again.
        box.Text = string.Empty;
        Control<Button>(view, "LogFolderBrowse").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(saved, box.Text);
        Assert.Equal(saved, AppSettings.Load(_folder).LogFolder);
    }

    [AvaloniaFact]
    public void SaveFailureShowsBannerAndTryAgain()
    {
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, string.Empty);
        var (window, _) = Show(AppSettings.Load(Path.Combine(blocker, "settings")));
        var view = OpenSettings(window);

        Control<ToggleButton>(view, "Trend30").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var banner = Control<Border>(view, "SaveErrorBanner");
        Assert.True(banner.IsEffectivelyVisible);
        Assert.StartsWith("Not saved:", Control<TextBlock>(view, "SaveErrorText").Text);
        Assert.EndsWith("Your choices apply until you close the app.", Control<TextBlock>(view, "SaveErrorText").Text);

        File.Delete(blocker);
        Control<Button>(view, "TryAgainButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(banner.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task ReopensLastBinAtStart()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var settings = AppSettings.Load(_folder);
        settings.LastBin = TuneScreen.CopyCar(_folder);
        settings.XdfByFirmware["43X001"] = TuneScreen.Xdf!;
        var (_, shell) = Show(settings);

        await shell.StartAsync();

        Assert.NotNull(shell.Tune.Document);
    }

    [AvaloniaFact]
    public async Task MissingLastBinShowsBanner()
    {
        var settings = AppSettings.Load(_folder);
        settings.LastBin = Path.Combine(_folder, "gone", "CAR.bin");
        var (_, shell) = Show(settings);

        await shell.StartAsync();

        Assert.Null(shell.Tune.Document);
        Assert.Equal("Could not reopen CAR.bin: the file was not found.", shell.Tune.ErrorMessage);
        Assert.Equal(TuneAction.OpenBin, shell.Tune.ErrorAction);
    }

    [AvaloniaFact]
    public async Task MissingXdfShowsBannerAndNoPicker()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var settings = AppSettings.Load(_folder);
        settings.LastBin = TuneScreen.CopyCar(_folder);
        settings.XdfByFirmware["43X001"] = Path.Combine(_folder, "moved", "Siemens_MS43_MS43X001_512K.xdf");
        var (_, shell) = Show(settings);

        await shell.StartAsync();

        Assert.Null(shell.Tune.Document);
        Assert.Equal("Could not reopen CAR.bin: its XDF Siemens_MS43_MS43X001_512K.xdf was not found.", shell.Tune.ErrorMessage);
        Assert.Equal(TuneAction.OpenXdf, shell.Tune.ErrorAction);
    }

    [AvaloniaFact]
    public async Task ReopenOffStartsEmpty()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var settings = AppSettings.Load(_folder);
        settings.LastBin = TuneScreen.CopyCar(_folder);
        settings.XdfByFirmware["43X001"] = TuneScreen.Xdf!;
        settings.ReopenLastBin = false;
        var (_, shell) = Show(settings);

        await shell.StartAsync();

        Assert.Null(shell.Tune.Document);
        Assert.Null(shell.Tune.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task SaveWithoutBakOrLogWhenSwitchedOff()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var settings = AppSettings.Load(_folder);
        settings.KeepBackup = false;
        settings.AppendEditLog = false;
        var (_, shell) = Show(settings);
        var bin = TuneScreen.CopyCar(Path.Combine(_folder, "bins"));
        await shell.Tune.OpenAsync(bin, TuneScreen.Xdf);
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf");
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));

        Assert.True(shell.Tune.Save());

        Assert.False(File.Exists(bin + ".bak"));
        Assert.False(File.Exists(Path.ChangeExtension(bin, ".log")));
    }
}
