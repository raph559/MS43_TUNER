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
}
