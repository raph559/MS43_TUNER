using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>A screen in one of its states, opened the way the user reaches it.</summary>
internal sealed record GateState(string Screen, string State, Func<Task<GateWindow?>> Open)
{
    public string Name => $"{Screen}-{State}";

    public override string ToString() => Name;
}

/// <summary>The window a state opened, its shell and its clock.</summary>
internal sealed record GateWindow(Window Window, ShellViewModel Shell, ManualTime Time);

/// <summary>
/// Every screen and state the restyle's gate checks: Tune's item kinds and its first run, Live's
/// states, Compare's views, Settings with and without a save failure, the save prompt, the connection log and the
/// Performance tab's run, results and compare states. States that need the user's car files are left out when the files
/// are missing.
/// </summary>
internal static class GateScreens
{
    private static readonly IReadOnlyList<AdxValueDefinition> Ms43x = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");

    public static IEnumerable<GateState> All()
    {
        foreach (var (state, item) in new[]
                 {
                     ("map", "ip_iga_ron98_pl__n__maf"), ("value", "c_lc_n_max"), ("switch", "c_conf_load"),
                     ("flag", "lv_mil_ind_knk_lv_1"), ("curve", "ip_iga_tco_st__tco"), ("axis", "ldpm_n_14"),
                 })
        {
            yield return new GateState("tune", state, () => Tune(item));
        }

        yield return new GateState("tune", "nobin", () => Shell(shell => shell.SelectedTab = ShellViewModel.TuneTab));

        yield return new GateState("live", "idle", () => Shell(_ => { }));
        yield return new GateState("live", "tiles", () => Shell((shell, time) => Logging(shell, time, dials: false)));
        yield return new GateState("live", "dials", () => Shell((shell, time) => Logging(shell, time, dials: true)));
        yield return new GateState("live", "failed", () => Shell(shell =>
            shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."))));
        yield return new GateState("live", "lost", () => Shell((shell, time) =>
        {
            Logging(shell, time, dials: false);
            time.Advance(TimeSpan.FromSeconds(8));
            shell.Live.ReportLoggingStopped(new TimeoutException("No reply."));
        }));

        yield return new GateState("compare", "map", () => Compare(compare => compare.Selected = Item(compare, "ip_iga_ron98_pl__n__maf")));
        yield return new GateState("compare", "value", () => Compare(compare => compare.Selected = Item(compare, "c_lc_n_max")));
        yield return new GateState("compare", "bytes", () => Compare(compare => compare.ShowBytes()));

        yield return new GateState("settings", "saved", () => Shell(_ => { }, settings: true));
        yield return new GateState("settings", "notsaved", NotSaved);

        yield return new GateState("dialog", "save", () => Shell(shell => _ = shell.Tune.AskToSave!("Save the changes to CAR_current_43X001_SP95_STAGE1.bin?")));
        yield return new GateState("dialog", "log", () => Shell(shell =>
        {
            shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));
            shell.Live.ShowConnectionLog();
        }));

        // The Performance tab: a pull ready to arm, running with cylinder 4 knocking, its result,
        // the saved results, two of them compared, and a pull waiting on a cold engine (the red and amber banners).
        yield return new GateState("performance", "run", () => Performance(shell => Warm(shell)));
        yield return new GateState("performance", "running", () => Performance(shell =>
        {
            Warm(shell);
            shell.Performance.Run.Arm();
            var pull = Pull();
            for (var i = 0; i < pull.Count && shell.Performance.Run.KnockWarning is null; i++)
            {
                shell.Live.ShowSample(pull[i]);
            }
        }));
        yield return new GateState("performance", "result", () => Performance(shell =>
        {
            Warm(shell);
            shell.Performance.Run.Arm();
            PerfFeed.Feed(shell, Pull());
        }));
        yield return new GateState("performance", "results", () => Performance(shell =>
        {
            shell.Performance.IsResultsMode = true;
            shell.Performance.Results.ShowCold = true;
        }));
        yield return new GateState("performance", "compare", () => Performance(shell =>
        {
            var results = shell.Performance.Results;
            shell.Performance.IsResultsMode = true;
            foreach (var row in results.Rows.Where(row => !row.IsCold))
            {
                row.IsPicked = true;
            }

            results.Compare();
        }));
        yield return new GateState("performance", "cold", () => Performance(shell =>
        {
            Connect(shell);
            shell.Performance.Run.Arm();
            PerfFeed.Feed(shell, SyntheticRuns.Idle(PerfFeed.Ms43x, 1, coolantC: 64, oilC: 60));
        }));
    }

    /// <summary>
    /// Closes a state's window. A state's finished run is a picture, not the user's: it is dropped first, or the close would
    /// ask about it and leave the window open, holding the keyboard while the next state is checked.
    /// </summary>
    public static void Close(GateWindow opened)
    {
        opened.Shell.Performance.Run.Discard();
        opened.Window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    public static void Theme(GateWindow opened, bool night)
    {
        opened.Shell.IsNight = night;
        opened.Shell.IsDay = !night;
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

    private static Task<GateWindow?> Shell(Action<ShellViewModel> arrange, bool settings = false, AppSettings? saved = null) =>
        Shell((shell, _) => arrange(shell), settings, saved);

    private static Task<GateWindow?> Shell(Action<ShellViewModel, ManualTime> arrange, bool settings = false, AppSettings? saved = null)
    {
        var time = new ManualTime();
        var (window, shell) = TestShell.Create(saved ?? AppSettings.Load(TestShell.NewFolder()), new MainWindowViewModel(time, () => ["COM1"]));
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

    // The Performance tab with the pedal calibrated and three saved 3rd gear pulls, the earliest made cold.
    private static Task<GateWindow?> Performance(Action<ShellViewModel> arrange)
    {
        var folder = TestShell.NewFolder();
        PerfFeed.SaveResult(folder, "Morning", 6.4, minute: 0, cold: true);
        PerfFeed.SaveResult(folder, "Baseline", 6.2, minute: 1);
        PerfFeed.SaveResult(folder, "Ignition +1.5", 6.0, minute: 2);
        var settings = AppSettings.Load(folder);
        settings.FullPedal = SyntheticRuns.FullPedal;
        return Shell(shell =>
        {
            shell.SelectedTab = ShellViewModel.PerformanceTab;
            Dispatcher.UIThread.RunJobs();
            arrange(shell);
        }, saved: settings);
    }

    // Connected to the ms43x definition, the header showing the logger's rate (over 15 samples/s).
    private static void Connect(ShellViewModel shell)
    {
        PerfFeed.Connect(shell);
        shell.Live.ShowRate(new RateStats(30.2, 33.1, 31, 36, 100));
    }

    // Connected with a warm engine idling, so no banner shows and the ready check can pass.
    private static void Warm(ShellViewModel shell)
    {
        Connect(shell);
        PerfFeed.Feed(shell, SyntheticRuns.Idle(PerfFeed.Ms43x, 1));
    }

    // A 3rd gear pull from 1700 to 6500 rpm in 6 s, cylinder 4 retarding 1.6 degrees from 5000 to 5600 rpm.
    private static IReadOnlyList<LiveSample> Pull() => SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, knockCyl4: 1.6);

    // Logging with the mockups' sample values at the logger's 38 samples/s, connected, in Tiles or Dials.
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
