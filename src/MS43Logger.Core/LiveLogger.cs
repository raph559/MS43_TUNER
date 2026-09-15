namespace MS43Logger.Core;

/// <summary>Something that returns one logger payload per call (a connected <see cref="Ds2Session"/>).</summary>
public interface IPayloadSource
{
    byte[] PollPayload();

    /// <summary>
    /// When the request whose reply the last <see cref="PollPayload"/> returned went out, as <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>;
    /// null when the source sends one request per call, so the time before the call is that request's.
    /// </summary>
    long? LastRequestTimestamp => null;

    /// <summary>The requests the last <see cref="PollPayload"/> sent: 1, or more when it retried a timed-out one inside the call.</summary>
    int LastAttempts => 1;
}

/// <param name="Time">The wall clock once the reply was decoded; for display and file names only.</param>
/// <param name="Payload">The frame's data bytes as polled; channels' raw values are read from it.</param>
/// <param name="RequestedAt">When the request went out, on the run's monotonic clock (zero at the run's start): the closest
/// stamp to when the DME latched the values, so test math uses it (performance spec 4.2).</param>
/// <param name="RepliedAt">When the reply had been read, on the same clock.</param>
/// <param name="Retries">Requests the source sent again inside the poll after a timeout; <paramref name="RequestedAt"/> is the
/// one that got the reply.</param>
public sealed record LiveSample(
    DateTimeOffset Time,
    long Index,
    IReadOnlyList<DecodedValue> Values,
    ReadOnlyMemory<byte> Payload = default,
    TimeSpan RequestedAt = default,
    TimeSpan RepliedAt = default,
    int Retries = 0);

/// <summary>
/// Polls, decodes and publishes samples. Retries the first two consecutive timeouts after 50 ms; the third
/// timeout or any other error stops the loop. An optional pause follows each sample; the default is zero.
/// </summary>
public sealed class LiveLogger
{
    public const int TimeoutLimit = 3;

    private readonly TimeSpan _pause;

    /// <summary>The shortest time from one poll to the next, whatever the source.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>The wait after each sample.</summary>
    public TimeSpan Pause => _pause;
    private readonly SampleRateMeter _rate = new();
    private readonly object _rateLock = new();

    public LiveLogger(TimeSpan? pause = null)
    {
        _pause = pause ?? TimeSpan.Zero;
    }

    /// <summary>Raised on the polling thread for every decoded sample.</summary>
    public event EventHandler<LiveSample>? SampleReceived;

    public event EventHandler<string>? Status;

    /// <summary>Raised with the number of timeouts in a row (1, then 2) when a timeout is about to be retried.</summary>
    public event EventHandler<int>? Retrying;

    public event EventHandler<Exception>? Failed;

    public RateStats? Rate
    {
        get
        {
            lock (_rateLock)
            {
                return _rate.Current;
            }
        }
    }

    public Task RunAsync(IPayloadSource source, AdxDefinition definition, IReadOnlyList<AdxValueDefinition> channels, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(channels);
        lock (_rateLock)
        {
            _rate.Reset();
        }

        return Task.Run(() => Loop(source, definition, channels, token), CancellationToken.None);
    }

    private async Task Loop(IPayloadSource source, AdxDefinition definition, IReadOnlyList<AdxValueDefinition> channels, CancellationToken token)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var timeouts = 0;
        long index = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var requestedAt = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                var payload = source.PollPayload();
                var repliedAt = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                // A source that retried inside the call stamps the request that got the reply: the first one timed out
                // about a listen timeout before the values were latched (performance spec 4.2).
                if (source.LastRequestTimestamp is { } sent
                    && System.Diagnostics.Stopwatch.GetElapsedTime(started, sent) is var resent && resent > requestedAt && resent <= repliedAt)
                {
                    requestedAt = resent;
                }

                var retries = Math.Max(source.LastAttempts - 1, 0);
                lock (_rateLock)
                {
                    _rate.Record(repliedAt);
                }

                var values = definition.DecodePayload(payload, channels);
                if (timeouts > 0)
                {
                    timeouts = 0;
                    Status?.Invoke(this, "Resumed");
                }

                SampleReceived?.Invoke(this, new LiveSample(DateTimeOffset.UtcNow, index++, values, payload, requestedAt, repliedAt, retries));
                if (_pause > TimeSpan.Zero)
                {
                    await Task.Delay(_pause, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Disconnect cancels the run, then closes the port under a poll still waiting for its reply: what that poll
                // throws ends the run quietly instead of reporting a failure or a retry.
                return;
            }
            catch (TimeoutException error)
            {
                timeouts++;
                if (timeouts >= TimeoutLimit)
                {
                    Failed?.Invoke(this, error);
                    return;
                }

                Status?.Invoke(this, $"Polling timeout ({timeouts}/{TimeoutLimit}). Retrying...");
                Retrying?.Invoke(this, timeouts);
                try
                {
                    await Task.Delay(50, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception error)
            {
                Failed?.Invoke(this, error);
                return;
            }
        }
    }
}

public static class GaugeMath
{
    public const double StartAngle = -225;
    public const double Sweep = 270;

    /// <summary>Needle angle in degrees (0 = right, clockwise), sweeping 270° from bottom-left; values outside the scale are clamped.</summary>
    public static double Angle(double min, double max, double value) =>
        StartAngle + Sweep * (Math.Clamp(value, min, max) - min) / (max - min);
}
