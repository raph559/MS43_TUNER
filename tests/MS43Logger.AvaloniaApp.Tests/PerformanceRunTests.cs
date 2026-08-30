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
    /// <paramref name="rpm"/> and <paramref name="gear"/> (performance spec 4.1); times from <paramref name="from"/> seconds.
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

/// <summary>The Performance tab's Run side (performance spec 6, 9.1), fed by Live's samples.</summary>
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
        // One name and one unit per thing, as the analysis names them (minor review, finding 36).
        Assert.StartsWith("Conditions: intake air 25 °C · coolant 92 °C · oil 88 °C · pressure ", run.ConditionsText);
        Assert.Contains(" kPa · battery ", run.ConditionsText);
        Assert.Contains(" V · RON adaptation ", run.ConditionsText);
        Assert.Matches(@"^Ignition (\d+\.\d °CRK|n/a) · VANOS in (\d+\.\d °CRK|n/a) · AFR target \S+ · intake air 25 °C$", run.LiveValuesText);
        Assert.Equal("Full pedal reads 100.0 °PVS.", run.PedalText);
        Assert.NotEmpty(run.LiveTrace);
        Assert.True(run.LiveTrace[^1].Y >= 6400);
        Assert.True(run.Version > 0);

        // The band is remembered for the next run of this preset (performance spec 9.2).
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
        // Made when the run's last sample arrived (plan Task 7 ruling), the end crossing, before the pull's last sample.
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
    // shows up as a gain against it (spec 9.2; final review, findings 6 and 8).
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
}
