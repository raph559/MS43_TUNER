using System.Diagnostics;

namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// The logger's poll on a V9 ECU: sub 11 (<c>6D 0B</c>) instead of <c>0B B0</c>, the same 73-byte payload, plus the watch
/// record. A watch asked for goes before the next poll and again before every poll until the ECU acknowledges it: the
/// first request after the connect's speed switch can be lost. Each record carries the address that was acknowledged
/// when its poll went out, so a record of the map before never shows on the map after (RF4).
/// While <see cref="Quiet"/>, it polls with <paramref name="stock"/> (<c>0B B0</c>) and sends the overlay nothing: every
/// <c>6D</c> frame is the overlay's keep-alive, and V8's rule is that live settings go back to flash 3 s after it hears none.
/// </summary>
public sealed class WatchingPayloadSource(IDs2Line line, IPayloadSource? stock = null) : IPayloadSource, IWatchTarget
{
    private readonly object _gate = new();
    private bool _pending;
    private uint _asked;
    private uint? _watched;

    public long? LastRequestTimestamp { get; private set; }

    /// <summary>The record of the last log reply; null before any.</summary>
    public WatchRecord? LastRecord { get; private set; }

    /// <summary>The address acknowledged when the last log request went out; null without a watch.</summary>
    public uint? LastRecordWatched { get; private set; }

    public uint? Watched { get { lock (_gate) return _watched; } }

    /// <summary>Set when the live-tuning session has stopped: the overlay must hear nothing, so its 3 s fallback runs.</summary>
    public bool Quiet { get; set; }

    public int LastAttempts => Quiet && stock is not null ? stock.LastAttempts : 1;

    public void SetWatch(uint? address)
    {
        lock (_gate)
        {
            _asked = address ?? 0;
            _pending = true;
        }
    }

    public byte[] PollPayload()
    {
        if (Quiet && stock is not null)
        {
            LastRecord = null;
            LastRecordWatched = null;
            var stockPayload = stock.PollPayload();
            LastRequestTimestamp = stock.LastRequestTimestamp ?? Stopwatch.GetTimestamp();
            return stockPayload;
        }

        uint ask; bool pending;
        lock (_gate) (ask, pending) = (_asked, _pending);
        if (pending) SendWatch(ask);

        uint? watched;
        lock (_gate) watched = _watched;
        var sent = Stopwatch.GetTimestamp();
        var frame = line.Request(OverlayV9.LogRequest);
        var record = OverlayV9.ParseLogReply(frame, out var payload)
            ?? throw new InvalidDataException("The V9 log reply is not an 88-byte frame with a valid checksum.");
        LastRequestTimestamp = sent;
        LastRecord = record;
        LastRecordWatched = watched;
        return payload.ToArray();
    }

    private void SendWatch(uint address)
    {
        byte[] reply;
        try
        {
            reply = line.Request(OverlayV9.WatchRequest(address));
        }
        catch (TimeoutException)
        {
            return;                                              // still pending: it goes again before the next poll
        }
        if (reply is not [0x12, 7, 0xA0, var a2, var a1, var a0, _] || (uint)(a2 << 16 | a1 << 8 | a0) != address) return;
        lock (_gate)
        {
            if (_asked != address) return;                       // a newer watch was asked for meanwhile: that one goes next
            _pending = false;
            _watched = address == 0 ? null : address;
        }
    }
}
