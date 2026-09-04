using System.Text;
using Avalonia.Headless.XUnit;
using MS43Logger.Core;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>A clock the tests move whose local time is UTC, so a result made at 12:00 UTC is named "… 12:00" on any machine.</summary>
internal sealed class UtcTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Samples fed to the Performance tab the way Live receives them: through ShowSample, so SampleShown fires.</summary>
internal static class PerfFeed
{
    public static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    /// <summary>The car values <see cref="SaveResult"/> makes results with: 1480 kg, 205/55 R16, manual, no speed calibration.</summary>
    public static readonly AnalysisInputs Inputs = new(new CarValues(1480, "205/55 R16", false), new Dictionary<int, double>(), SyntheticRuns.FullPedal);

    /// <summary>
    /// A 3rd gear pull made with the Core run and saved in <paramref name="folder"/>'s results, as made at 12:<paramref name="minute"/>
    /// UTC; <paramref name="cold"/> marks it not comparable; <paramref name="tune"/> is the tune it was made with, none by default.
    /// </summary>
    public static void SaveResult(string folder, string name, double seconds, int minute, bool cold = false, TuneIdentity? tune = null)
    {
        var warmUp = new WarmUpMonitor();
        var run = new PerfRun(PerfTestSpec.Pull(3), Inputs, warmUp);
        foreach (var sample in SyntheticRuns.Pull(Ms43x, 3, 1700, 6500, seconds))
        {
            var frame = PerfChannels.Read(Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt);
            warmUp.Push(frame);
            run.Push(frame, sample.Payload);
        }

        var outcome = run.Outcome!;
        if (cold)
        {
            outcome = outcome with
            {
                Figures = outcome.Figures with { Flags = [new RunFlag("cold", FlagSeverity.Cold, "Engine cold: not comparable."), .. outcome.Figures.Flags] },
            };
        }

        var time = new UtcTime();
        time.Advance(TimeSpan.FromMinutes(minute));
        var result = PerfResult.From(outcome, name, "", Inputs, tune, "ms43x", Ms43x, time.GetUtcNow(), "test");
        Assert.NotNull(new PerfResultStore(Path.Combine(folder, "results"), time).Save(result));
    }

    public static (MainWindow Window, ShellViewModel Shell) Open(string folder, double? fullPedal = SyntheticRuns.FullPedal, string variant = "ms43x")
    {
        var settings = AppSettings.Load(folder);
        settings.FullPedal = fullPedal;
        var (window, shell) = TestShell.Create(settings, new MainWindowViewModel(new UtcTime(), () => ["COM1"]));
        shell.Live.SelectedVariant = variant;
        return (window, shell);
    }

    public static void Connect(ShellViewModel shell, AdxDefinition? definition = null)
    {
        shell.Live.BeginSession();
        shell.Live.CurrentDefinition = definition ?? Ms43x;
    }

    public static void Feed(ShellViewModel shell, IEnumerable<LiveSample> samples)
    {
        foreach (var sample in samples)
        {
            shell.Live.ShowSample(sample);
        }
    }

    public static void Select(ShellViewModel shell, string title) =>
        shell.Performance.SelectedEntry = shell.Performance.Entries.OfType<PerformanceTestEntry>().Single(entry => entry.Title == title);

    /// <summary>
    /// <paramref name="seconds"/> of ms43x samples at <paramref name="rate"/> holding these physical values, with N carrying
    /// <paramref name="rpm"/> and <paramref name="gear"/>; times from <paramref name="from"/> seconds.
    /// </summary>
    public static IReadOnlyList<LiveSample> Steady(double seconds, double rpm, int gear, double from = 0, double rate = 30,
        params (string Id, double Value)[] values)
    {
        var samples = new List<LiveSample>();
        var start = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i <= (int)Math.Round(seconds * rate); i++)
        {
            var raw = values.Select(value => (value.Id, Raw(value.Id, value.Value))).Append(("N", (long)Math.Round(rpm) | (long)gear << 13)).ToArray();
            var payload = LivePayloads.Build(Ms43x, raw);
            var at = TimeSpan.FromSeconds(from + i / rate);
            samples.Add(new LiveSample(start + at, i, Ms43x.DecodePayload(payload), payload, at, at + TimeSpan.FromMilliseconds(5)));
        }

        return samples;
    }

    // The raw value of a linear channel that decodes closest to value.
    private static long Raw(string id, double value)
    {
        var channel = Ms43x.Values.First(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        double Decode(long raw) => Ms43x.DecodePayload(LivePayloads.Build(Ms43x, (channel.Id, raw)), [channel])[0].Value;
        var offset = Decode(0);
        return Math.Clamp((long)Math.Round((value - offset) / (Decode(1) - offset)), 0, (1L << channel.SizeInBits) - 1);
    }

    /// <summary>The ms43x definition with its vehicle speed channel renamed, as a definition that does not log it.</summary>
    public static AdxDefinition WithoutSpeed()
    {
        var assembly = typeof(BundledDefinitions).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("MS43X001_Logging.adx", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var text = reader.ReadToEnd().Replace("<ADXVALUE id=\"VS\" ", "<ADXVALUE id=\"VSX\" ", StringComparison.Ordinal);
        return AdxDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(text)), "no-speed.adx");
    }
}

