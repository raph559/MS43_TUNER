using System.Buffers.Binary;

namespace MS43Logger.Core.LiveTuning;

public sealed record OverlayPlacement(int Offset, int DirectorySlot);

public static class OverlayV5
{
    public const int Version = 5, BaseSlots = 5, RecordBytes = 8, PoolBStart = 704, PoolBBytes = 228;
    public const int CopyBytes = PoolBStart + PoolBBytes;
    public const int PoolBAddress = 0xE6E0;
    public const int IndexBytes = 16, IndexOffset = CopyBytes - IndexBytes;
    public static int LookupBytes(int version) => version >= OverlayV7.Version ? OverlayV7.IndexBytes : IndexBytes;

    /// <summary>
    /// Where pool A ends for allocations: V8 keeps its last four bytes for the pops state, V9 24 with the watch record,
    /// V10 36 with its stab and blip state.
    /// </summary>
    public static int PoolAEnd(int version) => version >= OverlayV10.Version ? OverlayV10.PoolABytes
        : version >= OverlayV9.Version ? OverlayV9.PoolABytes
        : version >= OverlayV8.Version ? OverlayV8.PoolABytes : PoolBStart;

    /// <summary>The bytes the live maps can use: V8 reports 928, V9 908, V10 896.</summary>
    public static int Budget(int version) => version >= OverlayV10.Version ? OverlayV10.CopyBytes
        : version >= OverlayV9.Version ? OverlayV9.CopyBytes
        : version >= OverlayV8.Version ? OverlayV8.CopyBytes : CopyBytes;
    public static int LookupOffset(int version) => CopyBytes - LookupBytes(version);
    public static IReadOnlyList<OverlayMap> Maps => OverlayV4.Maps;

    public static IReadOnlyList<OverlayMap> Catalogue(int version) => version switch
    {
        Version => Maps,
        OverlayV6.Version => OverlayV6.Maps,
        OverlayV7.Version => OverlayV7.Maps,
        OverlayV8.Version => OverlayV8.Maps,
        OverlayV9.Version => OverlayV9.Maps,
        OverlayV10.Version => OverlayV10.Maps,
        _ => throw new ArgumentException("Expected overlay version 5, 6, 7, 8, 9 or 10.", nameof(version)),
    };

    public static bool Fits(int offset, int bytes, int version = Version) => (offset & 3) == 0 && (bytes & 3) == 0
        && OverlayV4.Fits(offset, bytes) && (offset >= PoolBStart || offset + bytes <= PoolAEnd(version));

    public static int? RecordOffset(OverlayEntry entry) => entry.DirectorySlot >= BaseSlots
        ? PoolBStart + 4 * (entry.DirectorySlot - BaseSlots) : null;

    public static int TrackingBytes(OverlayStatus status)
    {
        var records = status.Tables.Count(e => e.State != OverlayState.Free && e.DirectorySlot >= BaseSlots);
        return records == 0 ? 0 : records * RecordBytes + LookupBytes(status.Version);
    }

    public static byte[] Load(int table, OverlayPlacement placement) =>
        [.. OverlayContract.Load(table, placement.Offset),
            placement.DirectorySlot < BaseSlots ? (byte)255 : checked((byte)(placement.DirectorySlot - BaseSlots))];

