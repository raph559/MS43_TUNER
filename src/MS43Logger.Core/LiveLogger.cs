namespace MS43Logger.Core;

/// <summary>Something that returns one logger payload per call (a connected <see cref="Ds2Session"/>).</summary>
public interface IPayloadSource
{
    byte[] PollPayload();
}

public sealed record LiveSample(DateTimeOffset Time, long Index, IReadOnlyList<DecodedValue> Values);

/// <summary>
/// The polling loop: poll, decode, publish, pause. Same rules as before the move out of the window:
/// up to 3 timeouts in a row are retried after 50 ms, any other error stops the loop, and a pause follows every sample.
/// </summary>
public sealed class LiveLogger
{
    public const int TimeoutLimit = 3;

    private readonly TimeSpan _pause;
    private readonly SampleRateMeter _rate = new();
    private readonly object _rateLock = new();

    public LiveLogger(TimeSpan? pause = null)
    {
        _pause = pause ?? TimeSpan.FromMilliseconds(20);
    }

    /// <summary>Raised on the polling thread for every decoded sample.</summary>
    public event EventHandler<LiveSample>? SampleReceived;

    public event EventHandler<string>? Status;

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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timeouts = 0;
        long index = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var payload = source.PollPayload();
                lock (_rateLock)
                {
                    _rate.Record(clock.Elapsed);
                }

                var values = definition.DecodePayload(payload, channels);
                if (timeouts > 0)
                {
                    timeouts = 0;
                    Status?.Invoke(this, "Resumed");
                }

                SampleReceived?.Invoke(this, new LiveSample(DateTimeOffset.UtcNow, index++, values));
                if (_pause > TimeSpan.Zero)
                {
                    await Task.Delay(_pause, token);
                }
            }
            catch (OperationCanceledException)
            {
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
