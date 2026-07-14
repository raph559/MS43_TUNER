using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

public class LiveLoggerTests
{
    private sealed class FakeSource(params Func<byte[]>[] steps) : IPayloadSource
    {
        private int _next;

        public byte[] PollPayload() => _next < steps.Length ? steps[_next++]() : throw new OperationCanceledException();
    }

    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    private static byte[] Payload(byte rpmHigh)
    {
        var payload = new byte[73];
        payload[0x04] = rpmHigh;
        return payload;
    }

    private static Func<byte[]> Timeout() => () => throw new TimeoutException("no reply");

    [Fact]
    public async Task LoggerPublishesDecodedSamples()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var samples = new List<LiveSample>();
        logger.SampleReceived += (_, sample) => samples.Add(sample);
        var source = new FakeSource(() => Payload(1), () => Payload(2), () => Payload(3));
        var channels = Ms43x.MatchChannels(["N"]);

        await logger.RunAsync(source, Ms43x, channels, CancellationToken.None);

        Assert.Equal(new long[] { 0, 1, 2 }, samples.Select(sample => sample.Index));
        Assert.Equal(Ms43x.DecodePayload(Payload(3), channels)[0].Value, samples[2].Values[0].Value);
        Assert.NotNull(logger.Rate);
    }

    [Fact]
    public async Task ThreeTimeoutsInARowStopTheLogger()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var samples = 0;
        var statuses = new List<string>();
        Exception? failure = null;
        logger.SampleReceived += (_, _) => samples++;
        logger.Status += (_, status) => statuses.Add(status);
        logger.Failed += (_, error) => failure = error;
        var source = new FakeSource(Timeout(), Timeout(), () => Payload(1), Timeout(), Timeout(), Timeout(), () => Payload(2));

        await logger.RunAsync(source, Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);

        Assert.Equal(1, samples);
        Assert.IsType<TimeoutException>(failure);
        Assert.Equal(
            new[] { "Polling timeout (1/3). Retrying...", "Polling timeout (2/3). Retrying...", "Resumed", "Polling timeout (1/3). Retrying...", "Polling timeout (2/3). Retrying..." },
            statuses);
    }

    [Fact]
    public async Task RetryingCountsTimeoutsInARow()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var retries = new List<int>();
        logger.Retrying += (_, count) => retries.Add(count);
        var source = new FakeSource(Timeout(), Timeout(), () => Payload(1), Timeout(), Timeout(), Timeout(), () => Payload(2));

        await logger.RunAsync(source, Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 1, 2 }, retries);
    }

    [Fact]
    public async Task OtherErrorsStopAtOnceAndCancellationIsQuiet()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        Exception? failure = null;
        logger.Failed += (_, error) => failure = error;

        await logger.RunAsync(new FakeSource(() => throw new InvalidOperationException("bad frame")), Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);
        Assert.IsType<InvalidOperationException>(failure);

        failure = null;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await logger.RunAsync(new FakeSource(() => Payload(1)), Ms43x, Ms43x.MatchChannels(["N"]), cancelled.Token);
        Assert.Null(failure);
    }

    [Fact]
    public void ReadRawFollowsTheChannelLayout()
    {
        var payload = LivePayloads.Build(Ms43x, ("N", 2480 | 3 << 13), ("TCO", 124));
        var n = Ms43x.Values.First(v => v.Id == "N");
        var tco = Ms43x.Values.First(v => v.Id == "TCO");

        Assert.Equal(27056, AdxDefinition.ReadRaw(n, payload));
        Assert.Equal(2480, Ms43x.DecodePayload(payload, [n])[0].Value);
        Assert.Equal(124, AdxDefinition.ReadRaw(tco, payload));
    }

    [Fact]
    public async Task LoggerPublishesThePayload()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var samples = new List<LiveSample>();
        logger.SampleReceived += (_, sample) => samples.Add(sample);

        await logger.RunAsync(new FakeSource(() => Payload(1), () => Payload(2)), Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);

        Assert.Equal(Payload(2), samples[1].Payload.ToArray());
    }

    // Tests time a run on the request's monotonic stamp, not the wall clock after decoding (performance spec 4.2).
    [Fact]
    public async Task SamplesCarryRequestAndReplyTimes()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var samples = new List<LiveSample>();
        logger.SampleReceived += (_, sample) => samples.Add(sample);
        Func<byte[]> Slow(byte rpmHigh) => () =>
        {
            Thread.Sleep(5);
            return Payload(rpmHigh);
        };

        await logger.RunAsync(new FakeSource(Slow(1), Slow(2), Slow(3)), Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);

        Assert.Equal(3, samples.Count);
        Assert.All(samples, s => Assert.True(s.RequestedAt < s.RepliedAt));
        Assert.True(samples[0].RepliedAt <= samples[1].RequestedAt);
        Assert.True(samples[1].RepliedAt <= samples[2].RequestedAt);
    }

    // A poll the source retried inside its call is stamped at the request that got the reply, not at the first one, which
    // timed out 400 ms earlier on the ms43x definition (final review, finding 2).
    [Fact]
    public async Task ARetriedPollIsStampedAtTheRequestThatReplied()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        var samples = new List<LiveSample>();
        logger.SampleReceived += (_, sample) => samples.Add(sample);

        await logger.RunAsync(new RetryingSource(Payload(1)), Ms43x, Ms43x.MatchChannels(["N"]), CancellationToken.None);

        var sample = Assert.Single(samples);
        Assert.Equal(1, sample.Retries);
        var beforeReply = sample.RepliedAt - sample.RequestedAt;
        Assert.True(beforeReply < TimeSpan.FromMilliseconds(100), $"stamped {beforeReply.TotalMilliseconds:0} ms before its reply");
        Assert.True(sample.RequestedAt > TimeSpan.Zero);
    }

    // The first request times out inside the call, the second one gets the reply.
    private sealed class RetryingSource(byte[] payload) : IPayloadSource
    {
        private bool _polled;

        public long? LastRequestTimestamp { get; private set; }

        public int LastAttempts { get; private set; }

        public byte[] PollPayload()
        {
            if (_polled)
            {
                throw new OperationCanceledException();
            }

            _polled = true;
            Thread.Sleep(250);
            LastRequestTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            LastAttempts = 2;
            Thread.Sleep(5);
            return payload;
        }
    }

    [Theory]
    [InlineData(0, -225)]
    [InlineData(8000, 45)]
    [InlineData(4000, -90)]
    [InlineData(9000, 45)]
    [InlineData(-50, -225)]
    public void GaugeAngleClampsAndSweeps(double value, double angle)
    {
        Assert.Equal(angle, GaugeMath.Angle(0, 8000, value), 9);
    }

    // Disconnect cancels the run, then closes the port under a poll still waiting for its reply (final review, Important 2).
    [Fact]
    public async Task APortClosedByDisconnectIsNotAFailure()
    {
        var logger = new LiveLogger(pause: TimeSpan.Zero);
        Exception? failure = null;
        var retries = 0;
        logger.Failed += (_, error) => failure = error;
        logger.Retrying += (_, _) => retries++;

        using var closed = new CancellationTokenSource();
        await logger.RunAsync(new FakeSource(() => { closed.Cancel(); throw new InvalidOperationException("The port is closed."); }),
            Ms43x, Ms43x.MatchChannels(["N"]), closed.Token);
        Assert.Null(failure);

        using var timedOut = new CancellationTokenSource();
        await logger.RunAsync(new FakeSource(() => { timedOut.Cancel(); throw new TimeoutException("no reply"); }),
            Ms43x, Ms43x.MatchChannels(["N"]), timedOut.Token);
        Assert.Null(failure);
        Assert.Equal(0, retries);
    }
}
