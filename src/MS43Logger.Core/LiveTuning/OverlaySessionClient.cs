using System.Security.Cryptography;

namespace MS43Logger.Core.LiveTuning;

internal sealed class OverlayV4Client(IDs2Line line, int version = 4)
{
    private uint _session, _revision;
    private readonly byte[]?[] _copies = new byte[(version >= 5 ? OverlayV5.Catalogue(version) : OverlayV4.Maps).Count][];
    private long? _lastPatchEnded;

    public OverlayStatus Status()
    {
        var status = version is 5 or 6 or 7 or 8 or 9 or 10 ? OverlayV5.ReadStatus(line, version) : OverlayV4.ParseStatus(line.Request(OverlayContract.Status()));
        if (status.Session == 0 && status.Tables.All(t => t.State == OverlayState.Free))
        {
            _session = 0; Array.Clear(_copies);
        }
        if (_session != 0 && status.Session != _session) throw new InvalidDataException("The ECU's live session changed. Reconnect before editing.");
        _revision = status.Revision; return status;
    }

    public OverlayReason? Run(byte[] command)
    {
        EnsureSession();
        if (_revision == uint.MaxValue) throw new InvalidOperationException("Live revision exhausted; reconnect before editing.");
        var session = _session; var revision = _revision;
        var packet = OverlayV3.Mutation(command, session, revision);
        try { return Accept(OverlayV3.Response(line.Request(packet))); }
        catch (Exception error) when (error is TimeoutException or InvalidDataException)
        {
            // A missing reply can mean either a lost request or a completed command. Read the
            // revision and the command's result before deciding whether it is safe to repeat.
            var status = Status();
            if (status.Session != session) throw new InvalidDataException("The ECU's live session changed during the command.");
            if (status.Revision == revision)
                return Accept(OverlayV3.Response(line.Request(packet)));
            if (status.Revision == revision + 1 && HasResult(command, status)) return null;
            throw new InvalidDataException("The interrupted live command could not be confirmed. Reconnect to read the ECU's state.");
        }
    }

    private bool HasResult(byte[] command, OverlayStatus status)
    {
        var entry = command.Length > 2 ? status.Tables[command[2]] : null;
        static int Word(byte[] b, int at) => b[at] << 8 | b[at + 1];
        return command[1] switch
        {
            1 => entry!.State == OverlayState.Loaded && entry.Offset == Word(command, 3) && entry.ContentCrc32 == 0
                && (version < 5 || (command[5] == 255 ? entry.DirectorySlot < 5 : entry.DirectorySlot == 5 + command[5])),
            2 => entry!.State == OverlayState.Loaded
                && line.Read(OverlayV4.Physical(entry.Offset) + Word(command, 3), command.Length - 5).AsSpan().SequenceEqual(command.AsSpan(5)),
            3 => entry!.State == OverlayState.On && entry.ContentCrc32 == System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(command.AsSpan(3)),
            4 => entry!.State is OverlayState.Ready or OverlayState.Loaded,
            5 => status.Tables.All(e => e.State != OverlayState.Ready) && (status.Limiter is not { Confirmed: true } || status.Limiter.Enabled),
            6 => status.Tables.All(e => e.State != OverlayState.On) && status.Limiter is not { Enabled: true },
            7 => entry!.State == OverlayState.Free,
            8 => status.Tables.All(e => e.State == OverlayState.Free) && status.Limiter is not { Confirmed: true },
            _ => false,
        };
    }

    public SendResult Send(int table, byte[] desired, byte[] flash, OverlayStatus status)
    {
        EnsureSession();
        var entry = status.Tables[table];
        if (entry.State == OverlayState.On)
        {
            var current = _copies[table];
            if (current is null || OverlayContract.Crc32(current) != entry.ContentCrc32)
            {
                current = line.Read(OverlayV4.Physical(entry.Offset), desired.Length);
                if (OverlayContract.Crc32(current) != entry.ContentCrc32) throw new InvalidDataException("The active map differs from its confirmed CRC.");
            }
            foreach (var at in OverlayV3.ChangedWindows(current, desired).ToArray())
            {
                var next = current.ToArray(); desired.AsSpan(at, 8).CopyTo(next.AsSpan(at));
                try
                {
                    if (Patch(OverlayV4.Patch(table, at, _session, _revision, next.AsSpan(at, 8), version >= 5 ? OverlayV5.Catalogue(version) : OverlayV4.Maps)) is { } reason)
                    {
                        _copies[table] = null; return new(reason, null);
                    }
                }
                catch { _copies[table] = null; throw; }
                current = next; _copies[table] = current;
            }
            return new(null, null);
        }
        var placement = version is 5 or 6 or 7 or 8 or 9 or 10 ? OverlayV5.Place(table, status) : null;
        var plannedOffset = version is 5 or 6 or 7 or 8 or 9 or 10 ? placement?.Offset : OverlayV4.Place(table, status);
        if (plannedOffset is not { } offset) return new(OverlayReason.Overlap, CopyAreaPlanner.FreeFirst(table, status));
        if (Run(version is 5 or 6 or 7 or 8 or 9 or 10 ? OverlayV5.Load(table, placement!) : OverlayContract.Load(table, offset)) is { } load) return new(load, null);
        // The firmware takes an inactive write to a settings group only as the whole group (V6 and later).
        var wholeGroup = version >= 6 && OverlayV5.Catalogue(version)[table].ScalarGroup is not null;
        if (wholeGroup && Run(OverlayContract.Write(table, 0, desired)) is { } group) return new(group, null);
        for (var at = 0; !wholeGroup && at < desired.Length;)
        {
            if (desired[at] == flash[at]) { at++; continue; }
            var end = at;
            while (end < desired.Length && desired[end] != flash[end] && end - at < OverlayV3.MostWrite) end++;
            if (Run(OverlayContract.Write(table, at, desired.AsSpan(at, end - at))) is { } write) return new(write, null);
            at = end;
        }
        if (!line.Read(OverlayV4.Physical(offset), desired.Length).AsSpan().SequenceEqual(desired)) return new(OverlayReason.Integrity, null);
        var on = Run(OverlayContract.On(table, OverlayContract.Crc32(desired)));
        if (on is null) _copies[table] = desired.ToArray();
        return new(on, null);
    }

