using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Performance tab as drawn (performance spec 9.1, 9.2): rail, Run and Results, the header's primary button and shortcuts.</summary>
public sealed class PerformanceViewTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private (MainWindow Window, ShellViewModel Shell) Show(double width = 1440, double height = 900, double? fullPedal = SyntheticRuns.FullPedal, string variant = "ms43x")
    {
        var (window, shell) = PerfFeed.Open(_folder, fullPedal, variant);
        window.Width = width;
        window.Height = height;
        window.Show();
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static void Press(Visual root, string name)
    {
        var button = Find.Named<Button>(root, name);
        Assert.True(button.IsEffectivelyVisible, $"{name} is not shown");
        Assert.True(button.IsEffectivelyEnabled, $"{name} is disabled");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Shown(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Control>().Any(control => control.Name == name && control.IsEffectivelyVisible);

    private static IReadOnlyList<LiveSample> ThirdGearPull(double knock = 0) => SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, knockCyl4: knock);

    private void SaveResult(string name, double seconds, int minute, bool cold = false) => PerfFeed.SaveResult(_folder, name, seconds, minute, cold);

    [AvaloniaFact]
    public void PerformanceTabShowsTheView()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Width = 1440;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<PerformanceView>().Single();
        Assert.False(view.IsVisible);

        Find.Named<ToggleButton>(window, "TabPerformance").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.IsPerformance);
        Assert.True(view.IsEffectivelyVisible);
        Assert.False(window.GetVisualDescendants().OfType<LiveView>().Single().IsVisible);
        Assert.False(window.GetVisualDescendants().OfType<TuneView>().Single().IsVisible);
        Assert.False(window.GetVisualDescendants().OfType<CompareView>().Single().IsVisible);
        Assert.True(Shown(window, "PerformanceRail"));
        Assert.True(Find.Named<ToggleButton>(window, "RunModeButton").IsChecked);
        Assert.Equal("Arm pull", Find.Named<Button>(window, "ArmButton").Content);
        // The reason Arm is off is a line in the pane too, not only a tooltip (system 5.9).
        Assert.Contains(view.GetVisualDescendants().OfType<NumberText>(), text => text.Value == "Connect to the car to run a test." && text.IsEffectivelyVisible);
        Screens.Save(window, "performance-run-night-1440");
    }

    [AvaloniaFact]
    public void ConnectIsPrimaryOnPerformanceWhileDisconnected()
    {
        var (window, shell) = Show();
        var connect = Find.Named<Button>(window, "ConnectButton");
        var arm = Find.Named<Button>(window, "ArmButton");

        Assert.Contains("primary", connect.Classes);
        Assert.DoesNotContain("primary", arm.Classes);
        Assert.False(arm.IsEffectivelyEnabled);
        Assert.False(shell.SaveIsPrimary);

        PerfFeed.Connect(shell);
        shell.Live.IsConnected = true;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("primary", connect.Classes);
        Assert.Contains("primary", arm.Classes);
        Assert.True(arm.IsEffectivelyEnabled);
        Assert.False(shell.SaveIsPrimary);

        // Tune and Compare keep Save as their primary.
        shell.SelectedTab = ShellViewModel.CompareTab;
        Assert.True(shell.SaveIsPrimary);
        Assert.False(shell.ConnectIsPrimary);
    }

    // A finished run not saved when the link drops: Save result is the one primary, not Connect too (minor review, findings 20
    // and 33); once saved, Connect is again.
    [AvaloniaFact]
    public void SaveResultIsTheOnePrimaryAfterTheLinkDrops()
    {
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        Dispatcher.UIThread.RunJobs();
        Press(window, "ArmButton");
        PerfFeed.Feed(shell, ThirdGearPull());
        shell.Live.Disconnect();
        Dispatcher.UIThread.RunJobs();

        Assert.False(shell.ConnectIsPrimary);
        var primary = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Classes.Contains("primary"));
        Assert.Equal("SaveResultButton", primary.Name);

        Press(window, "SaveResultButton");
        Assert.True(shell.ConnectIsPrimary);
        Assert.Contains("primary", Find.Named<Button>(window, "ConnectButton").Classes);
    }

    // Ctrl S on Performance saves the result there is to save, never the bin (minor review, finding 31).
    [AvaloniaFact]
    public void CtrlSSavesTheResultOnPerformance()
    {
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        shell.Performance.Run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.Performance.Run.IsSaved);
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
        window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
    }

    // Live's red coolant banner shows at the top of the Performance pane too (minor review, finding 77).
    [AvaloniaFact]
    public void PerformanceShowsTheRedCoolantBanner()
    {
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        PerfFeed.Feed(shell, SyntheticRuns.Idle(PerfFeed.Ms43x, 1, coolantC: 64, oilC: 88));
        Dispatcher.UIThread.RunJobs();

        var view = window.GetVisualDescendants().OfType<PerformanceView>().Single();
        var banner = Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => border.Name == "PerformanceBanner" && border.IsEffectivelyVisible);
        Assert.Equal("banner warn", string.Join(" ", banner.Classes.Where(name => !name.StartsWith(':'))));
        Assert.Equal("Coolant 64 °C: engine not at operating temperature (80 °C). Do not use full throttle; results will not be valid.",
            Assert.Single(banner.GetVisualDescendants().OfType<NumberText>()).Value);
    }

    // The tip on another test names the one that is on, with a way to it (minor review, finding 29).
    [AvaloniaFact]
    public void TheRunningTestIsOneClickAway()
    {
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        Dispatcher.UIThread.RunJobs();
        Press(window, "ArmButton");
        PerfFeed.Select(shell, "Knock check");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Full-throttle pull is armed: cancel it first.", Find.Named<NumberText>(window, "ArmTipText").Value);
        Press(window, "ShowRunningTestButton");

        Assert.Equal("Full-throttle pull", shell.Performance.SelectedEntry.Title);
        Assert.True(Shown(window, "CancelRunButton"));
    }

    // Ctrl K from an opened result keeps what was typed into it (minor review, finding 30).
    [AvaloniaFact]
    public void CtrlKKeepsTheEditsOfAnOpenedResult()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        var (window, shell) = Show();
        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();
        Press(window, "OpenResultButton");
        Find.Named<TextBox>(window, "OpenedNameBox").Text = "Before";

        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();

        Assert.False(shell.Performance.Results.HasOpened);
        Assert.Equal("Before", Assert.Single(shell.Performance.Results.Rows).Name);
    }

    [AvaloniaFact]
    public void LivePerformanceButtonOpensTheTab()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsLive);

        Press(window, "PerformanceButton");

        Assert.True(shell.IsPerformance);
        Assert.True(window.GetVisualDescendants().OfType<PerformanceView>().Single().IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void RunViewShowsChecksThenTheResult()
    {
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(window, "ReadyChecks"));

        Press(window, "ArmButton");

        Assert.True(Shown(window, "ReadyChecks"));
        Assert.False(Shown(window, "ArmButton"));
        Assert.True(Shown(window, "CancelRunButton"));
        // The options of a run that is on cannot change.
        Assert.False(Find.Named<ToggleButton>(window, "Gear2").IsEffectivelyEnabled);

        var samples = ThirdGearPull(knock: 1.6);
        PerfFeed.Feed(shell, samples.Take(150));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(RunStage.Running, shell.Performance.Run.Stage);
        Assert.False(Shown(window, "ReadyChecks"));
        Assert.True(Shown(window, "RunRpm"));
        Assert.Equal(shell.Performance.Run.RpmText, Find.Named<ReadingText>(window, "RunRpm").Value);
        var chart = Find.Named<RunChart>(window, "LiveRunChart");
        Assert.NotEmpty(chart.TraceA!);
        Assert.True(chart.IsRunning);
        Assert.True(chart.IsLive);
        var progress = Find.Named<ReadoutBar>(window, "RunProgress");
        Assert.Equal(2000, progress.Minimum);
        Assert.Equal(6400, progress.Maximum);
        Assert.InRange(progress.FillFraction, 0.01, 0.99);
        Assert.Equal(6, Find.Named<CylinderBars>(window, "KnockBars").Values!.Count);
        Screens.Save(window, "performance-running-night-1440");

        PerfFeed.Feed(shell, samples.Skip(150));
        Dispatcher.UIThread.RunJobs();

        var save = Find.Named<Button>(window, "SaveResultButton");
        Assert.True(save.IsEffectivelyVisible);
        Assert.Contains("primary", save.Classes);
        Assert.Equal("Keep this run in the results (Ctrl S)", ToolTip.GetTip(save));
        Assert.False(Shown(window, "ArmButton"));
        Assert.True(Shown(window, "DiscardResultButton"));
        Assert.True(Shown(window, "RunAgainButton"));
        Assert.Equal("3rd pull 12:00", Find.Named<TextBox>(window, "ResultNameBox").Text);
        // Exactly one primary button on the screen (system 5.2).
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Classes.Contains("primary"));
        Screens.Save(window, "performance-result-night-1440");

        Find.Named<TextBox>(window, "ResultNameBox").Text = "Baseline";
        Press(window, "SaveResultButton");

        Assert.True(shell.Performance.Run.IsSaved);
        Assert.False(save.IsEffectivelyEnabled);
        Assert.Equal("Baseline", Assert.Single(shell.Performance.Results.Rows).Name);
    }

    // A knock check's result shows each cylinder's worst retard as the six bars, in Ink2 since it is stored (spec 6.2, 9.2).
    [AvaloniaFact]
    public void KnockCheckResultShowsTheBars()
    {
        var (window, shell) = Show();
        PerfFeed.Select(shell, "Knock check");
        PerfFeed.Connect(shell);
        Dispatcher.UIThread.RunJobs();
        Press(window, "ArmButton");
        // 1.5° is two of the channel's 0.75° steps.
        PerfFeed.Feed(shell, SyntheticRuns.Pull(PerfFeed.Ms43x, 4, 1300, 4600, 6.0, knockCyl4: 1.5, knockFromRpm: 3000, knockToRpm: 3500));
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.Performance.Run.HasResult);
        var bars = Find.Named<CylinderBars>(window, "ResultKnockBars");
        Assert.True(bars.IsEffectivelyVisible);
        Assert.False(bars.IsLive);
        Assert.Equal(1.5, bars.Values![3]!.Value, 2);
        Assert.Equal(0, bars.Values[0]!.Value);
        // The cylinder is named in words under the bars, and its icon takes the knock flag's amber (minor review, finding 34).
        var line = Find.Named<NumberText>(window, "ResultKnockText");
        Assert.Equal("Knock retard: cylinder 4 at 1.5 °CRK.", line.Value);
        Assert.True(line.IsEffectivelyVisible);
        Assert.Contains(shell.Performance.Run.Flags, flag => flag.Code == "knock" && flag.IsSuspect);
        // The six cylinders are the bars, not six more figures.
        Assert.DoesNotContain(shell.Performance.Run.Figures, figure => figure.Key.StartsWith("knock-max-", StringComparison.Ordinal));
        Screens.Save(window, "performance-knock-result-night-1440");
    }

    [AvaloniaFact]
    public void ResultsCompareShowsTheChart()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        SaveResult("Ignition +1.5", 6.0, minute: 2);
        var (window, shell) = Show();

        Find.Named<ToggleButton>(window, "ResultsModeButton").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Performance.IsResultsMode);
        var picks = window.GetVisualDescendants().OfType<CheckBox>().Where(box => box.Name == "PickBox" && box.IsEffectivelyVisible).ToList();
        Assert.Equal(2, picks.Count);
        Assert.False(Find.Named<Button>(window, "CompareButton").IsEffectivelyEnabled);
        Screens.Save(window, "performance-results-night-1440");

        foreach (var pick in picks)
        {
            pick.IsChecked = true;
        }

        Dispatcher.UIThread.RunJobs();
        Press(window, "CompareButton");

        var chart = Find.Named<RunChart>(window, "CompareChart");
        Assert.True(chart.IsEffectivelyVisible);
        Assert.NotEmpty(chart.TraceA!);
        Assert.NotEmpty(chart.TraceB!);
        Assert.Equal("A: Baseline", chart.LabelA);
        Assert.Equal("kW", chart.YUnits);
        Assert.True(Find.Named<ToggleButton>(window, "MetricPower").IsChecked);
        // The analysis' own name for MAF (minor review, finding 36).
        Assert.Equal("Airflow", Find.Named<ToggleButton>(window, "MetricMaf").Content);
        Assert.Equal("Engine speed over time", Find.Named<ToggleButton>(window, "MetricRpm").Content);
        Assert.False(Shown(window, "ResultsList"));
        Screens.Save(window, "performance-compare-night-1440");

        Find.Named<ToggleButton>(window, "MetricRpm").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("s", chart.XUnits);
        Assert.False(Find.Named<ToggleButton>(window, "MetricPower").IsChecked);

        Press(window, "CloseComparisonButton");
        Assert.True(Shown(window, "ResultsList"));
    }

    // The difference panel shows each run's flags (final review, finding 5).
    [AvaloniaFact]
    public void CompareShowsAColdRunsFlag()
    {
        SaveResult("Warm", 6.0, minute: 1);
        SaveResult("Morning", 6.4, minute: 2, cold: true);
        var (window, shell) = Show();
        var results = shell.Performance.Results;
        shell.Performance.IsResultsMode = true;
        results.ShowCold = true;
        foreach (var row in results.Rows)
        {
            row.IsPicked = true;
        }

        results.Compare();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Shown(window, "FlagsA"));
        Assert.True(Shown(window, "FlagsB"));
        Assert.Contains(Find.Named<ItemsControl>(window, "FlagsB").GetVisualDescendants().OfType<NumberText>(),
            text => text.Value == "Engine cold: not comparable." && text.IsEffectivelyVisible);
        Screens.Save(window, "performance-compare-cold-night-1440");
    }

    [AvaloniaFact]
    public void ResultsSayWhenThereAreNone()
    {
        var (window, shell) = Show(1024, 680);
        Find.Named<ToggleButton>(window, "ResultsModeButton").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("No saved results yet.", Find.Named<NumberText>(window, "ResultsEmptyText").Value);
        Assert.True(Shown(window, "ResultsEmptyText"));
        Assert.False(Find.Named<Button>(window, "CompareButton").IsEffectivelyEnabled);
        Screens.Save(window, "performance-results-empty-night-1024");
    }

    // One unreadable file is named with why, and the others still list (review focus 3).
    [AvaloniaFact]
    public void ABadFileIsNamedAndTheOthersStillList()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        File.WriteAllText(Path.Combine(_folder, "results", "perf-20261021-110000.json"), "{");
        var (window, shell) = Show();
        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Single(shell.Performance.Results.Rows);
        // A file name is UI text, not numbers and ids (system 3.1; minor review, finding 35).
        Assert.Contains(Find.Named<ItemsControl>(window, "SkippedFiles").GetVisualDescendants().OfType<TextBlock>(),
            text => text is not NumberText && text.Text == "perf-20261021-110000.json: not a result file" && text.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void OpenAndRenameShowTheStoredResult()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        var (window, shell) = Show();
        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();

        Press(window, "RenameButton");

        Assert.True(shell.Performance.Results.HasOpened);
        var name = Find.Named<TextBox>(window, "OpenedNameBox");
        Assert.True(name.IsFocused);
        // Titled with its name; Save changes waits for a change (minor review, finding 46).
        Assert.Equal("Baseline", Find.Named<TextBlock>(window, "OpenedTitle").Text);
        var saveDetails = Find.Named<Button>(window, "SaveDetailsButton");
        Assert.False(saveDetails.IsEffectivelyEnabled);
        Assert.Equal("Nothing to save: the name, description and direction are as saved.", ToolTip.GetTip(saveDetails));
        name.Text = "Before";
        Dispatcher.UIThread.RunJobs();
        Press(window, "SaveDetailsButton");
        Assert.False(saveDetails.IsEffectivelyEnabled);
        Assert.Equal("Before", Find.Named<TextBlock>(window, "OpenedTitle").Text);
        Press(window, "CloseOpenedButton");

        Assert.Equal("Before", Assert.Single(shell.Performance.Results.Rows).Name);
        Press(window, "OpenResultButton");
        Assert.Contains(window.GetVisualDescendants().OfType<ReadingText>(), text => text.IsEffectivelyVisible && text.Unit == "s");
    }

    [AvaloniaFact]
    public void DeleteAsksThroughTheConfirmDialog()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        var (window, shell) = Show();
        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();

        Press(window, "DeleteResultButton");
        var dialog = window.GetVisualDescendants().OfType<ConfirmActionDialog>().Single();
        Assert.Equal("Delete the result \"Baseline\"?", Find.Named<TextBlock>(dialog, "MessageText").Text);
        Press(dialog, "ConfirmActionButton");

        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "results")));
        Assert.Empty(shell.Performance.Results.Rows);
    }

    [AvaloniaFact]
    public void RailRowsChooseTheTestAndSayWhyOneIsOff()
    {
        var (window, shell) = Show(variant: "b25");
        var list = Find.Named<ListBox>(window, "PerformanceTests");
        var knock = shell.Performance.Entries.OfType<PerformanceTestEntry>().Single(entry => entry.Title == "Knock check");

        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "This definition does not log knock per cylinder." && text.IsEffectivelyVisible);
        list.SelectedItem = knock;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(knock, shell.Performance.SelectedEntry);
        Assert.Equal("Arm pull", Find.Named<Button>(window, "ArmButton").Content);
        Assert.False(Find.Named<ToggleButton>(window, "Gear3").IsEffectivelyVisible);
        Assert.True(Find.Named<ToggleButton>(window, "Gear4").IsEffectivelyVisible);
    }

    // Tests chosen before the list has followed (the tab not in front) settle on the last one, instead of the rail and the
    // pane posting their choices to each other for ever.
    [AvaloniaFact]
    public void ChoosingTestsSettles()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Show();
        var changes = 0;
        shell.Performance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PerformanceViewModel.SelectedEntry) && ++changes > 50)
            {
                throw new InvalidOperationException("The rail and the pane take turns choosing the test.");
            }
        };

        PerfFeed.Select(shell, "Pedal");
        PerfFeed.Select(shell, "Car");
        Dispatcher.UIThread.RunJobs();
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Car", shell.Performance.SelectedEntry.Title);
        Assert.Same(shell.Performance.SelectedEntry, Find.Named<ListBox>(window, "PerformanceTests").SelectedItem);
    }

    [AvaloniaFact]
    public void SetupPanesShowTheirFields()
    {
        var (window, shell) = Show();
        PerfFeed.Select(shell, "Car");
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(window, "ArmButton"));
        Assert.Equal("1480", Find.Named<TextBox>(window, "MassBox").Text);
        Assert.Equal("205/55 R16", Find.Named<TextBox>(window, "TyreBox").Text);
        Assert.True(Find.Named<ToggleButton>(window, "GearboxManual").IsChecked);
        Find.Named<ToggleButton>(window, "GearboxAutomatic").IsChecked = true;
        Assert.True(shell.Tune.Settings.Automatic);

        PerfFeed.Select(shell, "Speed calibration");
        PerfFeed.Connect(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(window, "GpsSpeedBox"));
        Press(window, "ArmButton");
        PerfFeed.Feed(shell, PerfFeed.Steady(4, 3000, 5, values: [("VS", 100), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(window, "GpsSpeedBox"));
        Assert.True(Shown(window, "GpsVsBox"));
        Assert.Contains("primary", Find.Named<Button>(window, "SaveCalibrationButton").Classes);
        Press(window, "SaveCalibrationButton");
        Assert.Equal(100.0 / 3000, shell.Tune.Settings.KmhPerRpm[5], 6);
    }

    [AvaloniaFact]
    public void CtrlBTogglesTheRailOnPerformance()
    {
        var (window, shell) = Show();
        Assert.False(shell.Performance.RailCollapsed);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Performance.RailCollapsed);
        Assert.True(Shown(window, "PerformanceExpandButton"));
        Assert.False(Shown(window, "PerformanceTests"));

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Performance.RailCollapsed);
        Assert.True(Shown(window, "PerformanceCollapseButton"));

        // Ctrl K opens the results on their search (performance spec 9.1).
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Performance.IsResultsMode);
        Assert.True(Find.Named<TextBox>(window, "ResultsSearchBox").IsFocused);

        // Ctrl Z belongs to Tune and Live: on Performance it changes nothing.
        var undone = false;
        shell.Tune.Undos.Push(new UndoStep("Set", () => undone = true, () => { }));
        window.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Dispatcher.UIThread.RunJobs();
        Assert.False(undone);
    }

    [AvaloniaFact]
    public void CompactHeaderFitsTheFourthTab()
    {
        var (window, shell) = Show(1024, 680);
        shell.Live.IsConnected = true;
        shell.Live.ShowRate(new RateStats(8, 125, 110, 140, 100));
        Dispatcher.UIThread.RunJobs();

        // The words "slow link" give way under the compact header; the rate and its icon stay (performance spec 9.1).
        Assert.False(Shown(window, "SlowLinkText"));
        Assert.True(shell.Live.IsSlowLink);
        Assert.Equal(new Thickness(8, 0), Find.Named<ToggleButton>(window, "TabPerformance").Padding);

        window.Width = 1440;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Shown(window, "SlowLinkText"));
        Assert.Equal(new Thickness(12, 0), Find.Named<ToggleButton>(window, "TabPerformance").Padding);
    }

    // Every state of the tab at both sizes and in both themes, for review; nothing may run past the window's right edge
    // (system 8: no horizontal page scroll, nothing clipped).
    [AvaloniaFact]
    public void PerformanceScreensForReview()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        SaveResult("Ignition +1.5", 6.0, minute: 2);
        SaveResult("Morning", 6.4, minute: 0, cold: true);
        File.WriteAllText(Path.Combine(_folder, "results", "perf-20261021-110000.json"), "{");
        var (window, shell) = Show();
        var run = shell.Performance.Run;
        var results = shell.Performance.Results;
        // The logger's rate, as the header shows it while connected.
        void Rate() => shell.Live.ShowRate(new RateStats(30.2, 33.1, 31, 36, 100));
        var samples = ThirdGearPull(knock: 1.6);
        var problems = new List<string>();

        var states = new (string Name, Action Arrange)[]
        {
            ("idle", () => { }),
            ("waiting", () =>
            {
                PerfFeed.Connect(shell);
                Rate();
                PerfFeed.Feed(shell, SyntheticRuns.Idle(PerfFeed.Ms43x, 1, coolantC: 92, oilC: 60));
                run.Arm();
                PerfFeed.Feed(shell, samples.Take(10));
            }),
            ("running", () => PerfFeed.Feed(shell, samples.Skip(10).Take(140))),
            ("result", () => PerfFeed.Feed(shell, samples.Skip(150))),
            ("car", () => PerfFeed.Select(shell, "Car")),
            ("calibration", () => PerfFeed.Select(shell, "Speed calibration")),
            ("capturing", () =>
            {
                run.Arm();
                PerfFeed.Feed(shell, PerfFeed.Steady(4, 3000, 5, from: 20, values: [("VS", 100), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));
                run.GpsKmh = "97";
            }),
            ("pedal", () =>
            {
                run.Cancel();
                PerfFeed.Select(shell, "Pedal");
                run.Arm();
                PerfFeed.Feed(shell, PerfFeed.Steady(1, 0, 0, from: 30, values: [("PVS", 96), ("TCO", 92), ("TOIL", 88)]));
            }),
            ("link", () =>
            {
                run.Cancel();
                PerfFeed.Select(shell, "Link test");
                run.Arm();
                PerfFeed.Feed(shell, PerfFeed.Steady(12, 750, 0, from: 40, values: [("TCO", 92), ("TOIL", 88)]));
            }),
            ("results", () =>
            {
                shell.Performance.IsResultsMode = true;
                results.ShowCold = true;
            }),
            ("compare", () =>
            {
                foreach (var row in results.Rows.Where(row => !row.IsCold))
                {
                    row.IsPicked = true;
                }

                results.Compare();
                Assert.NotNull(results.Comparison);
            }),
            ("opened", () =>
            {
                results.CloseComparison();
                results.Open(results.Rows[0]);
            }),
        };

        foreach (var (name, arrange) in states)
        {
            arrange();
            foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
            {
                shell.IsNight = night;
                shell.IsDay = !night;
                foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    Screens.Save(window, $"performance-state-{name}-{theme}-{width}");
                    problems.AddRange(window.GetVisualDescendants().OfType<Control>()
                        .Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0)
                        .Where(control => control.TranslatePoint(new Point(control.Bounds.Width, 0), window) is { } right && right.X > window.ClientSize.Width + 0.5)
                        .Select(control => $"{name} {theme} {width}: {control.Name ?? control.GetType().Name} runs past the window"));
                }
            }
        }

        shell.IsNight = true;
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(30)));
    }

    // The warm-up banners stay at the top of the pane, out of the scrolling part, on Run and on Results (minor review,
    // finding 32).
    [AvaloniaFact]
    public void WarmUpBannersStayInViewOnRunAndResults()
    {
        var (window, shell) = Show(1024, 680);
        PerfFeed.Connect(shell);
        PerfFeed.Feed(shell, SyntheticRuns.Idle(PerfFeed.Ms43x, 1, coolantC: 64, oilC: 88));
        Dispatcher.UIThread.RunJobs();

        var banner = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border => border.Name == "PerformanceBanner" && border.IsEffectivelyVisible);
        Assert.DoesNotContain(banner.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(banner.IsEffectivelyVisible);
    }

    // Numbers in Bahnschrift, the firmware id in Cascadia, file names in plain UI text (system 3.1; minor review, finding 35).
    [AvaloniaFact]
    public void NumbersIdsAndFileNamesTakeTheirFonts()
    {
        var tune = new TuneIdentity("CAR_2024.bin", null, "43X001", null, "MS43X001 v1.0", null, true, ["a", "b", "c"], "00", "00", null, null, "");
        SaveResult("Baseline", 6.2, minute: 1);
        PerfFeed.SaveResult(_folder, "Tuned", 6.0, minute: 2, tune: tune);
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        shell.Performance.Run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull().Take(10));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Find.Named<ItemsControl>(window, "ReadyChecks").GetVisualDescendants().OfType<NumberText>(),
            text => text.Value == "Between 1300 and 1800 rpm" && text.IsEffectivelyVisible);
        Assert.Contains(Find.Named<ListBox>(window, "PerformanceTests").GetVisualDescendants().OfType<NumberText>(),
            text => text.Value == "4th gear, 1500 to 4500 rpm, knock per cylinder" && text.IsEffectivelyVisible);

        shell.Performance.IsResultsMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Find.Named<ItemsControl>(window, "ResultsList").GetVisualDescendants().OfType<TextBlock>(),
            text => text is not NumberText && text.Text == "CAR_2024.bin" && text.IsEffectivelyVisible);

        shell.Performance.Results.Open(shell.Performance.Results.Rows.Single(row => row.Name == "Tuned"));
        Dispatcher.UIThread.RunJobs();
        var line = Find.Named<TuneLineText>(window, "OpenedTuneLine");
        var runs = line.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Equal("Tune: CAR_2024.bin · 43X001 · unsaved edits in 3 items", string.Concat(runs.Select(run => run.Text)));
        Assert.Empty(Assert.Single(runs, run => run.Text == "CAR_2024.bin").Classes);
        Assert.Contains("id", Assert.Single(runs, run => run.Text == "43X001").Classes);
        Assert.Contains("num", Assert.Single(runs, run => run.Text == "3").Classes);
    }

    // The action bar comes after what it acts on in tab order, so Tab from the last field reaches Save (minor review,
    // finding 39).
    [AvaloniaFact]
    public void TheActionBarComesLastInTabOrder()
    {
        SaveResult("Baseline", 6.2, minute: 1);
        var (window, shell) = Show();
        PerfFeed.Connect(shell);
        shell.Performance.Run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Find.Named<Button>(window, "SaveResultButton"),
            KeyboardNavigationHandler.GetNext(Find.Named<ToggleButton>(window, "DirectionB"), NavigationDirection.Next));

        shell.Performance.IsResultsMode = true;
        shell.Performance.Results.Open(shell.Performance.Results.Rows.Single(row => row.Name == "Baseline"));
        shell.Performance.Results.Opened!.Description = "SP95";
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Find.Named<Button>(window, "SaveDetailsButton"),
            KeyboardNavigationHandler.GetNext(Find.Named<ToggleButton>(window, "OpenedDirectionB"), NavigationDirection.Next));
    }

    // A group heading on the rail is a heading: no focus, no hover, no press (minor review, finding 40).
    [AvaloniaFact]
    public void GroupHeadingsAreNotRows()
    {
        var (window, _) = Show();

        var items = Find.Named<ListBox>(window, "PerformanceTests").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        var headings = items.Where(item => item.DataContext is PerformanceGroup).ToList();
        Assert.Equal(3, headings.Count);
        Assert.All(headings, item => Assert.False(item.Focusable || item.IsHitTestVisible));
        Assert.All(items.Where(item => item.DataContext is PerformanceTestEntry), item => Assert.True(item.Focusable && item.IsHitTestVisible));
    }
}
