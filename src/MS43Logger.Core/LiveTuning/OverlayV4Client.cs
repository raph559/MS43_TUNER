using System.Security.Cryptography;

namespace MS43Logger.Core.LiveTuning;

internal sealed class OverlayV4Client(IDs2Line line, int version = 4)
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
