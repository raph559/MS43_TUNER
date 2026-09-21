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

    /// <summary>Puts <paramref name="copy"/> live (spec 5.2); on success the map's intent is Live.</summary>
    public Task<SendResult> SendAsync(int table, byte[] copy)
    {
        var snapshot = (byte[])copy.Clone();
        return Run(() =>
        {
            // A fresh status: the last one can be a second old, from before a key cycle emptied the ECU.
            SendResult result;
            try
            {
                result = _client.Send(table, snapshot, Flash(table), _client.Status());
            }
            catch (Exception error) when (Detection?.Header?.Version is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10
                && error is TimeoutException or InvalidDataException)
            {
                Problem = "The update was interrupted. Completed groups remain applied; reconnecting will read back the active map.";
                throw;
            }
            if (result is { Refused: null, FreeFirst: null })
            {
                _sent[table] = snapshot;
                _intent[table] = MapIntent.Live;
            }
            else
            {
                Problem = Describe(table, result);
            }

            Publish(_client.Status());
            return result;
        }, fatalOnly: true);
    }

    /// <summary>The per-map switch: on with the complete copy's CRC, or off.</summary>
    public Task<OverlayReason?> SetOnAsync(int table, bool on) => Command(() =>
    {
        var refused = on ? _client.On(table, Crc(table)) : _client.Off(table);
        if (refused is null && _intent[table] != MapIntent.None)
        {
            _intent[table] = on ? MapIntent.Live : MapIntent.SentOff;
        }

        return refused;
    });

    /// <summary>The big A/B switch: every map sent goes on (all on), or off (all off).</summary>
    public Task<OverlayReason?> SetAllAsync(bool on) => Command(() =>
    {
        var refused = on ? _client.AllOn() : _client.AllOff();
        if (refused is null)
        {
            for (var table = 0; table < _intent.Length; table++)
            {
                if (_intent[table] != MapIntent.None)
                {
                    _intent[table] = on ? MapIntent.Live : MapIntent.SentOff;
                }
            }
            if (_limiterIntent is not null) _limiterIntent = _limiterIntent with { Enabled = on };
        }

        return refused;
    });

    /// <summary>Every map back to Free in the ECU; nothing is put back after it.</summary>
    public Task<OverlayReason?> RevertAsync() => Command(() =>
    {
        var refused = _client.Revert();
        if (refused is null)
        {
            Array.Fill(_intent, MapIntent.None);
            Array.Fill(_sent, null);
            _limiterIntent = null;
        }

        return refused;
    });

    public Task<OverlayReason?> FreeAsync(int table) => Command(() =>
    {
        var refused = _client.Free(table);
        if (refused is null)
        {
            _intent[table] = MapIntent.None;
            _sent[table] = null;
        }

        return refused;
    });

    public Task<OverlayReason?> SendLimiterAsync(LimiterSettings settings)
    {
        var snapshot = settings with { GearLimits = settings.GearLimits.ToArray() };
        return Command(() =>
        {
            _client.Status();
            var refused = _client.SendLimiter(snapshot);
            if (refused is null) _limiterIntent = snapshot;
            return refused;
        });
    }

    private Task<OverlayReason?> Command(Func<OverlayReason?> command) => Run(() =>
    {
        var refused = command();
        if (refused is { } reason)
        {
            Problem = $"The ECU refused: {reason}.";
        }

        Publish(_client.Status());
        return refused;
    }, fatalOnly: true);

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

    /// <summary>
    /// With the engine running, every map the session sent is brought back to what was wanted: one that is Free (a key cycle
    /// wiped it) or Loaded (a send refused or cut short) is sent again, and switched off again if it was off; a live map the
    /// ECU switched off (a stall, a silence) goes back on; a map wanted off that is on (its off was lost) goes off. A refusal
    /// drops the map's intent, so it is not tried every second.
    /// </summary>
    private OverlayStatus Restore(OverlayStatus status)
    {
        ReconcileV3(status);
        if (!status.Running)
        {
            return status;
        }

        for (var table = 0; table < _intent.Length; table++)
        {
            if (_intent[table] == MapIntent.None || _sent[table] is not { } copy)
            {
                continue;
            }

            var state = status.Tables[table].State;
            OverlayReason? refused = null;
            if (state is OverlayState.Free or OverlayState.Loaded)
            {
                var result = _client.Send(table, copy, Flash(table), status);
                refused = result.Refused ?? (result.FreeFirst is not null ? OverlayReason.Overlap : null);
                if (refused is null && _intent[table] == MapIntent.SentOff)
                {
                    refused = _client.Off(table);
                }
            }
            else if (state == OverlayState.Ready && _intent[table] == MapIntent.Live)
            {
                refused = _client.On(table, OverlayContract.Crc32(copy));
            }
            else if (state == OverlayState.On && _intent[table] == MapIntent.SentOff)
            {
                refused = _client.Off(table);
            }
            else
            {
                continue;
            }

            if (refused is { } reason)
            {
                Problem = $"{Maps[table].Key} could not be put back: {reason}.";
                _intent[table] = MapIntent.None;
            }

            status = _client.Status();
        }

        if (_limiterIntent is { } wanted && status.Limiter is { } actual
            && (!actual.Confirmed || !actual.Payload().AsSpan().SequenceEqual(wanted.Payload())))
        {
            var refused = _client.SendLimiter(wanted);
            if (refused is not null) { Problem = $"Limiter settings could not be restored: {refused}."; _limiterIntent = null; }
            status = _client.Status();
        }
        return status;
    }

    private uint Crc(int table) => _sent[table] is { } copy
        ? OverlayContract.Crc32(copy)
        : (Last ?? _client.Status()).Tables[table].ContentCrc32;

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
