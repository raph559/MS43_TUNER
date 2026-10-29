using System.Buffers.Binary;
namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for session framing introduced in V3.</summary>
public static class OverlayV3
{
    public const int Version = 3;
    public const int PatchBytes = OverlayProtocol.PatchBytes;
    public const int CopyBytes = 932;
    public const int MostWrite = OverlayProtocol.MostWrite;
    public const int MinimumPatchGapMilliseconds = OverlayProtocol.MinimumPatchGapMilliseconds;
    public static IReadOnlyList<int> Addresses { get; } = Array.AsReadOnly(new[] { 0x041D00, 0x00E6E0, 0x041E40, 0x00E704, 0x041F00 });

    public static int Offset(int table) => Addresses[table] & 0x3FFF;

    public static byte[] Open(uint session) => OverlayProtocol.Open(session);
    public static byte[] Mutation(byte[] command, uint session, uint revision) => OverlayProtocol.Mutation(command, session, revision);

    public static byte[] Patch(int table, int at, uint session, uint revision, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        if (table is < 0 or > 4 || before.Length != OverlayContract.Maps[table].Bytes || after.Length != before.Length
            || at < 0 || (at & 1) != 0 || at + PatchBytes > before.Length || session == 0 || revision == uint.MaxValue)
            throw new ArgumentException("Invalid live patch.");
        for (var i = 0; i < before.Length; i++)
            if ((i < at || i >= at + PatchBytes) && before[i] != after[i])
                throw new ArgumentException("A patch changes only its eight-cell window.");
        var result = new byte[34];
        result[0] = 0x6D; result[1] = 9;
        Put(result, 2, session); Put(result, 6, revision);
        result[10] = (byte)table;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(12), (ushort)at);
        Put(result, 14, OverlayContract.Crc32(before));
        Put(result, 18, OverlayContract.Crc32(after));
        after.Slice(at, PatchBytes).CopyTo(result.AsSpan(22));
        Put(result, 30, OverlayContract.Crc32(result.AsSpan(2, 28)));
        return result;
    }
    public static IEnumerable<int> ChangedWindows(byte[] before, byte[] after) => OverlayProtocol.ChangedWindows(before, after);

    public static OverlayStatus ParseStatus(IReadOnlyList<byte> reply)
    {
        OverlayContract.ValidateFrame(reply);
        if (reply.Count != 60 || reply[2] != 0xA0 || !reply.Skip(3).Take(5).SequenceEqual(new byte[] { 76, 84, 79, 86, 3 })
            || (reply[8] & ~1) != 0)
            throw new InvalidDataException("Invalid protocol 3 status.");
        var p = reply.Skip(3).ToArray();
        var entries = new OverlayEntry[5];
        var free = CopyBytes;
        for (var i = 0; i < 5; i++)
        {
            var at = 6 + 8 * i;
            var state = (OverlayState)p[at];
            var offset = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(at + 2));
            if (state > OverlayState.On || p[at + 1] != 0 || (state != OverlayState.Free && offset != Offset(i)))
                throw new InvalidDataException("Invalid fixed map location.");
            entries[i] = new OverlayEntry(state, offset, Get(p, at + 4));
            if (state != OverlayState.Free) free -= OverlayContract.Maps[i].Bytes;
        }
        if (BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(46)) != free)
            throw new InvalidDataException("Invalid map byte count.");
        return new OverlayStatus(3, (p[5] & 1) != 0, false, entries, free, Get(p, 48), Get(p, 52));
    }
    public static (OverlayReason? Reason, uint Revision) Response(byte[] response) => OverlayProtocol.Response(response);

    private static uint Get(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
    private static void Put(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
}
