using System.Buffers.Binary;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

// Application-side test double for overlay V10.4. Compiled C167 behavior needs emulator tests.
// HeaderVersion and Minor alter only the header for detection tests.
internal sealed class FakeOverlayEcu(byte[] flash) : IDs2Line
{
    private static IReadOnlyList<OverlayMap> Maps => OverlayV10.Maps;

    /// <summary>A 512 KiB bin with the first five maps filled in (AFR cells 128.., the others 20..), as the overlay's flash.</summary>
    public static byte[] Image(int seed = 0)
    {
        var image = new byte[524288];
        foreach (var map in OverlayContract.Maps)
            for (var i = 0; i < map.Bytes; i++)
                image[(map.Address & 0x7FFFF) + i] = (byte)(map.RicherOnly ? 128 + (i + seed) % 60 : 20 + (i * 7 + map.Index + seed) % 200);
        return image;
    }

    /// <summary>A bin as the V10.4 overlay's flash: <see cref="Image"/>, the limiter's thresholds, and the flashed groups 138..142.</summary>
    public static byte[] CurrentImage(int seed = 0)
    {
        var image = Image(seed);
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        OverlayV104Settings.Defaults.CopyTo(image, 0x4FF80);
        return image;
    }

    /// <summary>The low byte of the header's version word: 10.</summary>
    public int HeaderVersion { get; set; } = OverlayV10.Version;

    /// <summary>The high byte of the header's version word: 4 on V10.4.</summary>
    public int Minor { get; set; } = OverlayV10.Minor;
    public ushort? HeaderMagic { get; set; }

    /// <summary>False: the header reads as the bin's own bytes, as on an ECU without the overlay.</summary>
    public bool HasOverlay { get; set; } = true;
    public int? ScalarFlagsOverride { get; set; }
    public OverlayEntry[] Entries { get; } = OverlayV10.Maps.Select(_ => new OverlayEntry(OverlayState.Free, 0, 0)).ToArray();
    public byte[] Copies { get; } = new byte[932];
    public LimiterSettings Limiter { get; private set; } = new(flash.AsSpan(0x75D47, 6).ToArray(), flash[0x70BFF], flash[0x7131E] != 0, false);
    public List<byte[]> Requests { get; } = new();
    public List<int> Waits { get; } = new();
    public bool LoseNextPatchReply { get; set; }
    public byte? LoseMutation { get; set; }
    public bool LoseBeforeExecution { get; set; }
    public Action? BeforeLostMutationReply { get; set; }
    public bool Running { get; set; } = true;
    public uint Session { get; private set; }
    public uint Revision { get; private set; }
    public long MonotonicMilliseconds { get; private set; }
    public Action? BeforeOverflowRead { get; set; }
    private ushort _disableEpoch;
    /// <summary>The address sub 12 last set, or null before any; 0 after a stop.</summary>
    public uint? WatchAddress { get; private set; }
    /// <summary>The record sub 11 replies with, after the 73 stock bytes.</summary>
    public WatchRecord LogRecord { get; set; } = new(WatchKind.None, 0, 0, 0, 0, 0, 0);
    /// <summary>What each sub-11 reply says is in the stock payload (its first byte counts the replies).</summary>
    public byte[] StockPayload { get; } = Enumerable.Range(0, 73).Select(i => (byte)(i * 3 + 1)).ToArray();
    /// <summary>Called before each sub-11 reply is built, so a test can change the record a poll in flight carries.</summary>
    public Action? BeforeLogReply { get; set; }
    /// <summary>This many watch requests time out, as the first one after the speed switch did in the emulator.</summary>
    public int DropWatches { get; set; }
    /// <summary>A table whose load (sub 1) is refused with this reason, as a full copy area refuses it.</summary>
    public (int Table, OverlayReason Reason)? RefuseLoad { get; set; }
    /// <summary>Thrown by the next <see cref="Request"/>, once.</summary>
    public Exception? FailNext { get; set; }

    /// <summary>Asked before every request; the exception it returns is thrown instead of an answer (a lost reply).</summary>
    public Func<byte[], Exception?>? FailWhen { get; set; }

    /// <summary>Answers the next 0x6D request with these bytes instead, once, as a stray or late frame would.</summary>
    public byte[]? ReplyNext { get; set; }

