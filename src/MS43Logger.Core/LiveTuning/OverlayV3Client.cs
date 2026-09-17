using System.Security.Cryptography;

namespace MS43Logger.Core.LiveTuning;

internal sealed class OverlayV3Client(IDs2Line line)
{
    private uint _session;
    private uint _revision;
    private readonly byte[]?[] _copies = new byte[5][];
    private long? _lastPatchEnded;

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