    public OverlayReason? SendLimiter(LimiterSettings settings)
    {
        var bytes = settings.Payload(); EnsureSession();
        var result = Patch(OverlayV4.Patch(255, 0, _session, _revision, bytes));
        if (result is not null) return result;
        var actual = line.Read(OverlayV4.LimiterAddress, 8); actual[7] &= 0x81;
        return actual.AsSpan().SequenceEqual(bytes) ? null : OverlayReason.Integrity;
    }

    private OverlayReason? Patch(byte[] packet)
    {
        (OverlayReason? Reason, uint Revision) reply;
        try { reply = OverlayV3.Response(RequestPatch(packet)); }
        catch (Exception error) when (error is TimeoutException or InvalidDataException)
        {
            reply = OverlayV3.Response(RequestPatch(packet));
        }
        return Accept(reply);
    }

    private OverlayReason? Accept((OverlayReason? Reason, uint Revision) reply)
    {
        if (reply.Reason is null)
        {
            if (reply.Revision != _revision + 1) throw new InvalidDataException("Unexpected live revision acknowledgement.");
            _revision = reply.Revision;
        }
        return reply.Reason;
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
        uint token;
        do { token = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (token == 0);
        var result = OverlayV3.Response(line.Request(OverlayV3.Open(token)));
        if (result.Reason is not null || result.Revision != 0) throw new InvalidDataException("The ECU did not open the requested live session.");
        _session = token; _revision = 0;
    }
}

/// <summary>The current overlay's live session: its status, its session-stamped commands, the active-map patches and the limiter.</summary>
internal sealed class OverlaySessionClient(IDs2Line line)
{
    private uint _session, _revision;
    private long? _lastPatchEnded;

    private bool HasResult(byte[] command, OverlayStatus status)
    {
        var entry = command.Length > 2 ? status.Tables[command[2]] : null;
        static int Word(byte[] b, int at) => b[at] << 8 | b[at + 1];
        return command[1] switch
        {
            1 => entry!.State == OverlayState.Loaded && entry.Offset == Word(command, 3) && entry.ContentCrc32 == 0
                && (command[5] == 255 ? entry.DirectorySlot < 5 : entry.DirectorySlot == 5 + command[5]),
            2 => entry!.State == OverlayState.Loaded
                && line.Read(OverlayMemory.Physical(entry.Offset) + Word(command, 3), command.Length - 5).AsSpan().SequenceEqual(command.AsSpan(5)),
            3 => entry!.State == OverlayState.On && entry.ContentCrc32 == System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(command.AsSpan(3)),
            4 => entry!.State is OverlayState.Ready or OverlayState.Loaded,
            5 => status.Tables.All(e => e.State != OverlayState.Ready) && (status.Limiter is not { Confirmed: true } || status.Limiter.Enabled),
            6 => status.Tables.All(e => e.State != OverlayState.On) && status.Limiter is not { Enabled: true },
            7 => entry!.State == OverlayState.Free,
            8 => status.Tables.All(e => e.State == OverlayState.Free) && status.Limiter is not { Confirmed: true },
            _ => false,
        };
    }

    private OverlayReason? Accept((OverlayReason? Reason, uint Revision) reply)
    {
        if (reply.Reason is null)
        {
            if (reply.Revision != _revision + 1) throw new InvalidDataException("Unexpected live revision acknowledgement.");
            _revision = reply.Revision;
        }
        return reply.Reason;
    }

    private byte[] RequestPatch(byte[] packet)
    {
        if (_lastPatchEnded is { } previous)
        {
            var remaining = OverlayProtocol.MinimumPatchGapMilliseconds - (line.MonotonicMilliseconds - previous);
            if (remaining > 0) line.WaitMilliseconds((int)remaining);
        }
        try { return line.Request(packet); }
        finally { _lastPatchEnded = line.MonotonicMilliseconds; }
    }

    private void EnsureSession()
    {
        if (_session != 0) return;
        uint token;
        do { token = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (token == 0);
        var result = OverlayProtocol.Response(line.Request(OverlayProtocol.Open(token)));
        if (result.Reason is not null || result.Revision != 0) throw new InvalidDataException("The ECU did not open the requested live session.");
        _session = token; _revision = 0;
    }
}
