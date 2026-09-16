using System.Diagnostics;
using System.Globalization;
using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core;

/// <summary>
/// The logger's source with the ECU's diagnosis read between two polls, for the log's columns (V10.1 plan, Task 4, as the
/// V10.1 review reshaped it): the stored faults and the task queues when logging starts and every 20 s, and on a V10 ECU
/// (<paramref name="overlayState"/>) the feature flags FDF8 every 0.5 s, and while a pops window runs (and once after it)
/// pops' own state at 0x41FBC: its lowest rpm and V10.1's count of the stabs begun. At most one DS2 06 exchange goes out between two
/// polls, never retried, and none while <paramref name="active"/> says no (no log open, or a Performance run recording):
/// the fault round is spread over polls (the queue flags, the layout check once a session, the count, the list, a record
/// each). A read that fails keeps the last value and the poll goes on; three state reads in a row with no answer wait
/// 20 s, and the state cells are empty once the last good read is a second old. The queue flags go first, so an ECU that
/// answers no memory read costs one timed-out read every 20 s. DS2 06 is the stock memory read: while the overlay is
/// quiet, it hears nothing else from here.
/// </summary>
public sealed class DiagnosticPayloadSource(IPayloadSource inner, IDs2Line line, bool overlayState, Func<bool>? active = null) : IPayloadSource
{
    public const int FaultsEveryMilliseconds = 20_000, StateEveryMilliseconds = 500, StateFreshMilliseconds = 1_000, StateTimeoutsToWait = 3;
    public const int FlagsAddress = 0xFDF8;

    /// <summary>Pops' state: POPS_LEFT (word, 10 ms ticks), POPS_MIN (the window's lowest 807F), POPS_STABS (V10.1, the stabs begun).</summary>
    public const int PopsAddress = 0x41FBC;

    public static IReadOnlyList<string> CsvHeader { get; } =
        ["faults", "queues", "fdf8", "pops_window", "pops_stabs", "pops_low_rpm", "pops_used", "blip", "guard"];

    /// <summary>The columns before any source has read anything, as wide as the header.</summary>
    public static IReadOnlyList<string> EmptyCsvValues { get; } = CsvHeader.Select(_ => string.Empty).ToArray();

    private enum Step { Queues, Layout, LayoutRest, Count, List, Record }

    private readonly object _gate = new();
    private readonly List<EcuFault> _found = new();
    private long? _faultsDue, _stateDue, _stateAt, _popsAt;
    private bool _windowSeen, _popsPending;
    private byte _popsLowest, _popsStabs;
    private int _stateTimeouts, _queueWord, _count;
    private Step? _step;
    private bool? _layout;                                        // null until checked: once a session
    private byte[] _layoutHead = [];
    private int[] _paths = [];
    private string _faults = string.Empty, _queues = string.Empty;
    private ushort? _flags;

    /// <summary>The logger's own source, polled after the read.</summary>
    public IPayloadSource Inner => inner;

    /// <summary>The stored faults' P-codes, oldest first and space-separated; "none", "unsupported" (another layout), or empty before a read.</summary>
    public string Faults { get { lock (_gate) return _faults; } }

    /// <summary>The task queues that have overflowed since key-on, space-separated; "none", "unsupported", or empty before a read.</summary>
    public string Queues { get { lock (_gate) return _queues; } }

    public long? LastRequestTimestamp { get; private set; }

    public int LastAttempts => inner.LastAttempts;

    public byte[] PollPayload()
    {
        if (active?.Invoke() ?? true) ReadOne(line.MonotonicMilliseconds);
        var sent = Stopwatch.GetTimestamp();
        var payload = inner.PollPayload();
        LastRequestTimestamp = inner.LastRequestTimestamp ?? sent;
        return payload;
    }

