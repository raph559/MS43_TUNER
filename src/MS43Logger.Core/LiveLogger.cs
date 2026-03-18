namespace MS43Logger.Core;

/// <summary>Something that returns one logger payload per call (a connected <see cref="Ds2Session"/>).</summary>
public interface IPayloadSource
{
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
}

public static class GaugeMath
{
    public const double StartAngle = -225;
    public const double Sweep = 270;

    /// <summary>Needle angle in degrees (0 = right, clockwise), sweeping 270° from bottom-left; values outside the scale are clamped.</summary>
    public static double Angle(double min, double max, double value) =>
        StartAngle + Sweep * (Math.Clamp(value, min, max) - min) / (max - min);
}
