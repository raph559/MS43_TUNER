namespace MS43Logger.Core.LiveTuning;

/// <summary>What the app wants of a map in the ECU: nothing, sent and switched off, or live.</summary>
public enum MapIntent
{
    None,
    SentOff,
    Live,
}

/// <summary>
/// The app's side of a live-tuning session on one connection (live tuning spec 5.4): it detects the overlay, keeps it alive
/// with a status on every <see cref="TickAsync"/>, remembers what was sent and what was live, and puts it back after a key
/// cycle or a stall once the engine runs. Maps it finds in the ECU that it did not send are read back and adopted. Requests
/// run one at a time, off the caller's thread. A timeout is not fatal (the link may be reconnecting); any other error stops
/// the session, and it sends nothing more, and so does a reply that is not the overlay's (a late or garbled frame).
/// The session outlives its line: after the app reconnects (a key cycle) it is rebound to the new line, detects again, and
/// keeps what it had sent as long as the ECU's flashed maps are the same.
/// </summary>
public sealed class LiveTuningSession(IDs2Line line)
{
    private OverlayClient _client = new(line);
    private IDs2Line _line = line;
    private IReadOnlyList<byte[]>? _knownFlash;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MapIntent[] _intent = new MapIntent[5];
    private byte[]?[] _sent = new byte[5][];
    private LimiterSettings? _limiterIntent;
    private LimiterSettings? _limiterFlash;
    private bool _adopted;

    public event EventHandler<OverlayStatus>? StatusChanged;

    public event EventHandler<Exception>? Stopped;

    public OverlayDetection? Detection { get; private set; }
    public IReadOnlyList<OverlayMap> Maps => Detection?.Catalogue ?? OverlayContract.Maps;

    public OverlayStatus? Last { get; private set; }

    public bool IsStopped { get; private set; }

    /// <summary>The last thing that went wrong, for the page to show; null when nothing has.</summary>
    public string? Problem { get; private set; }

    /// <summary>Moves the session to a new line (the app reconnected): it must detect again before anything is sent.</summary>
    public void Rebind(IDs2Line line)
    {
        _client = new OverlayClient(line);
        _line = line;
        Detection = null;
        Last = null;
        _adopted = false;
    }

    public MapIntent Intent(int table) => _intent[table];

    /// <summary>The copy the ECU holds (or held before a key cycle) for this map, as sent or read back.</summary>
    public byte[]? SentCopy(int table) => _sent[table] is { } copy ? (byte[])copy.Clone() : null;

    /// <summary>Detects the overlay (reads only). Without it, the session sends nothing, ever.</summary>
    public Task<OverlayDetection> StartAsync() => Run(() =>
    {
        var detection = _client.Detect();
        if (detection.Problem is null)
        {
            // Another bin flashed since: what was sent belongs to other maps.
            if (_knownFlash is { } known && (known.Count != detection.Flash.Count || !known.Zip(detection.Flash).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second))))
            {
                Array.Fill(_intent, MapIntent.None);
                Array.Fill(_sent, null);
            }

            _knownFlash = detection.Flash;
            if (_intent.Length != detection.Flash.Count)
            {
                _intent = new MapIntent[detection.Flash.Count];
                _sent = new byte[detection.Flash.Count][];
            }
            if (_limiterFlash is not null && (detection.LimiterFlash is null || !_limiterFlash.Payload().AsSpan().SequenceEqual(detection.LimiterFlash.Payload())))
                _limiterIntent = null;
            _limiterFlash = detection.LimiterFlash;
        }

        return Detection = detection;
    }, fatalOnly: false);

    private void Adopt(OverlayStatus status)
    {
        if (_limiterIntent is null && status.Limiter is { Confirmed: true } limiter)
            _limiterIntent = limiter with { GearLimits = limiter.GearLimits.ToArray() };
        for (var table = 0; table < _intent.Length; table++)
        {
            var entry = status.Tables[table];
            if (entry.State == OverlayState.Free || _intent[table] != MapIntent.None)
            {
                continue;
            }

            var copy = _client.ReadCopy(table, entry);
            if (entry.State is OverlayState.Ready or OverlayState.On
                && OverlayContract.Crc32(copy) != entry.ContentCrc32)
            {
                throw new InvalidDataException("The ECU's table content does not match its verified CRC.");
            }

            _sent[table] = copy;
            _intent[table] = entry.State == OverlayState.On ? MapIntent.Live : MapIntent.SentOff;
        }
    }

    private byte[] Flash(int table) => Detection?.Flash is { } flash
        ? flash[table]
        : throw new InvalidOperationException("No live-tuning overlay was detected on this ECU.");

    private string Describe(int table, SendResult result) => result.FreeFirst is { } free
        ? $"{Maps[table].Key} does not fit in the ECU: remove {Maps[free].Key} first."
        : result.Refused == OverlayReason.Overlap ? $"{Maps[table].Key} does not fit. Remove loaded maps to release a slot and a large enough memory block."
        : $"The ECU refused {Maps[table].Key}: {result.Refused}.";

    private void Publish(OverlayStatus status)
    {
        ReconcileV3(status);
        Last = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ReconcileV3(OverlayStatus status)
    {
        if (status.Version is not (3 or 4 or 5 or 6 or 7 or 8 or 9 or 10)) return;
        for (var table = 0; table < _intent.Length; table++)
        {
            var entry = status.Tables[table];
            if (entry.State is not (OverlayState.On or OverlayState.Ready) || _intent[table] == MapIntent.None
                || (_sent[table] is { } known && OverlayContract.Crc32(known) == entry.ContentCrc32)) continue;
            var actual = _client.ReadCopy(table, entry);
            if (OverlayContract.Crc32(actual) != entry.ContentCrc32)
                throw new InvalidDataException("The active map could not be reconciled after an interrupted update.");
            _sent[table] = actual;
        }
    }

    private async Task<T> Run<T>(Func<T> work, bool fatalOnly)
    {
        if (IsStopped)
        {
            throw new InvalidOperationException("The live-tuning session has stopped: " + Problem);
        }

        if (fatalOnly && Detection?.Problem is not null)
        {
            throw new InvalidOperationException("No live-tuning overlay was detected on this ECU.");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not TimeoutException and not InvalidOperationException and not InvalidDataException)
        {
            IsStopped = true;
            Problem = error.Message;
            Stopped?.Invoke(this, error);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