    /// <summary>The next writes this many are acknowledged but never reach the copy, as a chunk the ECU did not get.</summary>
    public int DropWrites { get; set; }

    /// <summary>The status the ECU would give now, without sending anything.</summary>
    public OverlayStatus Peek() => OverlayMemory.ParseStatus(StatusFrame(), OverflowPool());

    /// <summary>The engine stalls with the key on: not running, and the overlay switches everything off.</summary>
    public void Stall() { Running = false; AutomaticDisable(); }

    /// <summary>A key cycle: the boot's RAM test wipes the copy area, and the engine is stopped.</summary>
    public void KeyCycle() { Running = false; Reset(); }

    public void WaitMilliseconds(int milliseconds) { Waits.Add(milliseconds); MonotonicMilliseconds += milliseconds; }

    public void Reset()
    {
        Array.Fill(Entries, new OverlayEntry(OverlayState.Free, 0, 0)); Array.Clear(Copies);
        Session = Revision = 0; _disableEpoch = 0; Limiter = Limiter with { Enabled = false, Confirmed = false };
    }

    public void AutomaticDisable()
    {
        for (var i = 0; i < Entries.Length; i++)
            if (Entries[i].State == OverlayState.On) Entries[i] = Entries[i] with { State = OverlayState.Ready };
        Limiter = Limiter with { Enabled = false }; _disableEpoch++;
    }

    public byte[] Read(int address, int count)
    {
        var header = new byte[16]; "LTOV"u8.CopyTo(header); Put16(header, 4, (ushort)(HeaderVersion | Minor << 8)); Put16(header, 6, 0xB554);
        Put16(header, 8, 0xC800); Put16(header, 10, 0xD000); Put16(header, 12, (ushort)Maps.Count); Put16(header, 14, HeaderMagic ?? (ushort)(0xC0D0 + HeaderVersion));
        var list = Maps.SelectMany(m => new byte[] { (byte)(m.Address / 0x4000), 0,
            (byte)(m.Address % 0x4000), (byte)(m.Address % 0x4000 >> 8), (byte)m.Bytes, (byte)(m.Bytes >> 8), (byte)(m.ScalarGroup is null ? m.Flags : ScalarFlagsOverride ?? m.Flags), 0 }).ToArray();
        if (HasOverlay && address >= OverlayContract.HeaderAddress && address < OverlayContract.HeaderAddress + 16) return header.AsSpan(address - OverlayContract.HeaderAddress, count).ToArray();
        if (address >= 0xCC800 && address < 0xCC800 + list.Length) return list.AsSpan(address - 0xCC800, count).ToArray();
        if (address == OverlayMemory.LimiterAddress) return WireLimiter().AsSpan(0, count).ToArray();
        if (address is >= 0x41D00 and < 0x41FC0) return Copies.AsSpan(address - 0x41D00, count).ToArray();
        if (address is >= 0xE6E0 and < 0xE7C4)
        {
            BeforeOverflowRead?.Invoke();
            return OverflowPool().AsSpan(address - 0xE6E0, count).ToArray();
        }
        return flash.AsSpan(address & 0x7FFFF, count).ToArray();
    }

