using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

// The run state machine and its analysis are performance spec 6 and 7; the figure keys and flags are the plan's (Task 6).
public class PerfRunTests
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    // The shared monitor, fed every frame before the run sees it, as Live feeds it (spec 5).
    private readonly WarmUpMonitor warmUp = new();

    private static PerfFrame Frame(LiveSample sample) =>
        PerfChannels.Read(Ms43x, sample.Values, sample.Payload.Span, sample.RequestedAt);

    private static PerfFrame Decode(RecordedSample sample) =>
        PerfChannels.Read(Ms43x, Ms43x.DecodePayload(sample.Payload), sample.Payload, sample.At);

    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Frames(IEnumerable<LiveSample> samples) =>
        samples.Select(s => (Frame(s), s.Payload)).ToArray();

    // Frames of a until the first one where from is true, then those of b: two drives on one timeline spliced.
    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Splice(IReadOnlyList<LiveSample> a, IReadOnlyList<LiveSample> b,
        Func<PerfFrame, bool> from)
    {
        var first = Frames(a);
        var second = Frames(b);
        Assert.Equal(first.Length, second.Length);
        var at = Array.FindIndex(first, x => from(x.Frame));
        Assert.True(at > 0);
        return [.. first.Take(at), .. second.Skip(at)];
    }

    private static (PerfFrame Frame, ReadOnlyMemory<byte> Payload)[] Later(IEnumerable<(PerfFrame Frame, ReadOnlyMemory<byte> Payload)> frames,
        double seconds) =>
        frames.Select(x => (x.Frame with { At = x.Frame.At + TimeSpan.FromSeconds(seconds) }, x.Payload)).ToArray();

    private void Drive(PerfRun run, IEnumerable<(PerfFrame Frame, ReadOnlyMemory<byte> Payload)> frames)
    {
        foreach (var (frame, payload) in frames)
        {
            warmUp.Push(frame);
            run.Push(frame, payload);
        }
    }

    private void Drive(PerfRun run, IEnumerable<LiveSample> samples) => Drive(run, Frames(samples));

    private static Figure Fig(RunFigures figures, string key) => Assert.Single(figures.Figures, f => f.Key == key);

    private static RunFlag Flag(RunFigures figures, string code) => Assert.Single(figures.Flags, f => f.Code == code);

    private static void AllFinite(RunFigures figures)
    {
        Assert.All(figures.Figures, f => Assert.True(f.Value is null || double.IsFinite(f.Value.Value), f.Key));
        Assert.All(figures.Traces, t =>
        {
            Assert.Equal(t.X.Count, t.Y.Count);
            Assert.All(t.X.Concat(t.Y), v => Assert.True(double.IsFinite(v), t.Key));
        });
    }
}
