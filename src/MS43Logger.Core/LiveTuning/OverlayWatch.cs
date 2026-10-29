using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

/// <summary>Watch selection and combined stock/log reply encoding.</summary>
public static class OverlayWatch
{
    public const int LogReplyBytes = 88, StockPayloadBytes = 73;

    /// <summary>Sets the watch to <paramref name="address"/> (page * 4000h + offset, below 80000h); 0 stops it.</summary>
    public static byte[] WatchRequest(uint address) => [0x6D, 0x0C, (byte)(address >> 16), (byte)(address >> 8), (byte)address];

    public static byte[] LogRequest => [0x6D, 0x0B];

    /// <summary>
    /// The record of an 88-byte log reply (12 58 A0, 73 stock bytes, 11 record bytes, checksum), with the stock payload in
    /// <paramref name="payload"/>; null for any other frame or a bad checksum.
    /// </summary>
    public static WatchRecord? ParseLogReply(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (frame.Length != LogReplyBytes || frame[0] != 0x12 || frame[1] != LogReplyBytes || frame[2] != 0xA0) return null;
        byte xor = 0;
        foreach (var b in frame[..^1]) xor ^= b;
        if (xor != frame[^1]) return null;
        payload = frame.Slice(3, StockPayloadBytes);
        var r = frame.Slice(3 + StockPayloadBytes, 11);
        if (r[0] > (byte)WatchKind.CurveInput) return null;
        return new WatchRecord((WatchKind)r[0], r[1], BinaryPrimitives.ReadUInt16BigEndian(r[2..]), BinaryPrimitives.ReadUInt16BigEndian(r[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(r[6..]), BinaryPrimitives.ReadUInt16BigEndian(r[8..]), r[10]);
    }
}
