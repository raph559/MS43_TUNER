using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Performance;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// Faster logging: the logger polls without a pause, every sample goes to the bars, dials, gear, CSV and
/// Performance, the numbers change at most five times a second (the latest one shown), and the knock bars hold their highest
/// value for a second so a one-sample knock cannot flash by.
/// </summary>
public sealed class LiveRateTests
{
    /// <summary>A clock that moves only when told; its timers fire as it passes their time.</summary>
    private sealed class SteppedTime : TimeProvider
    {
        private readonly List<SteppedTimer> _timers = new();
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new SteppedTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var timer in _timers.Where(timer => timer.Due is { } due && due <= _now).ToList())
            {
                timer.Due = null;
                timer.Fire();
            }

            Dispatcher.UIThread.RunJobs();
        }

        private sealed class SteppedTimer(SteppedTime time, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time.GetUtcNow() + dueTime;
                return true;
            }

            public void Dispose() => Due = null;

            public ValueTask DisposeAsync()
            {
                Due = null;
                return ValueTask.CompletedTask;
            }
        }
    }

    private static (MainWindowViewModel Live, SteppedTime Time) Connected()
    {
        var time = new SteppedTime();
        var live = new MainWindowViewModel(time, () => ["COM1"]);
        live.SelectedVariant = "ms43x";
        live.PreviewChannels();
        live.IsConnected = true;
        return (live, time);
    }

    private static void Sample(MainWindowViewModel live, double rpm)
    {
        var channels = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());
        live.ShowSample([new DecodedValue(channels.Single(channel => channel.Id == "N"), rpm)]);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Row(MainWindowViewModel live, string id) => live.LiveValues.Single(value => value.Id == id).Value;

    [AvaloniaFact]
    public void NumbersChangeFiveTimesASecond_TheBarsWithEverySample()
    {
        var (live, time) = Connected();
        var rpm = live.Gauges[0];

        Sample(live, 2000);
        Assert.Equal(("2000", 2000.0), (rpm.Display, rpm.Value));
        var first = Row(live, "N");

        time.Advance(TimeSpan.FromMilliseconds(40));
        Sample(live, 2100);
        Assert.Equal(2100, rpm.Value);
        Assert.Equal("2000", rpm.Display);
        Assert.Equal(first, Row(live, "N"));

        // No newer sample: the latest number shows when its 200 ms come.
        time.Advance(TimeSpan.FromMilliseconds(160));
        Assert.Equal("2100", rpm.Display);
        Assert.NotEqual(first, Row(live, "N"));

        time.Advance(TimeSpan.FromMilliseconds(250));
        Sample(live, 2300);
        Assert.Equal("2300", rpm.Display);
    }

    [AvaloniaFact]
    public void AKnockHoldsItsBarForASecond()
    {
        var (live, time) = Connected();
        live.CurrentDefinition = PerfFeed.Ms43x;
        var pull = SyntheticRuns.Pull(PerfFeed.Ms43x, 3, 1700, 6500, 6.0, knockCyl4: 1.6);
        var knocking = pull.First(sample => PerfChannels.Read(PerfFeed.Ms43x, sample.Values, sample.Payload.Span, TimeSpan.Zero).KnockRetard[3] >= 1.5);
        var quiet = pull[^1];

        live.ShowSample(knocking);
        Assert.Equal(1.5, live.Knock[3]);
        time.Advance(TimeSpan.FromMilliseconds(600));
        live.ShowSample(quiet);
        Assert.Equal(1.5, live.Knock[3]);
        Assert.NotNull(live.KnockWarning);

        time.Advance(TimeSpan.FromMilliseconds(500));
        live.ShowSample(quiet);
        Assert.Equal(0, live.Knock[3]);
        Assert.Null(live.KnockWarning);
    }
}
