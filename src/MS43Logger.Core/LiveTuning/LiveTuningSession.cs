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
    private bool _adopted;

    public event EventHandler<Exception>? Stopped;

    public bool IsStopped { get; private set; }

    /// <summary>The last thing that went wrong, for the page to show; null when nothing has.</summary>
    public string? Problem { get; private set; }

    public MapIntent Intent(int table) => _intent[table];

    /// <summary>The copy the ECU holds (or held before a key cycle) for this map, as sent or read back.</summary>
    public byte[]? SentCopy(int table) => _sent[table] is { } copy ? (byte[])copy.Clone() : null;
}