    /// <summary>
    /// The values of <see cref="CsvHeader"/>: FDF8 in hex, 0 or 1 for each bit, the stabs begun and the lowest rpm of the
    /// pops window (kept after it while the read is fresh), the blip 0 off, 1 rising, 2 holding.
    /// </summary>
    public IReadOnlyList<string> CsvValues()
    {
        var now = line.MonotonicMilliseconds;
        lock (_gate)
        {
            var flags = _stateAt is { } at && now - at <= StateFreshMilliseconds ? _flags : null;
            var pops = _popsAt is { } read && now - read <= StateFreshMilliseconds;
            static string Bit(ushort? word, int bit) => word is { } w ? ((w >> bit) & 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
            var blip = flags is { } f ? ((f & 1 << 8) == 0 ? "0" : (f & 1 << 12) == 0 ? "1" : "2") : string.Empty;
            return
            [
                _faults, _queues, flags is { } w ? w.ToString("X4", CultureInfo.InvariantCulture) : string.Empty, Bit(flags, 3),
                pops ? _popsStabs.ToString(CultureInfo.InvariantCulture) : string.Empty,
                pops ? (_popsLowest * 32).ToString(CultureInfo.InvariantCulture) : string.Empty,
                Bit(flags, 4), blip, Bit(flags, 13),
            ];
        }
    }

    /// <summary>One exchange at most: the state when it is due, else pops' state after a window's FDF8, else the fault round.</summary>
    private void ReadOne(long now)
    {
        if (_step is null && _layout != false && (_faultsDue is not { } due || now >= due))
        {
            _faultsDue = now + FaultsEveryMilliseconds;
            _step = Step.Queues;
        }
        if (overlayState && (_stateDue is not { } state || now >= state)) ReadState(now);
        else if (_popsPending) ReadPops(now);
        else if (_step is not null) FaultStep();
    }

    private void ReadPops(long now)
    {
        _popsPending = false;
        try
        {
            var pops = ReadOnce(PopsAddress, 4);
            lock (_gate) (_popsLowest, _popsStabs, _popsAt) = (pops[2], pops[3], now);
        }
        catch (Exception error) when (Failed(error))
        {
        }
    }

    private void ReadState(long now)
    {
        _stateDue = now + StateEveryMilliseconds;
        try
        {
            var flags = ReadOnce(FlagsAddress, 2);
            var word = (ushort)(flags[0] | flags[1] << 8);
            lock (_gate) (_flags, _stateAt) = (word, now);
            _stateTimeouts = 0;
            var window = (word & 1 << 3) != 0;
            _popsPending = window || _windowSeen;                // during a window, and once after it for its final count
            _windowSeen = window;
        }
        catch (TimeoutException)
        {
            if (++_stateTimeouts < StateTimeoutsToWait) return;
            _stateTimeouts = 0;
            _stateDue = now + FaultsEveryMilliseconds;           // three in a row with no answer: wait for the next round
        }
        catch (Exception error) when (Failed(error))
        {
        }
    }

    private void FaultStep()
    {
        try
        {
            switch (_step)
            {
                case Step.Queues:
                    var flags = ReadOnce(FaultMemory.QueueFlagsAddress, 2);
                    _queueWord = flags[0] | flags[1] << 8;
                    _step = _layout == true ? Step.Count : Step.Layout;
                    if (_layout == true) PublishQueues();
                    break;
                case Step.Layout:
                    _layoutHead = ReadOnce(FaultMemory.DescriptorsAddress, Ds2Memory.MostRead);
                    _step = Step.LayoutRest;
                    break;
                case Step.LayoutRest:
                    var rest = ReadOnce(FaultMemory.DescriptorsAddress + Ds2Memory.MostRead, FaultMemory.LayoutBytes - Ds2Memory.MostRead);
                    _layout = FaultMemory.MatchesLayout([.. _layoutHead, .. rest]);
                    if (_layout == true)
                    {
                        PublishQueues();
                        _step = Step.Count;
                    }
                    else
                    {
                        lock (_gate) (_faults, _queues) = ("unsupported", "unsupported");   // FD84 is MS43X001's too
                        _step = null;
                    }
                    break;
                case Step.Count:
                    _count = ReadOnce(FaultMemory.StoredCountAddress, 1)[0];
                    if (_count == 0) PublishFaults();
                    else _step = Step.List;
                    break;
                case Step.List:
                    var list = ReadOnce(FaultMemory.StoredListAddress, Math.Min(_count, Math.Min(FaultPaths.All.Count, Ds2Memory.MostRead)));
                    _paths = list.Select(b => (int)b).Where(path => path < FaultPaths.All.Count).Distinct().ToArray();
                    _found.Clear();
                    if (_paths.Length == 0) PublishFaults();
                    else _step = Step.Record;
                    break;
                case Step.Record:
                    var path = _paths[_found.Count];
                    _found.Add(FaultMemory.Decode(path, ReadOnce(FaultMemory.RecordsAddress + FaultMemory.RecordSize * path, FaultMemory.RecordSize)));
                    if (_found.Count == _paths.Length) PublishFaults();
                    break;
            }
        }
        catch (Exception error) when (Failed(error))
        {
            _step = null;                                        // the round ends here; the values stay, the next is on time
        }
    }

    private void PublishQueues()
    {
        var queues = FaultMemory.QueueOverflows(_queueWord);
        var text = queues.Count == 0 ? "none" : string.Join(" ", queues);
        lock (_gate) _queues = text;
    }

    private void PublishFaults()
    {
        // 20 paths have no P-code (P0000): their name stands in, so they can be told apart.
        var codes = _found.Count == 0 ? "none" : string.Join(" ", _found.Select(fault => fault.PCode != "P0000" ? fault.PCode : fault.Name ?? fault.PCode));
        lock (_gate) _faults = codes;
        _found.Clear();
        _step = null;
    }

    /// <summary>One DS2 06 read of <paramref name="count"/> bytes, not retried: these columns wait for their next turn instead.</summary>
    private byte[] ReadOnce(int address, int count)
    {
        var reply = line.Request([0x06, 0x00, (byte)(address >> 16), (byte)(address >> 8), (byte)address, (byte)count]);
        OverlayContract.ValidateFrame(reply);
        if (reply.Length < 4 || reply[2] != 0xA0) throw new Ds2RefusedException(address, reply.Length > 2 ? reply[2] : (byte)0);
        if (reply.Length - 4 != count) throw new InvalidDataException($"The ECU answered {reply.Length - 4} bytes to a read of {count} at {address:X6}.");
        return reply[3..^1];
    }

    private static bool Failed(Exception error) => error is TimeoutException or InvalidDataException or IOException or Ds2RefusedException;
}