    public static OverlayStatus ReadStatus(IDs2Line line, int version = Version)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var first = line.Request(OverlayContract.Status());
            ValidateSummary(first, version);
            if (first[44] == 255) return ParseStatus(first, [], version);
            var pool = line.Read(PoolBAddress, PoolBBytes);
            var last = line.Request(OverlayContract.Status());
            ValidateSummary(last, version);
            if (first.SequenceEqual(last)) return ParseStatus(last, pool, version);
        }
        throw new InvalidDataException("Live allocations changed while reading their status. Retry after the ECU state settles.");
    }

    public static OverlayStatus ParseStatus(IReadOnlyList<byte> reply, ReadOnlySpan<byte> poolB, int version = Version)
    {
        ValidateSummary(reply, version);
        var indexBytes = LookupBytes(version);
        var indexOffset = LookupOffset(version);
        var maps = Catalogue(version);
        var p = reply.Skip(3).ToArray();
        var entries = maps.Select(_ => new OverlayEntry(OverlayState.Free, 0, 0)).ToArray();
        var occupied = new List<(int Start, int End)>();
        var free = Budget(version);
        for (var slot = 0; slot < BaseSlots; slot++) ReadEntry(p.AsSpan(6 + slot * 8, 8), slot, true);
        if (p[25] != 0 || p[33] != 0) throw new InvalidDataException("Invalid directory control bytes.");
        var next = (int)p[41];
        var previousEnd = 0;
        if (next != 255 && poolB.Length != PoolBBytes) throw new InvalidDataException("Missing overflow directory snapshot.");
        if (next != 255) AddRange(indexOffset, indexBytes);
        while (next != 255)
        {
            var at = next * 4;
            if (at < previousEnd || at + RecordBytes > PoolBBytes - indexBytes)
                throw new InvalidDataException("Invalid overflow directory link.");
            AddRange(PoolBStart + at, RecordBytes);
            ReadEntry(poolB.Slice(at, RecordBytes), BaseSlots + next, false);
            previousEnd = at + RecordBytes;
            next = poolB[at + 3];
        }
        if (p[41] != 255)
        {
            var expectedIndex = new byte[indexBytes];
            for (var id = 0; id < entries.Length; id++)
                if (entries[id].State != OverlayState.Free) expectedIndex[id / 8] |= (byte)(1 << (id % 8));
            if (!poolB[(PoolBBytes - indexBytes)..].SequenceEqual(expectedIndex))
                throw new InvalidDataException("Invalid live-map lookup index.");
        }
        if (BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(46)) != free)
            throw new InvalidDataException("Invalid live RAM accounting.");
        return new(version, (p[5] & 1) != 0, false, entries, free,
            BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(48)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(52)),
            LimiterSettings.Read(p.AsSpan(56, 8)));

        void ReadEntry(ReadOnlySpan<byte> data, int slot, bool wire)
        {
            var state = (OverlayState)data[0]; var id = data[1]; var offset = data[2] * 4;
            var crc = wire ? BinaryPrimitives.ReadUInt32BigEndian(data[4..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            if (state == OverlayState.Free)
            {
                if (slot >= BaseSlots || id != 0 || offset != 0 || crc != 0)
                    throw new InvalidDataException("Invalid free directory record.");
                return;
            }
            if (state > OverlayState.On || id >= maps.Count || entries[id].State != OverlayState.Free || !Fits(offset, maps[id].Bytes, version))
                throw new InvalidDataException("Invalid map allocation.");
            AddRange(offset, maps[id].Bytes);
            entries[id] = new(state, offset, crc, slot);
        }

        void AddRange(int start, int bytes)
        {
            var end = start + bytes;
            if (occupied.Any(r => start < r.End && r.Start < end))
                throw new InvalidDataException("Map data overlaps another map or its tracking record.");
            occupied.Add((start, end)); free -= bytes;
        }
    }

    private static void ValidateSummary(IReadOnlyList<byte> reply, int version)
    {
        OverlayContract.ValidateFrame(reply);
        if (reply.Count != 68 || reply[2] != 0xA0 || !reply.Skip(3).Take(5).SequenceEqual(new byte[] { 76, 84, 79, 86, checked((byte)version) })
            || (reply[8] & ~1) != 0) throw new InvalidDataException($"Invalid protocol {version} status.");
    }

    public static OverlayPlacement? Place(int table, OverlayStatus status)
    {
        var maps = Catalogue(status.Version);
        if (table < 0 || table >= maps.Count || status.Tables.Count != maps.Count) throw new ArgumentException("Invalid live allocation request.");
        if (status.Tables[table].State != OverlayState.Free)
            return new(status.Tables[table].Offset, status.Tables[table].DirectorySlot);
        var baseSlot = Enumerable.Range(0, BaseSlots)
            .Where(slot => !status.Tables.Any(e => e.State != OverlayState.Free && e.DirectorySlot == slot))
            .Select(slot => (int?)slot).FirstOrDefault();
        if (baseSlot is { } available)
            return DataOffset(table, maps, Gaps(status), TrackingBytes(status) == 0) is { } offset ? new(offset, available) : null;

        if (maps.Any(m => status.Tables[m.Index].State != OverlayState.Free
            && status.Tables[m.Index].Offset + m.Bytes > LookupOffset(status.Version))) return null;
        foreach (var gap in Gaps(status, includeIndex: true).Where(g => g.Start >= PoolBStart && g.Bytes >= RecordBytes).OrderBy(g => g.Bytes))
        {
            // Either edge preserves one contiguous data gap; examine both before refusing a fit.
            foreach (var record in new[] { gap.Start + gap.Bytes - RecordBytes, gap.Start }.Distinct())
                if (DataOffset(table, maps, Gaps(status, (record, record + RecordBytes), includeIndex: true), false) is { } offset)
                    return new(offset, BaseSlots + (record - PoolBStart) / 4);
        }
        return null;
    }

    private static int? DataOffset(int table, IReadOnlyList<OverlayMap> maps, IEnumerable<(int Start, int Bytes)> gaps, bool originalPools)
    {
        var preferB = originalPools && table is 1 or 3;
        return gaps.Where(g => g.Bytes >= maps[table].Bytes)
            .OrderBy(g => (g.Start >= PoolBStart) == preferB ? 0 : 1).ThenBy(g => g.Bytes).ThenBy(g => g.Start)
            .Select(g => (int?)g.Start).FirstOrDefault();
    }

    public static int LargestBlock(OverlayStatus status) => Gaps(status).Select(g => g.Bytes).DefaultIfEmpty().Max();

    public static int? FreeFirst(int table, OverlayStatus status) => Catalogue(status.Version)
        .Where(m => m.Index != table && status.Tables[m.Index].State != OverlayState.Free).OrderBy(m => m.Bytes)
        .Where(m => Place(table, status with { Tables = status.Tables.Select((e, i) => i == m.Index
            ? new OverlayEntry(OverlayState.Free, 0, 0) : e).ToArray() }) is not null)
        .Select(m => (int?)m.Index).FirstOrDefault();

    private static IEnumerable<(int Start, int Bytes)> Gaps(OverlayStatus status, (int Start, int End)? extra = null, bool includeIndex = false)
    {
        var ranges = Catalogue(status.Version).Where(m => status.Tables[m.Index].State != OverlayState.Free)
            .Select(m => (Start: status.Tables[m.Index].Offset, End: status.Tables[m.Index].Offset + m.Bytes)).ToList();
        ranges.AddRange(status.Tables.Where(e => e.State != OverlayState.Free).Select(RecordOffset).OfType<int>()
            .Select(at => (at, at + RecordBytes)));
        if (includeIndex || status.Tables.Any(e => e.State != OverlayState.Free && e.DirectorySlot >= BaseSlots))
            ranges.Add((LookupOffset(status.Version), CopyBytes));
        if (extra is { } record) ranges.Add(record);
        foreach (var (start, end) in new[] { (0, PoolAEnd(status.Version)), (PoolBStart, CopyBytes) })
        {
            var cursor = start;
            foreach (var range in ranges.Where(r => r.Start >= start && r.Start < end).OrderBy(r => r.Start))
            {
                if (range.Start > cursor) yield return (cursor, range.Start - cursor);
                cursor = Math.Max(cursor, range.End);
            }
            if (cursor < end) yield return (cursor, end - cursor);
        }
    }
}