/// <summary>The Performance tab's Run side, fed by Live's samples.</summary>
public sealed class PerformanceRunTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static IReadOnlyList<LiveSample> ThirdGearPull() => SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0);

    private static double Number(string text) => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    [AvaloniaFact]
    public void RailListsTheTests()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        var performance = shell.Performance;

        Assert.Equal(
            ["Pulls", "Full-throttle pull", "Knock check", "Acceleration", "Roll-on", "Standing start", "Setup", "Speed calibration", "Car", "Link test", "Pedal"],
            performance.Entries.Select(entry => entry switch
            {
                PerformanceGroup group => group.Title,
                PerformanceTestEntry test => test.Title,
                _ => "?",
            }));
        Assert.All(performance.Entries.OfType<PerformanceTestEntry>(), entry => Assert.False(string.IsNullOrWhiteSpace(entry.Summary)));
        Assert.Equal("Full-throttle pull", performance.SelectedEntry.Title);
        Assert.True(performance.SelectedEntry.IsCurrent);
        Assert.True(performance.IsRunMode);
        Assert.False(performance.IsResultsMode);

        performance.IsResultsMode = true;
        Assert.False(performance.IsRunMode);
        PerfFeed.Select(shell, "Knock check");
        Assert.True(performance.IsRunMode);
        Assert.Equal([4], performance.Run.GearChoices);
        Assert.Single(performance.Entries.OfType<PerformanceTestEntry>(), entry => entry.IsCurrent);
    }

    [AvaloniaFact]
    public void ArmIsOffUntilConnected()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        var run = shell.Performance.Run;
        Assert.Equal("Arm pull", run.ArmText);
        Assert.False(run.CanArm);
        Assert.Equal("Connect to the car to run a test.", run.ArmTip);

        PerfFeed.Connect(shell);
        Assert.True(run.CanArm);
        Assert.Null(run.ArmTip);

        shell.Live.Disconnect();
        Assert.Equal("Connect to the car to run a test.", run.ArmTip);
    }

    [AvaloniaFact]
    public void PullNeedsACalibratedPedal()
    {
        var (_, shell) = PerfFeed.Open(_folder, fullPedal: null);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;

        Assert.False(run.CanArm);
        Assert.Equal("Calibrate the pedal first.", run.ArmTip);
        PerfFeed.Select(shell, "Link test");
        Assert.Equal("Start link test", run.ArmText);
        Assert.True(run.CanArm);
    }

    [AvaloniaFact]
    public void PullRunsFromLiveSamples()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        Assert.Equal([2, 3, 4], run.GearChoices);
        Assert.Equal(3, run.SelectedGear);
        Assert.Equal("2000", run.StartRpm);
        Assert.Equal("6400", run.EndRpm);
        Assert.Equal("None", Assert.Single(run.BaselineChoices).Text);

        run.Arm();
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.NotEmpty(run.Checks);
        var samples = ThirdGearPull();
        PerfFeed.Feed(shell, samples.Take(20));
        Assert.Equal("Waiting for full throttle…", run.Status);
        PerfFeed.Feed(shell, samples.Skip(20));

        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.True(run.HasResult);
        var time = Assert.Single(run.Figures, figure => figure.Key == "time");
        Assert.Equal("2000 to 6400 rpm", time.Label);
        Assert.Equal(5.5, Number(time.Value), 0.02);
        Assert.Equal("s", time.Unit);
        Assert.DoesNotContain(run.Figures, figure => figure.Key == "peak-power-rpm");
        Assert.Equal("3rd pull 12:00", run.Name);
        Assert.Equal("", run.Description);
        Assert.Null(run.Direction);
        // One name and one unit per thing, as the analysis names them.
        Assert.StartsWith("Conditions: intake air 25 °C · coolant 92 °C · oil 88 °C · pressure ", run.ConditionsText);
        Assert.Contains(" kPa · battery ", run.ConditionsText);
        Assert.Contains(" V · RON adaptation ", run.ConditionsText);
        Assert.Matches(@"^Ignition (\d+\.\d °CRK|n/a) · VANOS in (\d+\.\d °CRK|n/a) · AFR target \S+ · intake air 25 °C$", run.LiveValuesText);
        Assert.Equal("Full pedal reads 100.0 °PVS.", run.PedalText);
        Assert.NotEmpty(run.LiveTrace);
        Assert.True(run.LiveTrace[^1].Y >= 6400);
        Assert.True(run.Version > 0);

        // The band is remembered for the next run of this preset.
        Assert.Equal([2000.0, 6400.0], shell.Tune.Settings.PerformanceBands["pull-3"]);
    }

    [AvaloniaFact]
    public void SaveWritesAResultWithTheTune()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        var samples = ThirdGearPull();
        PerfFeed.Feed(shell, samples);
        Assert.Equal("No bin open", run.TuneText);
        run.Name = "Baseline";
        run.Description = "SP95, dry";
        run.IsDirectionA = true;

        run.Save();

        Assert.Null(run.SaveError);
        Assert.True(run.IsSaved);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(shell.Tune.Settings.Folder, "results"), "*.json"));
        Assert.Equal("perf-20261021-120000.json", Path.GetFileName(file));
        var row = Assert.Single(shell.Performance.Results.Rows);
        Assert.Equal("Baseline", row.Name);
        Assert.Equal("Pull, 3rd", row.TestText);
        Assert.Equal("No bin", row.TuneText);
        var stored = new PerfResultStore(Path.GetDirectoryName(file)!).Load(Path.GetFileName(file))!;
        Assert.Equal("SP95, dry", stored.Description);
        Assert.Equal("A", stored.Direction);
        Assert.Null(stored.Tune);
        Assert.Equal("ms43x", stored.Variant);
        // Made when the run's last sample arrived, the end crossing, before the pull's last sample.
        Assert.Contains(samples, sample => sample.Time == stored.Created);
        Assert.True(stored.Created < samples[^1].Time);

        // The saved run is the next run's baseline.
        run.RunAgain();
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Equal(["None", "Baseline"], run.BaselineChoices.Select(choice => choice.Text));
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);
        Assert.NotEmpty(run.BaselineTrace!);
    }

    // The baseline is recomputed from its samples with the run's car values, so a test mass changed since it was saved never
    // shows up as a gain against it.
    [AvaloniaFact]
    public void BaselineDeltasUseTheRunsCarValues()
    {
        // Saved with 1480 kg; the same pull is then run with 1650 kg entered on Car.
        PerfFeed.SaveResult(_folder, "Baseline", 6.0, minute: 0);
        var (_, shell) = PerfFeed.Open(_folder);
        shell.Tune.Settings.TestMassKg = 1650;
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);

        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());

        var power = Assert.Single(run.Figures, figure => figure.Key == "peak-power");
        Assert.NotNull(power.Delta);
        Assert.InRange(Number(power.Delta!), -1, 1);
        Assert.InRange(Number(Assert.Single(run.Figures, figure => figure.Key == "time").Delta!), -0.01, 0.01);
    }

    [AvaloniaFact]
    public void AnotherGearBringsItsBandOnceTheRunIsOver()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull().Take(90));
        // The gear of a run that is on stays.
        run.IsGear2 = true;
        Assert.Equal(3, run.SelectedGear);
        Assert.False(run.IsGear2);
        Assert.Equal("2000", run.StartRpm);

        PerfFeed.Feed(shell, ThirdGearPull().Skip(90));
        Assert.True(run.HasResult);
        run.IsGear2 = true;

        Assert.Equal(2, run.SelectedGear);
        Assert.Equal("1500", run.StartRpm);
        Assert.Equal("6400", run.EndRpm);
        run.IsGear4 = true;
        Assert.Equal("4500", run.EndRpm);
        Assert.False(run.IsGear2);
    }

    // The user's car bin: 7104 rpm in 3rd, so a 3rd gear pull ends at 6954 and keeps the tune.
    [AvaloniaFact]
    public async Task ThirdGearEndsBelowTheOpenBinsLimit()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(select: null);
        var run = shell.Performance.Run;
        Assert.Equal("2000", run.StartRpm);
        Assert.Equal("6954", run.EndRpm);
        Assert.Equal("Ends 150 rpm below the open bin's rev limit in 3rd (7104 rpm).", run.BandNote);
        run.IsGear2 = true;
        Assert.Equal("6400", run.EndRpm);
        Assert.Null(run.BandNote);

        shell.Live.SelectedVariant = "ms43x";
        shell.Tune.Settings.FullPedal = SyntheticRuns.FullPedal;
        PerfFeed.Connect(shell);
        run.Arm();
        PerfFeed.Feed(shell, SyntheticRuns.Pull(PerfFeed.Ms43x, 2, 1200, 6500, 6.0));
        Assert.True(run.HasResult);
        Assert.Equal("Tune: CAR_current_43X001_SP95_STAGE1.bin · 43X001 · saved, no edits", run.TuneText);
    }

    // Closing the window asks about a finished run that is not saved, as it asks about unsaved Tune edits: only Discard, Run
    // again or arming another test drop one.
    [AvaloniaFact]
    public void CloseWithAnUnsavedRunAsks()
    {
        var (window, shell) = PerfFeed.Open(_folder);
        window.Show();
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());
        Assert.True(run.CanSave);
        string? asked = null;
        var answer = SaveChoice.Cancel;
        run.AskToSaveResult = message =>
        {
            asked = message;
            return Task.FromResult(answer);
        };

        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.Equal("Save the result \"3rd pull 12:00\"?", asked);
        Assert.True(run.CanSave);

        // With another test on the rail the run is still kept, and Save writes it.
        PerfFeed.Select(shell, "Roll-on");
        answer = SaveChoice.Save;
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
    }

    [AvaloniaFact]
    public void DiscardDropsTheRun()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());

        run.Discard();

        Assert.Null(run.Stage);
        Assert.False(run.HasResult);
        Assert.True(run.CanArm);
        Assert.False(Directory.Exists(Path.Combine(shell.Tune.Settings.Folder, "results")));
    }

    [AvaloniaFact]
    public void CancelStopsAWaitingRun()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        Assert.False(run.CanArm);

        run.Cancel();

        Assert.Null(run.Stage);
        Assert.True(run.CanArm);
    }

    [AvaloniaFact]
    public void RunSurvivesSwitchingTabs()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Assert.True(shell.IsPerformance);
        Assert.True(shell.Performance.IsVisible);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        var samples = ThirdGearPull();
        PerfFeed.Feed(shell, samples.Take(90));
        Assert.Equal(RunStage.Running, run.Stage);

        shell.SelectedTab = ShellViewModel.LiveTab;
        Assert.False(shell.Performance.IsVisible);
        PerfFeed.Feed(shell, samples.Skip(90).Take(30));
        Assert.Equal(RunStage.Running, run.Stage);
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Assert.Equal(RunStage.Running, run.Stage);
        PerfFeed.Feed(shell, samples.Skip(120));

        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.Equal(5.5, Number(Assert.Single(run.Figures, figure => figure.Key == "time").Value), 0.02);
    }

    [AvaloniaFact]
    public void DisconnectEndsTheRunAsIncomplete()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull().Take(120));

        shell.Live.Disconnect();

        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.True(run.HasResult);
        Assert.Contains(run.Flags, flag => flag.Code == "link-lost");
        Assert.Empty(shell.Live.WarmUpBanners);
    }

    [AvaloniaFact]
    public void ColdEngineBlocksTheStart()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, coolantC: 64));

        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.False(Assert.Single(run.Checks, check => check.Label == "Engine warm").Met);
        // Live's banners show on Performance too.
        Assert.Same(shell.Live.WarmUpBanners, run.WarmUp);
        Assert.Contains(run.WarmUp, banner => banner.IsDanger);
    }

    [AvaloniaFact]
    public void KnockCheckIsOffOnStockDefinitions()
    {
        var (_, shell) = PerfFeed.Open(_folder, variant: "b25");
        var knock = shell.Performance.Entries.OfType<PerformanceTestEntry>().Single(entry => entry.Title == "Knock check");
        Assert.Equal("This definition does not log knock per cylinder.", knock.OffText);
        Assert.Null(shell.Performance.Entries.OfType<PerformanceTestEntry>().Single(entry => entry.Title == "Full-throttle pull").OffText);

        PerfFeed.Select(shell, "Knock check");
        var run = shell.Performance.Run;
        Assert.False(run.CanArm);
        Assert.Equal("This definition does not log knock per cylinder.", run.ArmTip);

        // Connected on the stock definition: still off, for the same reason.
        PerfFeed.Connect(shell, BundledDefinitions.Load("b25"));
        Assert.Equal("This definition does not log knock per cylinder.", run.ArmTip);

        shell.Live.Disconnect();
        shell.Live.SelectedVariant = "ms43x";
        Assert.Null(knock.OffText);
        Assert.Equal("Connect to the car to run a test.", run.ArmTip);
    }

    // Was DialogTests.StartTestIsDisabledWithoutASpeedChannel: the old window's Start test is now the standing start's Arm.
    [AvaloniaFact]
    public void StandingStartIsOffWithoutASpeedChannel()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Standing start");
        var run = shell.Performance.Run;
        // The button names the test it arms.
        Assert.Equal("Arm standing start", run.ArmText);

        PerfFeed.Connect(shell, PerfFeed.WithoutSpeed());
        Assert.False(run.CanArm);
        Assert.Equal("This definition does not log vehicle speed.", run.ArmTip);
        Assert.Equal("This definition does not log vehicle speed.",
            shell.Performance.Entries.OfType<PerformanceTestEntry>().Single(entry => entry.Title == "Standing start").OffText);
    }

    // Was DialogTests.VsChannelDrivesThePerformanceTimer: 0 to 100 km/h, now back-extrapolated.
    [AvaloniaFact]
    public void StandingStartTimesFromLiveSamples()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Standing start");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        var samples = SyntheticRuns.Standing(PerfFeed.Ms43x, 7.6);
        PerfFeed.Feed(shell, samples.Take(45));
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.True(Assert.Single(run.Checks, check => check.Label == "Stopped").Met);

        PerfFeed.Feed(shell, samples.Skip(45));

        Assert.Equal(RunStage.Finished, run.Stage);
        var to100 = Assert.Single(run.Figures, figure => figure.Key == "t-0-100");
        Assert.Equal(7.6, Number(to100.Value), 0.05);
        Assert.Equal("Standing start 12:00", run.Name);
    }

    [AvaloniaFact]
    public void CalibrationIsSavedToSettings()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Speed calibration");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        Assert.Equal("Start capture", run.ArmText);
        run.Arm();
        Assert.Equal(RunStage.Running, run.Stage);

        // Steady 100 km/h in 5th at 3000 rpm, a light pedal.
        PerfFeed.Feed(shell, PerfFeed.Steady(4, 3000, 5, values: [("VS", 100), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));

        Assert.Equal(100.0 / 3000, run.Captured[5], 6);
        var row = Assert.Single(run.CapturedRows);
        Assert.Equal("5th", row.Gear);
        Assert.Equal("33.3", row.KmhPer1000);
        Assert.True(run.CanSaveCalibration);

        run.SaveCalibration();

        Assert.Equal(100.0 / 3000, shell.Tune.Settings.KmhPerRpm[5], 6);
        Assert.False(shell.Tune.Settings.GpsChecked[5]);
        Assert.Null(run.Stage);
        Assert.Equal(100.0 / 3000, AppSettings.Load(_folder).KmhPerRpm[5], 6);
    }

    [AvaloniaFact]
    public void GpsSpeedCorrectsTheCalibration()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Speed calibration");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, PerfFeed.Steady(4, 3000, 5, values: [("VS", 100), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));

        // The speedometer's reading is taken as the GPS speed is typed.
        run.GpsKmh = "96";
        Assert.Equal("100", run.GpsVsKmh);
        run.SaveCalibration();

        Assert.Equal(100.0 / 3000 * 0.96, shell.Tune.Settings.KmhPerRpm[5], 6);
        Assert.True(shell.Tune.Settings.GpsChecked[5]);

        // The speedometer's own ratio stays beside the corrected one: the slip check compares VS with it.
        Assert.Equal(100.0 / 3000, shell.Tune.Settings.SpeedometerKmhPerRpm[5], 6);
        Assert.Equal(100.0 / 3000, AppSettings.Load(_folder).SpeedometerKmhPerRpm[5], 6);
        var inputs = shell.Performance.CurrentInputs();
        Assert.Equal(100.0 / 3000 * 0.96, inputs.KmhPerRpm[5], 6);
        Assert.Equal(100.0 / 3000, inputs.SpeedometerKmhPerRpm![5], 6);
    }

    [AvaloniaFact]
    public void PedalStepStoresTheFullPedal()
    {
        var (_, shell) = PerfFeed.Open(_folder, fullPedal: null);
        PerfFeed.Select(shell, "Pedal");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        Assert.Equal("Read pedal", run.ArmText);
        run.Arm();

        // Ignition on, engine off, the pedal floored and held.
        PerfFeed.Feed(shell, PerfFeed.Steady(2.5, 0, 0, values: [("PVS", 96), ("TCO", 20), ("TOIL", 20)]));

        Assert.Equal(RunStage.Finished, run.Stage);
        Assert.Equal(96, shell.Tune.Settings.FullPedal!.Value, 0.25);
        Assert.Equal(96, AppSettings.Load(_folder).FullPedal!.Value, 0.25);
        PerfFeed.Select(shell, "Full-throttle pull");
        Assert.True(run.CanArm);
    }

    // A calibration saved gear by gear keeps each gear's GPS check, so the line under a test's options says what holds for its
    // own gear.
    [AvaloniaFact]
    public void GpsCheckIsKeptPerGear()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Speed calibration");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;

        // 3rd checked against a GPS speed, then 4th saved without one.
        run.Arm();
        PerfFeed.Feed(shell, PerfFeed.Steady(4, 3600, 3, values: [("VS", 80), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));
        run.GpsKmh = "78";
        run.SaveCalibration();
        run.Arm();
        PerfFeed.Feed(shell, PerfFeed.Steady(4, 3000, 4, from: 10, values: [("VS", 100), ("PVS", 20), ("TCO", 92), ("TOIL", 88)]));
        run.SaveCalibration();

        Assert.True(shell.Tune.Settings.GpsChecked[3]);
        Assert.False(shell.Tune.Settings.GpsChecked[4]);
        PerfFeed.Select(shell, "Full-throttle pull");
        Assert.StartsWith("Speed calibration: 3rd ", run.InputsText);
        Assert.DoesNotContain("GPS", run.InputsText);
        run.IsGear4 = true;
        Assert.Contains("km/h per 1000 rpm, speed not GPS-checked.", run.InputsText);
        // A result keeps which gears were checked.
        Assert.Equal([3], shell.Performance.CurrentInputs().GpsCheckedGears);
    }

    // One glitch above the calibrated full pedal never raises it; a reading held for 0.5 s does, to its lowest during the hold.
    [AvaloniaFact]
    public void AHigherPedalCountsOnlyOnceItHolds()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var settings = shell.Tune.Settings;
        var run = shell.Performance.Run;
        var raised = new List<string?>();
        run.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        PerfFeed.Feed(shell, PerfFeed.Steady(0.1, 800, 0, values: [("PVS", 110), ("TCO", 92), ("TOIL", 88)]));
        PerfFeed.Feed(shell, PerfFeed.Steady(1, 800, 0, from: 0.2, values: [("PVS", 40), ("TCO", 92), ("TOIL", 88)]));
        Assert.Equal(SyntheticRuns.FullPedal, settings.FullPedal);

        PerfFeed.Feed(shell, PerfFeed.Steady(0.6, 800, 0, from: 2, values: [("PVS", 104), ("TCO", 92), ("TOIL", 88)]));
        Assert.Equal(104, settings.FullPedal!.Value, 0.5);
        Assert.Equal(104, AppSettings.Load(_folder).FullPedal!.Value, 0.5);
        Assert.Contains(nameof(PerformanceRunViewModel.PedalText), raised);
        Assert.StartsWith("Full pedal reads 104.", run.PedalText);
    }

    // Each sample is decoded once, a hidden tab's run view raises nothing per sample, and the ready checks change in place.
    [AvaloniaFact]
    public void PerSampleWorkStaysSmall()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        var samples = ThirdGearPull();
        PerfFeed.Feed(shell, samples.Take(5));
        Assert.Same(shell.Live.LastFrame, run.LastFrame);

        var raised = new List<string?>();
        run.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        PerfFeed.Feed(shell, samples.Skip(5).Take(10));
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Empty(raised);

        // Shown again, the run view catches up at once.
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Assert.Contains(nameof(PerformanceRunViewModel.Status), raised);

        var checks = run.Checks;
        var first = checks[0];
        var changes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)checks).CollectionChanged += (_, _) => changes++;
        raised.Clear();
        PerfFeed.Feed(shell, samples.Skip(15).Take(5));
        Assert.Equal(RunStage.Waiting, run.Stage);
        Assert.Same(checks, run.Checks);
        Assert.Same(first, run.Checks[0]);
        Assert.Equal(0, changes);
        Assert.DoesNotContain(nameof(PerformanceRunViewModel.Checks), raised);
        // Arm's state changes with the stage, not with every sample.
        Assert.DoesNotContain(nameof(PerformanceRunViewModel.ArmIsPrimary), raised);
    }

    // Opening a bin again keeps a band typed but not armed, and a finished run's band.
    [AvaloniaFact]
    public async Task OpeningABinKeepsATypedBand()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(select: null);
        var run = shell.Performance.Run;
        Assert.Equal("6954", run.EndRpm);

        run.StartRpm = "2500";
        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        Assert.Equal(("2500", "6954"), (run.StartRpm, run.EndRpm));
        Assert.NotNull(run.BandNote);

        // An end typed over the bin's is the user's: it stays and no longer reads as the bin's limit.
        run.EndRpm = "6400";
        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        Assert.Equal(("2500", "6400"), (run.StartRpm, run.EndRpm));
        Assert.Null(run.BandNote);

        shell.Live.SelectedVariant = "ms43x";
        shell.Tune.Settings.FullPedal = SyntheticRuns.FullPedal;
        PerfFeed.Connect(shell);
        run.StartRpm = "2000";
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());
        Assert.True(run.HasResult);
        // Without the remembered band a reload would bring the bin's.
        shell.Tune.Settings.PerformanceBands.Remove("pull-3");
        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        Assert.Equal(("2000", "6400"), (run.StartRpm, run.EndRpm));
    }

    // A notice belongs to the test that raised it.
    [AvaloniaFact]
    public void NoticeStaysWithItsTest()
    {
        var (_, shell) = PerfFeed.Open(_folder, fullPedal: null);
        PerfFeed.Select(shell, "Pedal");
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, PerfFeed.Steady(2.5, 0, 0, values: [("PVS", 96), ("TCO", 20), ("TOIL", 20)]));
        Assert.StartsWith("Pedal calibrated: ", run.Notice);

        PerfFeed.Select(shell, "Full-throttle pull");
        Assert.Null(run.Notice);
        PerfFeed.Select(shell, "Pedal");
        Assert.StartsWith("Pedal calibrated: ", run.Notice);
    }

    // Arm on another test names the one that is on and leads to it.
    [AvaloniaFact]
    public void TheTipNamesTheRunningTestAndLeadsToIt()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();

        PerfFeed.Select(shell, "Roll-on");
        Assert.Equal("Full-throttle pull is armed: cancel it first.", run.ArmTip);
        Assert.True(run.ShowsRunningTest);
        PerfFeed.Feed(shell, ThirdGearPull().Take(90));
        Assert.Equal("Full-throttle pull is running: cancel it first.", run.ArmTip);

        run.ShowRunningTest();
        Assert.Equal("Full-throttle pull", shell.Performance.SelectedEntry.Title);
        Assert.Equal(RunStage.Running, run.Stage);
        Assert.False(run.ShowsRunningTest);
    }

    // The link failing as it does on the car (the logger stops on a timeout) keeps what a cold-oil pull measured, saves it and
    // clears the banners.
    [AvaloniaFact]
    public void ALinkFailureKeepsWhatThePullMeasured()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, oilC: 60).Take(120));
        Assert.Contains(shell.Live.WarmUpBanners, banner => banner.IsCaution);

        shell.Live.ReportLoggingStopped(new TimeoutException("The ECU did not answer."));

        Assert.False(shell.Live.IsConnected);
        Assert.True(run.HasResult);
        Assert.Contains(run.Flags, flag => flag.Code == "link-lost");
        Assert.NotEqual("n/a", Assert.Single(run.Figures, figure => figure.Key == "t-2000-4000").Value);
        Assert.Empty(shell.Live.WarmUpBanners);
        run.Save();
        Assert.Null(run.SaveError);
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
    }

    // A result records the definition that decoded its samples, not the picker's choice.
    [AvaloniaFact]
    public void AResultRecordsTheDefinitionThatDecodedIt()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        shell.Live.SelectedVariant = "b25";
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());

        run.Save();

        Assert.Null(run.SaveError);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
        var stored = new PerfResultStore(Path.GetDirectoryName(file)!).Load(Path.GetFileName(file))!;
        Assert.Equal("ms43x", stored.Variant);
        Assert.Equal(BundledDefinitions.Sha256("ms43x"), stored.DefinitionSha256);
        Assert.InRange(PerfCompare.Frames(stored).Max(frame => frame.Rpm ?? 0), 6400, 6600);
    }

    // After Save result, a new name, description or direction is written into the same file by Save changes.
    [AvaloniaFact]
    public void EditsAfterSaveAreWrittenToTheFile()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());
        run.Name = "Baseline";
        run.Save();
        Assert.False(run.CanSave);
        Assert.True(run.ShowsSaved);

        run.Name = "Baseline, dry";
        run.Description = "SP95";
        run.IsDirectionB = true;

        Assert.True(run.CanSave);
        Assert.False(run.ShowsSaved);
        Assert.Equal("Save changes", run.SaveText);
        Assert.True(run.HasUnsavedResult);
        run.Save();

        Assert.False(run.CanSave);
        Assert.False(run.HasUnsavedResult);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_folder, "results"), "*.json"));
        var stored = new PerfResultStore(Path.GetDirectoryName(file)!).Load(Path.GetFileName(file))!;
        Assert.Equal(("Baseline, dry", "SP95", "B"), (stored.Name, stored.Description, stored.Direction));
        Assert.Equal("Baseline, dry", Assert.Single(shell.Performance.Results.Rows).Name);
    }

    // The picker and the dashed trace stay on the baseline the run's deltas were taken against, until Discard or Run again.
    [AvaloniaFact]
    public void TheBaselineStaysWithTheRun()
    {
        PerfFeed.SaveResult(_folder, "Baseline", 6.2, minute: 0);
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);
        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull());

        // Saved, the run is now the newest pull of its preset.
        run.Save();
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);
        Assert.Equal("Baseline: Baseline", run.BaselineLabel);
        PerfFeed.Select(shell, "Roll-on");
        PerfFeed.Select(shell, "Full-throttle pull");
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);
        Assert.False(run.CanChangeBaseline);
        run.SelectedBaseline = new BaselineChoice("None", null);
        Assert.Equal("Baseline", run.SelectedBaseline!.Text);
        Assert.EndsWith(" s against Baseline", Assert.Single(run.Figures, figure => figure.Key == "time").DeltaText);

        // Discard frees the picker: the newest saved pull is the default again.
        run.Discard();
        Assert.True(run.CanChangeBaseline);
        Assert.Equal("3rd pull 12:00", run.SelectedBaseline!.Text);
    }

    // A run goes on while another test is chosen, and a finished one waits there unsaved.
    [AvaloniaFact]
    public void ARunSurvivesChoosingAnotherTest()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        run.Arm();
        var samples = ThirdGearPull();
        PerfFeed.Feed(shell, samples.Take(90));

        PerfFeed.Select(shell, "Car");
        Assert.Null(run.Stage);
        PerfFeed.Feed(shell, samples.Skip(90));
        PerfFeed.Select(shell, "Full-throttle pull");

        Assert.True(run.HasResult);
        Assert.Equal(5.5, Number(Assert.Single(run.Figures, figure => figure.Key == "time").Value), 0.02);
        PerfFeed.Select(shell, "Standing start");
        Assert.False(run.HasResult);
        Assert.True(run.HasUnsavedResult);
        PerfFeed.Select(shell, "Full-throttle pull");
        Assert.True(run.CanSave);
        Assert.Equal("3rd pull 12:00", run.Name);
    }

    // The tyre size is picked like on a tyre shop, width, profile and rim, never typed.
    [AvaloniaFact]
    public void TyreSizeIsPickedFromThreeLists()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Car");
        var run = shell.Performance.Run;
        Assert.Equal(205, run.TyreWidth);
        Assert.Equal(55, run.TyreAspect);
        Assert.Equal(16, run.TyreRim);
        Assert.Equal(135, run.TyreWidths[0]);
        Assert.Equal(355, run.TyreWidths[^1]);
        Assert.Equal([25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85], run.TyreAspects);
        Assert.Equal(13, run.TyreRims[0]);
        Assert.Equal(22, run.TyreRims[^1]);

        run.TyreWidth = 225;
        run.TyreAspect = 45;
        run.TyreRim = 17;

        Assert.Equal("225/45 R17", run.TyreSize);
        Assert.Equal("225/45 R17", AppSettings.Load(_folder).TyreSize);
    }

    // A size saved by hand in another form ("225/45ZR17") still selects its three parts.
    [AvaloniaFact]
    public void ASavedSizeInAnotherFormSelectsItsParts()
    {
        var settings = AppSettings.Load(_folder);
        settings.TyreSize = "225/45ZR17";
        settings.Save();
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Car");
        var run = shell.Performance.Run;

        Assert.Equal(225, run.TyreWidth);
        Assert.Equal(45, run.TyreAspect);
        Assert.Equal(17, run.TyreRim);
    }

    [AvaloniaFact]
    public void CarValuesAreSavedToSettings()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Select(shell, "Car");
        var run = shell.Performance.Run;
        Assert.False(run.HasArm);
        Assert.Equal("1480", run.MassKg);
        Assert.Equal("205/55 R16", run.TyreSize);
        Assert.True(run.IsManual);

        run.MassKg = "1520";
        run.TyreSize = "225/45 R17";
        run.IsAutomatic = true;
        run.MassKg = "12";
        run.TyreSize = "junk";

        Assert.NotNull(run.MassError);
        Assert.NotNull(run.TyreError);
        var saved = AppSettings.Load(_folder);
        Assert.Equal(1520, saved.TestMassKg);
        Assert.Equal("225/45 R17", saved.TyreSize);
        Assert.True(saved.Automatic);
    }

    // The checks list only what can be met together, and the note says when to floor it.
    [AvaloniaFact]
    public void TheNoteSaysWhenToFloorIt()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;

        run.Arm();
        PerfFeed.Feed(shell, ThirdGearPull().Take(10));

        Assert.Equal("Once every check is met, floor it: the run starts by itself.", run.ChecksNote);
        Assert.DoesNotContain(run.Checks, check => check.Label == "Full throttle");
    }

    // Samples that stop mid-run are said in words as well as by the marks turning Ink3.
    [AvaloniaFact]
    public void SilentSamplesMidRunAreSaidInWords()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;
        var pull = ThirdGearPull();
        run.Arm();
        PerfFeed.Feed(shell, pull.Take(150));
        Assert.Equal(RunStage.Running, run.Stage);
        Assert.True(run.IsLive);

        ((UtcTime)shell.Live.Time).Advance(TimeSpan.FromSeconds(2.4));
        run.CheckSamples();

        Assert.False(run.IsLive);
        Assert.Equal("No samples for 2 s.", run.Status);

        PerfFeed.Feed(shell, pull.Skip(150).Take(1));
        Assert.True(run.IsLive);
        Assert.NotEqual("No samples for 2 s.", run.Status);
    }

    // The live chart's axes hold for the run: rpm over the band, seconds in whole 5 s steps or the baseline's length, so the
    // chart never rescales with each sample.
    [AvaloniaFact]
    public void TheLiveChartKeepsItsAxesForTheRun()
    {
        var (_, shell) = PerfFeed.Open(_folder);
        PerfFeed.Connect(shell);
        var run = shell.Performance.Run;

        IReadOnlyList<(double?, double?, double?)> Axes()
        {
            var seen = new List<(double?, double?, double?)>();
            foreach (var sample in ThirdGearPull())
            {
                shell.Live.ShowSample(sample);
                if (run.Stage == RunStage.Running && !seen.Contains((run.LiveXMaximum, run.LiveYMinimum, run.LiveYMaximum)))
                {
                    seen.Add((run.LiveXMaximum, run.LiveYMinimum, run.LiveYMaximum));
                }
            }

            return seen;
        }

        run.Arm();
        Assert.Equal([(5.0, 2000.0, 6400.0), (10.0, 2000.0, 6400.0)], Axes());

        // Against a baseline, time runs to the baseline's length.
        Assert.True(run.HasResult);
        run.Save();
        run.RunAgain();
        var length = run.BaselineTrace!.Max(point => point.X);
        Assert.Equal([(length, 2000.0, 6400.0)], Axes());
    }
}
