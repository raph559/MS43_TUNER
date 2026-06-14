using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>A screen in one of its states, opened the way the owner reaches it.</summary>
internal sealed record GateState(string Screen, string State, Func<Task<GateWindow?>> Open)
{
    public string Name => $"{Screen}-{State}";

    public override string ToString() => Name;
}

/// <summary>The window a state opened, its shell (null for the Performance tests window) and its clock.</summary>
internal sealed record GateWindow(Window Window, ShellViewModel? Shell, ManualTime Time);

/// <summary>
/// Every screen and state the restyle's gate checks (plan Task 15): Tune's item kinds and its first run, Live's
/// states, Compare's views, Settings with and without a save failure, the save prompt, the connection log and the
/// Performance tests window. States that need the owner's car files are left out when the files are missing.
/// </summary>
internal static class GateScreens
{
    private static readonly IReadOnlyList<AdxValueDefinition> Ms43x = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");

    public static void Theme(GateWindow opened, bool night)
    {
        if (opened.Shell is { } shell)
        {
            shell.IsNight = night;
            shell.IsDay = !night;
        }
        else
        {
            Avalonia.Application.Current!.RequestedThemeVariant = night ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<GateWindow?> Tune(string item)
    {
        if (!TuneScreen.Available)
        {
            return null;
        }

        var time = new ManualTime();
        var (window, shell) = await TuneScreen.OpenAsync(item, TuneScreen.CopyCar(TestShell.NewFolder()), time);
        return Sized(new GateWindow(window, shell, time));
    }

    private static async Task<GateWindow?> Compare(Action<CompareViewModel> select)
    {
        if (!TuneScreen.Available || Release is null)
        {
            return null;
        }

        var time = new ManualTime();
        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: TuneScreen.CopyCar(TestShell.NewFolder()), time: time);
        shell.SelectedTab = ShellViewModel.CompareTab;
        shell.Compare.OpenB(Release);
        Dispatcher.UIThread.RunJobs();
        select(shell.Compare);
        return Sized(new GateWindow(window, shell, time));
    }

    private static Task<GateWindow?> Shell(Action<ShellViewModel> arrange, bool settings = false) => Shell((shell, _) => arrange(shell), settings);

    private static Task<GateWindow?> Shell(Action<ShellViewModel, ManualTime> arrange, bool settings = false)
    {
        var time = new ManualTime();
        var (window, shell) = TestShell.Create(AppSettings.Load(TestShell.NewFolder()), new MainWindowViewModel(time, () => ["COM1"]));
        shell.Live.SelectedVariant = "ms43x";
        window.Show();
        var opened = Sized(new GateWindow(window, shell, time));
        arrange(shell, time);
        if (settings)
        {
            window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.Control);
        }

        Dispatcher.UIThread.RunJobs();
        return Task.FromResult<GateWindow?>(opened);
    }

    // Settings whose file cannot be written: the folder's parent is a file.
    private static Task<GateWindow?> NotSaved()
    {
        var folder = TestShell.NewFolder();
        Directory.CreateDirectory(folder);
        var blocker = Path.Combine(folder, "blocker");
        File.WriteAllText(blocker, string.Empty);
        var time = new ManualTime();
        var (window, shell) = TestShell.Create(AppSettings.Load(Path.Combine(blocker, "settings")), new MainWindowViewModel(time, () => ["COM1"]));
        window.Show();
        var opened = Sized(new GateWindow(window, shell, time));
        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Find.Named<ToggleButton>(window.GetVisualDescendants().OfType<SettingsView>().Single(), "Trend30").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        return Task.FromResult<GateWindow?>(opened);
    }

    private static Task<GateWindow?> Performance()
    {
        var time = new ManualTime();
        var live = new MainWindowViewModel(time, () => ["COM1"]) { SelectedVariant = "ms43x" };
        var window = new PerformanceWindow { DataContext = live };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        live.ArmPerformanceTest();
        Speed(live, 0);
        time.Advance(TimeSpan.FromSeconds(0.5));
        Speed(live, 2);
        time.Advance(TimeSpan.FromSeconds(7.3));
        Speed(live, 100);
        Dispatcher.UIThread.RunJobs();
        return Task.FromResult<GateWindow?>(new GateWindow(window, null, time));
    }

    // Logging with the mockups' sample values (system 11.1) at the logger's 38 samples/s, connected, in Tiles or Dials.
    private static void Logging(ShellViewModel shell, ManualTime time, bool dials)
    {
        var live = shell.Live;
        shell.IsDials = dials;
        shell.IsTiles = !dials;
        live.IsConnected = true;
        live.BeginSession();
        for (var i = 0; i < 380; i++)
        {
            Sample(live, ("N", 2480 + 12 * Math.Sin(i / 9.0)), ("MAFIGA", 112.4 + Math.Sin(i / 7.0)), ("IGAAVG", 31.1), ("GEAR", 4), ("VS", 77),
                ("AMP", 101.2), ("TCO", 88.5), ("MAFTI", 118.0), ("TIA", 31.0), ("MAP", 31.5 + Math.Sin(i / 5.0)), ("TOIL", 96.0), ("TPS", 12.4), ("VB", 14.12));
            time.Advance(TimeSpan.FromSeconds(1.0 / 38));
        }

        live.ShowRate(new RateStats(38.2, 26.2, 24, 31, 100));
    }

    private static void Sample(MainWindowViewModel live, params (string Id, double Value)[] values) =>
        live.ShowSample(values.Select(value => new DecodedValue(Ms43x.Single(channel => channel.Id == value.Id), value.Value)).ToList());

    private static void Speed(MainWindowViewModel live, double kmh) => Sample(live, ("VS", kmh));

    private static ItemDifference Item(CompareViewModel compare, string title) =>
        compare.Result!.Items.Single(item => item.Item.Title == title);

    private static GateWindow Sized(GateWindow opened)
    {
        opened.Window.Width = 1024;
        opened.Window.Height = 680;
        Dispatcher.UIThread.RunJobs();
        return opened;
    }
}