    public byte[] Request(IReadOnlyList<byte> request, int listenMilliseconds = 400)
    {
        if (FailNext is { } failure) { FailNext = null; throw failure; }
        var p = request.ToArray();
        if (FailWhen?.Invoke(p) is { } lost) throw lost;
        Requests.Add(p);
        if (ReplyNext is { } stray && p[0] == OverlayContract.Command) { ReplyNext = null; return stray; }
        if (p is [0x06, 0, var a2, var a1, var a0, var n]) return Frame([0xA0, .. Read(a2 << 16 | a1 << 8 | a0, n)]);
        if (p is [0x6D, 0]) return StatusFrame();
        if (p is [0x6D, 12, var w2, var w1, var w0])
        {
            if (DropWatches > 0) { DropWatches--; throw new TimeoutException("the watch request was lost"); }
            var address = (uint)(w2 << 16 | w1 << 8 | w0);
            if (address >= 0x80000) return Refuse(4);
            WatchAddress = address; LogRecord = new(WatchKind.None, 0, 0, 0, 0, 0, 0);
            return Frame([0xA0, w2, w1, w0]);
        }
        if (p is [0x6D, 11])
        {
            BeforeLogReply?.Invoke();
            var r = LogRecord;
            return Frame([0xA0, .. StockPayload, (byte)r.Kind, r.Count, (byte)(r.Row >> 8), (byte)r.Row, (byte)(r.RowFraction >> 8),
                (byte)r.RowFraction, (byte)(r.Column >> 8), (byte)r.Column, (byte)(r.ColumnFraction >> 8), (byte)r.ColumnFraction, r.Columns]);
        }
        if (p is [0x6D, 10, ..])
        {
            var token = U32(p, 2); if (token != Session) { Session = token; Revision = 0; } return Ack();
        }
        if (U32(p, 2) != Session) return Refuse(12);
        if (p[1] == 9)
        {
            if (OverlayContract.Crc32(p.AsSpan(2, 20)) != U32(p, 22)) return Refuse(9);
            var table = p[10]; var at = U16(p, 12); var data = p.AsSpan(14, 8).ToArray();
            if (U32(p, 6) + 1 == Revision)
            {
                var same = table == 255 ? Limiter.Payload().SequenceEqual(data)
                    : Copies.AsSpan(Entries[table].Offset + at, 8).SequenceEqual(data);
                return same ? Ack() : Refuse(13);
            }
            if (U32(p, 6) != Revision) return Refuse(13);
            if (table == 255) Limiter = LimiterSettings.Read(data) with { Confirmed = true, AppliedIgnitionCut = (data[7] & 1) != 0 };
            else
            {
                data.CopyTo(Copies, Entries[table].Offset + at);
                Entries[table] = Entries[table] with { ContentCrc32 = OverlayContract.Crc32(Copies.AsSpan(Entries[table].Offset, Maps[table].Bytes)) };
            }
            Revision++;
            if (LoseNextPatchReply) { LoseNextPatchReply = false; throw new TimeoutException("Lost patch acknowledgement"); }
            return Ack();
        }
        if (U32(p, 6) != Revision) return Refuse(13);
        var lose = LoseMutation == p[1];
        if (lose) LoseMutation = null;
        if (lose && LoseBeforeExecution) throw new TimeoutException("Lost mutation request");
        var id = p.Length > 10 ? p[10] : 0;
        switch (p[1])
        {
            case 1:
                if (RefuseLoad is { } refused && refused.Table == id) return Refuse((byte)refused.Reason);
                var offset = U16(p, 11);
                Read(Maps[id].Address, Maps[id].Bytes).CopyTo(Copies, offset);
                var slot = p[13] == 255
                    ? Entries[id].State != OverlayState.Free ? Entries[id].DirectorySlot
                        : Enumerable.Range(0, 5).First(s => !Entries.Any(e => e.State != OverlayState.Free && e.DirectorySlot == s))
                    : 5 + p[13];
                Entries[id] = new(OverlayState.Loaded, offset, 0, slot); break;
            case 2:
                // As the firmware: an inactive write to a settings group must supply the whole group.
                if (Maps[id].ScalarGroup is not null && (U16(p, 11) != 0 || p.Length - 13 != Maps[id].Bytes)) return Refuse(4);
                if (DropWrites > 0) { DropWrites--; break; }
                p.AsSpan(13).CopyTo(Copies.AsSpan(Entries[id].Offset + U16(p, 11))); break;
            case 3:
                if (!Running) return Refuse((byte)OverlayReason.NotRunning);
                if (OverlayContract.Crc32(Copies.AsSpan(Entries[id].Offset, Maps[id].Bytes)) != U32(p, 11)) return Refuse(9);
                Entries[id] = Entries[id] with { State = OverlayState.On, ContentCrc32 = U32(p, 11) }; break;
            case 4: Entries[id] = Entries[id] with { State = OverlayState.Ready }; break;
            case 5:
                if (!Running) return Refuse((byte)OverlayReason.NotRunning);
                for (var i = 0; i < Entries.Length; i++) if (Entries[i].State == OverlayState.Ready) Entries[i] = Entries[i] with { State = OverlayState.On };
                if (Limiter.Confirmed) Limiter = Limiter with { Enabled = true }; break;
            case 6:
                for (var i = 0; i < Entries.Length; i++) if (Entries[i].State == OverlayState.On) Entries[i] = Entries[i] with { State = OverlayState.Ready };
                Limiter = Limiter with { Enabled = false }; break;
            case 7: Entries[id] = new(OverlayState.Free, 0, 0); break;
            case 8: Array.Fill(Entries, new OverlayEntry(OverlayState.Free, 0, 0)); Limiter = Limiter with { Enabled = false, Confirmed = false }; break;
            default: return Refuse(2);
        }
        Revision++;
        if (lose) { BeforeLostMutationReply?.Invoke(); throw new TimeoutException("Lost mutation acknowledgement"); }
        return Ack();
    }

