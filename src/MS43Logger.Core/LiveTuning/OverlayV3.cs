using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

public static class OverlayV3
{
    public const int Version = 3;
    public const int PatchBytes = 8;
    public const int CopyBytes = 932;
    public const int MostWrite = 112;
    public const int MinimumPatchGapMilliseconds = 100;
    public static IReadOnlyList<int> Addresses { get; } = Array.AsReadOnly(new[] { 0x041D00, 0x00E6E0, 0x041E40, 0x00E704, 0x041F00 });

    public static int Offset(int table) => Addresses[table] & 0x3FFF;

    public static byte[] Open(uint session)
    {
        if (session == 0) throw new ArgumentOutOfRangeException(nameof(session));
        var bytes = new byte[6] { 0x6D, 10, 0, 0, 0, 0 };
        Put(bytes, 2, session);
        return bytes;
    }

    public static byte[] Mutation(byte[] command, uint session, uint revision)
    {
        if (command.Length < 2 || command[0] != 0x6D || command[1] is < 1 or > 8 || session == 0)
            throw new ArgumentException("Invalid session mutation.");
        var result = new byte[command.Length + 8];
        command.AsSpan(0, 2).CopyTo(result);
        Put(result, 2, session);
        Put(result, 6, revision);
        command.AsSpan(2).CopyTo(result.AsSpan(10));
        return result;
    }

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

    public static IEnumerable<int> ChangedWindows(byte[] before, byte[] after)
    {
        if (before.Length != after.Length || before.Length < PatchBytes || (before.Length & 1) != 0)
            throw new ArgumentException("Invalid table sizes.");
        for (var i = 0; i < before.Length;)
        {
            if (before[i] == after[i]) { i++; continue; }
            var at = Math.Min(i / PatchBytes * PatchBytes, before.Length - PatchBytes);
            yield return at;
            i = at + PatchBytes;
        }
    }

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

    public static (OverlayReason? Reason, uint Revision) Response(byte[] response)
    {
        OverlayContract.ValidateFrame(response);
        if (response.Length == 8 && response[2] == 0xA0) return (null, Get(response, 3));
        return (OverlayContract.Refusal(response), 0);
    }

    private static uint Get(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
    private static void Put(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
}
