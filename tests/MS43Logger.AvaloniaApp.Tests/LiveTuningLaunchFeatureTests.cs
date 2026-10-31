using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Launch control, no-lift shift and rolling anti-lag (124..126) as settings groups on the Live tuning page.</summary>
public sealed class LiveTuningLaunchFeatureTests
{

    private sealed class ManualTicker : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