    public byte[] StatusFrame() => StatusFrameV5();

    private OverlayEntry[] OverflowEntries() => Entries.Where(e => e.State != OverlayState.Free && e.DirectorySlot >= 5)
        .OrderBy(e => e.DirectorySlot).ToArray();

    public byte[] OverflowPool()
    {
        var pool = Copies.AsSpan(704).ToArray(); var records = OverflowEntries();
        if (records.Length > 0)
        {
            Array.Clear(pool, 228 - OverlayMemory.LookupBytes, OverlayMemory.LookupBytes);
            for (var id = 0; id < Entries.Length; id++)
                if (Entries[id].State != OverlayState.Free) pool[228 - OverlayMemory.LookupBytes + id / 8] |= (byte)(1 << (id % 8));
        }
        for (var i = 0; i < records.Length; i++)
        {
            var e = records[i]; var id = Array.FindIndex(Entries, x => ReferenceEquals(x, e)); var at = (e.DirectorySlot - 5) * 4;
            pool[at] = (byte)e.State; pool[at + 1] = (byte)id; pool[at + 2] = (byte)(e.Offset / 4);
            pool[at + 3] = i + 1 == records.Length ? (byte)255 : (byte)(records[i + 1].DirectorySlot - 5);
            BinaryPrimitives.WriteUInt32LittleEndian(pool.AsSpan(at + 4), e.ContentCrc32);
        }
        return pool;
    }

    private byte[] StatusFrameV5()
    {
        var p = new byte[64]; "LTOV"u8.CopyTo(p); p[4] = (byte)HeaderVersion; p[5] = Running ? (byte)1 : (byte)0;
        for (var i = 0; i < Entries.Length; i++)
        {
            var e = Entries[i];
            if (e.State == OverlayState.Free || e.DirectorySlot >= 5) continue;
            var at = 6 + e.DirectorySlot * 8;
            p[at] = (byte)e.State; p[at + 1] = (byte)i; p[at + 2] = (byte)(e.Offset / 4); Put32(p, at + 4, e.ContentCrc32);
        }
        var records = OverflowEntries();
        p[9] = (byte)_disableEpoch; p[17] = (byte)(_disableEpoch >> 8);
        p[41] = records.Length == 0 ? (byte)255 : (byte)(records[0].DirectorySlot - 5);
        var free = OverlayMemory.Budget - Maps.Where(m => Entries[m.Index].State != OverlayState.Free).Sum(m => m.Bytes)
            - records.Length * 8 - (records.Length == 0 ? 0 : OverlayMemory.LookupBytes);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(46), (ushort)free);
        Put32(p, 48, Session); Put32(p, 52, Revision); WireLimiter().CopyTo(p, 56);
        return Frame([0xA0, .. p]);
    }
    private byte[] WireLimiter() => [.. Limiter.GearLimits, Limiter.Hysteresis,
        (byte)((Limiter.IgnitionCut ? 1 : 0) | (Limiter.Confirmed ? 32 : 0) | (Limiter.AppliedIgnitionCut ? 64 : 0) | (Limiter.Enabled ? 128 : 0))];
    private byte[] Ack() { var p = new byte[5]; p[0] = 0xA0; Put32(p, 1, Revision); return Frame(p); }
    private static byte[] Refuse(byte reason) => Frame([0xB1, reason]);
    private static byte[] Frame(byte[] p) { var f = new byte[p.Length + 3]; f[0] = 0x12; f[1] = (byte)f.Length; p.CopyTo(f, 2); f[^1] = f.Aggregate((byte)0, (a, b) => (byte)(a ^ b)); return f; }
    private static ushort U16(byte[] p, int at) => BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(at));
    private static uint U32(byte[] p, int at) => BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(at));
    private static void Put16(byte[] p, int at, ushort n) => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at), n);
    private static void Put32(byte[] p, int at, uint n) => BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(at), n);
}
