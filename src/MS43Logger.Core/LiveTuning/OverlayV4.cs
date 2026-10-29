using System.Buffers.Binary;
namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for the original catalogue, storage geometry and patch encoding.</summary>
public static class OverlayV4
{
    public const int Version = 4, CopyBytes = 932, MaxSlots = 5, LimiterAddress = 0x41FD8;
    public static IReadOnlyList<OverlayMap> Maps => OverlayCatalogue.BaseMaps;
    public static int Physical(int offset) => OverlayMemory.Physical(offset);
    public static bool Fits(int offset, int bytes) => OverlayMemory.FitsStorage(offset, bytes);
    public static byte[] Patch(int table, int at, uint session, uint revision, ReadOnlySpan<byte> eightBytes, IReadOnlyList<OverlayMap>? catalogue = null) =>
        OverlayProtocol.Patch(table, at, session, revision, eightBytes, catalogue);

    public static OverlayStatus ParseStatus(IReadOnlyList<byte> reply)
    {
        OverlayContract.ValidateFrame(reply);
        if (reply.Count != 68 || reply[2] != 0xA0 || !reply.Skip(3).Take(5).SequenceEqual(new byte[] { 76, 84, 79, 86, 4 })
            || (reply[8] & ~1) != 0) throw new InvalidDataException("Invalid protocol 4 status.");
        var p = reply.Skip(3).ToArray();
        var entries = Maps.Select(_ => new OverlayEntry(OverlayState.Free, 0, 0)).ToArray();
        var free = CopyBytes;
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            var at = 6 + slot * 8; var state = (OverlayState)p[at]; var id = p[at + 1];
            var offset = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(at + 2)); var crc = Get(p, at + 4);
            if (state == OverlayState.Free)
            {
                if (offset != 0 || crc != 0) throw new InvalidDataException("Invalid free allocation.");
                continue;
            }
            if (state > OverlayState.On || id >= Maps.Count || entries[id].State != OverlayState.Free || !Fits(offset, Maps[id].Bytes))
                throw new InvalidDataException("Invalid live allocation.");
            foreach (var m in Maps.Where(m => entries[m.Index].State != OverlayState.Free))
                if (offset < entries[m.Index].Offset + m.Bytes && entries[m.Index].Offset < offset + Maps[id].Bytes)
                    throw new InvalidDataException("Overlapping live allocations.");
            entries[id] = new(state, offset, crc); free -= Maps[id].Bytes;
        }
        if (BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(46)) != free) throw new InvalidDataException("Invalid free-byte count.");
        return new(4, (p[5] & 1) != 0, false, entries, free, Get(p, 48), Get(p, 52), LimiterSettings.Read(p.AsSpan(56, 8)));
    }

    public static int? Place(int table, OverlayStatus status)
    {
        if (status.Tables[table].State != OverlayState.Free) return status.Tables[table].Offset;
        if (status.Tables.Count(e => e.State != OverlayState.Free) >= MaxSlots) return null;
        return Gaps(status).Where(g => g.Bytes >= Maps[table].Bytes).OrderBy(g => g.Bytes).ThenBy(g => g.Start)
            .Select(g => (int?)g.Start).FirstOrDefault();
    }

    public static int LargestBlock(OverlayStatus status) => Gaps(status).Select(g => g.Bytes).DefaultIfEmpty(0).Max();

    private static IEnumerable<(int Start, int Bytes)> Gaps(OverlayStatus status)
    {
        foreach (var (start, end) in new[] { (0, 704), (704, CopyBytes) })
        {
            var cursor = start;
            foreach (var m in Maps.Where(m => status.Tables[m.Index].State != OverlayState.Free
                && status.Tables[m.Index].Offset >= start && status.Tables[m.Index].Offset < end).OrderBy(m => status.Tables[m.Index].Offset))
            {
                var offset = status.Tables[m.Index].Offset;
                if (offset > cursor) yield return (cursor, offset - cursor);
                cursor = offset + m.Bytes;
            }
            if (cursor < end) yield return (cursor, end - cursor);
        }
    }

    private static uint Get(byte[] p, int at) => BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(at));
}
