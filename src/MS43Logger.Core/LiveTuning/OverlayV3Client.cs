using System.Security.Cryptography;

namespace MS43Logger.Core.LiveTuning;

internal sealed class OverlayV3Client(IDs2Line line)
{
    private uint _session;
    private uint _revision;
    private readonly byte[]?[] _copies = new byte[5][];
    private long? _lastPatchEnded;

    public OverlayStatus Status()
    {
        var status = OverlayV3.ParseStatus(line.Request(OverlayContract.Status()));
        if (status.Session == 0 && status.Tables.All(t => t.State == OverlayState.Free))
        {
            _session = 0;
            Array.Clear(_copies);
        }
        if (_session != 0 && status.Session != _session)
            throw new InvalidDataException("The ECU's live session changed. Reconnect before sending another edit.");
        _revision = status.Revision;
        return status;
    }

    public OverlayReason? Run(byte[] command)
    {
        EnsureSession();
        if (_revision == uint.MaxValue) throw new InvalidOperationException("Live revision exhausted; reconnect before editing.");
        var result = OverlayV3.Response(line.Request(OverlayV3.Mutation(command, _session, _revision)));
        if (result.Reason is null)
        {
            if (result.Revision != _revision + 1) throw new InvalidDataException("Unexpected live revision acknowledgement.");
            _revision = result.Revision;
        }
        return result.Reason;
    }

    public SendResult Send(int table, byte[] desired, byte[] flash, OverlayStatus status)
    {
        EnsureSession();
        var entry = status.Tables[table];
        if (entry.State == OverlayState.On)
        {
            var current = _copies[table];
            if (current is null || OverlayContract.Crc32(current) != entry.ContentCrc32)
                current = ReadVerified(table, entry);
            foreach (var at in OverlayV3.ChangedWindows(current, desired).ToArray())
            {
                var next = current.ToArray();
                desired.AsSpan(at, OverlayV3.PatchBytes).CopyTo(next.AsSpan(at));
                var packet = OverlayV3.Patch(table, at, _session, _revision, current, next);
                (OverlayReason? Reason, uint Revision) result;
                try
                {
                    result = OverlayV3.Response(RequestPatch(packet));
                }
                catch (Exception error) when (error is TimeoutException or InvalidDataException)
                {
                    // A lost acknowledgement must repeat the same revision and bytes, never a new operation.
                    _copies[table] = null;
                    result = OverlayV3.Response(RequestPatch(packet));
                }
                if (result.Reason is { } reason) { _copies[table] = null; return new SendResult(reason, null); }
                if (result.Revision != _revision + 1)
                {
                    _copies[table] = null;
                    throw new InvalidDataException("Unexpected patch revision acknowledgement.");
                }
                _revision = result.Revision;
                current = next;
                _copies[table] = current;
            }
            return new SendResult(null, null);
        }

        if (Run(OverlayContract.Load(table, OverlayV3.Offset(table))) is { } load) return new SendResult(load, null);
        for (var at = 0; at < desired.Length;)
        {
            if (desired[at] == flash[at]) { at++; continue; }
            var end = at;
            while (end < desired.Length && desired[end] != flash[end] && end - at < OverlayV3.MostWrite) end++;
            if (Run(OverlayContract.Write(table, at, desired.AsSpan(at, end - at))) is { } write) return new SendResult(write, null);
            at = end;
        }
        if (!line.Read(OverlayV3.Addresses[table], desired.Length).AsSpan().SequenceEqual(desired))
            return new SendResult(OverlayReason.Integrity, null);
        var on = Run(OverlayContract.On(table, OverlayContract.Crc32(desired)));
        if (on is null) _copies[table] = desired.ToArray();
        return new SendResult(on, null);
    }

    private byte[] ReadVerified(int table, OverlayEntry entry)
    {
        var data = line.Read(OverlayV3.Addresses[table], OverlayContract.Maps[table].Bytes);
        if (OverlayContract.Crc32(data) != entry.ContentCrc32)
            throw new InvalidDataException("The active map differs from its confirmed CRC; no patch was sent.");
        return _copies[table] = data;
    }

    private byte[] RequestPatch(byte[] packet)
    {
        if (_lastPatchEnded is { } previous)
        {
            var remaining = OverlayV3.MinimumPatchGapMilliseconds - (line.MonotonicMilliseconds - previous);
            if (remaining > 0) line.WaitMilliseconds((int)remaining);
        }
        try { return line.Request(packet); }
        finally { _lastPatchEnded = line.MonotonicMilliseconds; }
    }

    private void EnsureSession()
    {
        if (_session != 0) return;
        uint session;
        do { session = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (session == 0);
        var result = OverlayV3.Response(line.Request(OverlayV3.Open(session)));
        if (result.Reason is not null || result.Revision != 0)
            throw new InvalidDataException("The ECU did not open the requested live session.");
        _session = session;
        _revision = 0;
    }
}
