using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// An ECU holding the live-tuning overlay, for tests, as the overlay bin behaves (MS43LAB plan 6 record, "The contract as
/// built"; patches/live_overlay.asm): DS2 command 06 reads its header, table list, flash and copy area, and command 0x6D
/// follows the contract check for check. Without the overlay, 06 reads FF at the header and 0x6D is unknown. Its clock moves
/// only by <see cref="Advance"/>; anything else gets no reply.
/// </summary>
internal sealed class FakeOverlayEcu : IDs2Line
{
    private const int HeaderAddress = OverlayContract.HeaderAddress;
    private const int TableListAddress = 0x0CBB18;
    private static readonly byte[] Header =
        { (byte)'L', (byte)'T', (byte)'O', (byte)'V', 0x02, 0x00, 0x54, 0xB5, 0x18, 0xBB, 0x40, 0xBB, 0x05, 0x00, 0x00, 0x00 };

    private readonly byte[] _flash;
    private readonly bool _hasOverlay;
    private readonly OverlayEntry[] _entries = new OverlayEntry[5];
    private readonly byte[] _copy = new byte[OverlayV3.CopyBytes];
    private uint _session;
    private uint _revision;
    private static readonly int[] V3Storage = { 0, 320, 356, 548, 740 };
    private bool _initialised;
    private TimeSpan _now;
    private TimeSpan _seen;

    public FakeOverlayEcu(byte[] flashImage, bool hasOverlay = true)
    {
        _flash = flashImage;
        _hasOverlay = hasOverlay;
        Wipe();
    }

    public bool Running { get; set; }
    public int Version { get; set; } = 2;
    public long MonotonicMilliseconds => (long)_now.TotalMilliseconds;
    public List<int> Waits { get; } = new();
    public void WaitMilliseconds(int milliseconds)
    {
        Waits.Add(milliseconds);
        Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>Thrown by the next <see cref="Request"/>, once.</summary>
    public Exception? FailNext { get; set; }

    /// <summary>Asked before every request; the exception it returns is thrown instead of an answer (a lost reply).</summary>
    public Func<byte[], Exception?>? FailWhen { get; set; }
    public Func<byte[], byte[], Exception?>? FailAfter { get; set; }

    /// <summary>Answers the next 0x6D request with these bytes instead, once, as a stray or late frame would.</summary>
    public byte[]? ReplyNext { get; set; }

    /// <summary>The next writes this many are answered A0 but never reach the copy, as a chunk the ECU did not get.</summary>
    public int DropWrites { get; set; }

    /// <summary>Every payload sent, in order.</summary>
    public List<byte[]> Requests { get; } = new();

    /// <summary>A flash image whose five maps hold distinct values; the λ map's cells sit at 128–188, so a cell can go leaner.</summary>
    public static byte[] Image(int seed = 0)
    {
        var image = new byte[524288];
        foreach (var map in OverlayContract.Maps)
        {
            for (var i = 0; i < map.Bytes; i++)
            {
                image[(map.Address & 0x7FFFF) + i] = (byte)(map.RicherOnly ? 128 + (i + seed) % 60 : 20 + (i * 7 + map.Index + seed) % 200);
            }
        }

        return image;
    }

    /// <summary>The ECU's own clock moves; housekeeping switches everything off after 3 s without a 0x6D frame or while stopped.</summary>
    public void Advance(TimeSpan time)
    {
        _now += time;
        Housekeeping();
    }

    /// <summary>The engine stalls with the key on: not running, and housekeeping switches everything off.</summary>
    public void Stall()
    {
        Running = false;
        Housekeeping();
    }

    /// <summary>A key cycle: the boot's RAM test wipes the window, and the engine is stopped.</summary>
    public void KeyCycle()
    {
        Running = false;
        Wipe();
        _initialised = false;
        _session = _revision = 0;
    }

    /// <summary>The status the ECU would give now, without sending anything.</summary>
    public OverlayStatus Peek() => OverlayContract.ParseStatus(StatusReply());

    public void CorruptCopy(int table, int index, byte value) => _copy[Storage(table) + index] = value;
    private int Storage(int table) => Version == 3 ? V3Storage[table] : _entries[table].Offset;

    public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
    {
        if (FailNext is { } failure)
        {
            FailNext = null;
            throw failure;
        }

        var bytes = payload.ToArray();
        if (FailWhen?.Invoke(bytes) is { } lost)
        {
            throw lost;
        }

        Requests.Add(bytes);
        if (ReplyNext is { } stray && bytes[0] == OverlayContract.Command)
        {
            ReplyNext = null;
            return stray;
        }

        var result = bytes switch
        {
            [0x06, 0x00, var a2, var a1, var a0, var n] => Reply(new byte[] { 0xA0 }.Concat(Enumerable.Range(0, n).Select(i => Peek8((a2 << 16 | a1 << 8 | a0) + i))).ToArray()),
            [OverlayContract.Command, ..] when !_hasOverlay => Reply(0xFF),
            [OverlayContract.Command, ..] => Handle(OverlayContract.Frame(bytes)),
            _ => throw new TimeoutException("The fake ECU does not answer this request."),
        };
        if (FailAfter?.Invoke(bytes, result) is { } after) throw after;
        return result;
    }

    private byte Peek8(int address)
    {
        if (Version == 3)
            for (var i = 0; i < 5; i++)
                if (address >= OverlayV3.Addresses[i] && address < OverlayV3.Addresses[i] + OverlayContract.Maps[i].Bytes)
                    return _copy[V3Storage[i] + address - OverlayV3.Addresses[i]];
        return address switch
    {
        >= HeaderAddress and < HeaderAddress + 16 => _hasOverlay
            ? address == HeaderAddress + 4 ? (byte)Version : Header[address - HeaderAddress] : (byte)0xFF,
        >= TableListAddress and < TableListAddress + 40 => _hasOverlay ? TableList()[address - TableListAddress] : (byte)0xFF,
        >= OverlayContract.CopyRamAddress and < OverlayContract.CopyRamAddress + OverlayContract.CopyBytes => _copy[address - OverlayContract.CopyRamAddress],
        _ => _flash[address & 0x7FFFF],
        };
    }

    private byte[] TableList() => OverlayContract.Maps.SelectMany(m => new[]
    {
        (byte)(m.Address / 0x4000), (byte)0, (byte)(m.Address % 0x4000), (byte)(m.Address % 0x4000 >> 8), (byte)m.Bytes, (byte)(m.Bytes >> 8),
        (byte)(Version == 2 && m.RicherOnly ? 1 : 0), (byte)0,
    }).ToArray();

    private byte[] Handle(byte[] frame)
    {
        if (!_initialised)
        {
            Wipe();
            _initialised = true;
        }

        if (OffDue())
        {
            AllOff();
        }

        _seen = _now;
        return Version == 3 ? HandleV3(frame) : HandleCommand(frame);
    }

    private byte[] HandleCommand(byte[] frame)
    {
        int len = frame[1];
        if (len < 5)
        {
            return Refuse(0xB0, OverlayReason.Length);
        }

        int sub = frame[3];
        int Arg(int k) => frame[4 + k];
        int Word(int k) => Arg(k) << 8 | Arg(k + 1);
        return sub switch
        {
            0 => len == 5 ? StatusReply() : Refuse(0xB0, OverlayReason.Length),
            1 => len != 8 ? Refuse(0xB0, OverlayReason.Length) : Load(Arg(0), Word(1)),
            2 => len < 8 ? Refuse(0xB0, OverlayReason.Length) : Write(Arg(0), Word(1), frame[7..^1]),
            3 => len != 10 ? Refuse(0xB0, OverlayReason.Length) : On(Arg(0),
                (uint)Arg(1) << 24 | (uint)Arg(2) << 16 | (uint)Arg(3) << 8 | (uint)Arg(4)),
            4 => len != 6 ? Refuse(0xB0, OverlayReason.Length) : Arg(0) > 4 ? Refuse(0xB0, OverlayReason.Table) : Off(Arg(0)),
            5 => len != 5 ? Refuse(0xB0, OverlayReason.Length) : AllOn(),
            6 => len != 5 ? Refuse(0xB0, OverlayReason.Length) : Accept(AllOff),
            7 => len != 6 ? Refuse(0xB0, OverlayReason.Length) : Free(Arg(0)),
            8 => len != 5 ? Refuse(0xB0, OverlayReason.Length) : Accept(Wipe),
            _ => Refuse(0xB0, OverlayReason.Sub),
        };
    }

    private byte[] HandleV3(byte[] frame)
    {
        uint Word(int at) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(at));
        byte[] Ack() => Reply(0xA0, (byte)(_revision >> 24), (byte)(_revision >> 16), (byte)(_revision >> 8), (byte)_revision);
        if (frame.Length < 5) return Refuse(0xB0, OverlayReason.Length);
        if (frame[3] == 0) return frame.Length == 5 ? StatusReply() : Refuse(0xB0, OverlayReason.Length);
        if (frame[3] == 10)
        {
            if (frame.Length != 9 || Word(4) == 0) return Refuse(0xB0, OverlayReason.Length);
            if (_session != Word(4)) { _session = Word(4); _revision = 0; }
            return Ack();
        }
        if (frame.Length < 13) return Refuse(0xB0, OverlayReason.Length);
        if (Word(4) == 0 || Word(4) != _session) return Refuse(0xB1, OverlayReason.Session);
        if (frame[3] == 9)
        {
            if (frame.Length != 37) return Refuse(0xB0, OverlayReason.Length);
            if (Checksum(frame[4..32]) != Word(32)) return Refuse(0xB1, OverlayReason.Integrity);
            var table = frame[12]; var at = frame[14] * 256 + frame[15];
            if (table > 4) return Refuse(0xB0, OverlayReason.Table);
            if (frame[13] != 0 || (at & 1) != 0 || at + 8 > OverlayContract.Maps[table].Bytes) return Refuse(0xB0, OverlayReason.Range);
            if (_entries[table].State != OverlayState.On) return Refuse(0xB1, OverlayReason.NotLoaded);
            if (!Running) return Refuse(0xB1, OverlayReason.NotRunning);
            var current = Copy(table);
            if (Word(8) != _revision)
                return Word(8) != uint.MaxValue && Word(8) + 1 == _revision && Checksum(current) == Word(20)
                    ? Ack() : Refuse(0xB1, OverlayReason.Revision);
            if (_revision == uint.MaxValue) return Refuse(0xB1, OverlayReason.Revision);
            if (Checksum(current) != Word(16)) return Refuse(0xB1, OverlayReason.Integrity);
            frame.AsSpan(24, 8).CopyTo(current.AsSpan(at));
            if (Checksum(current) != Word(20)) return Refuse(0xB1, OverlayReason.Integrity);
            current.CopyTo(_copy, Storage(table));
            _entries[table] = _entries[table] with { ContentCrc32 = Word(20) };
            _revision++;
            return Ack();
        }
        if (Word(8) != _revision || _revision == uint.MaxValue) return Refuse(0xB1, OverlayReason.Revision);
        var legacy = OverlayContract.Frame(frame.AsSpan(2, 2).ToArray().Concat(frame[12..^1]).ToArray());
        var response = HandleCommand(legacy);
        if (response[2] != 0xA0) return response;
        _revision++;
        return Ack();
    }

    private byte[] Load(int table, int offset)
    {
        if (table > 4)
        {
            return Refuse(0xB0, OverlayReason.Table);
        }

        var size = OverlayContract.Maps[table].Bytes;
        if (Version == 3)
        {
            if (offset != OverlayV3.Offset(table)) return Refuse(0xB0, OverlayReason.Range);
            if (_entries[table].State == OverlayState.On) return Refuse(0xB1, OverlayReason.TableOn);
            Flash(table).CopyTo(_copy, V3Storage[table]);
            _entries[table] = new OverlayEntry(OverlayState.Loaded, offset, 0);
            return Reply(0xA0);
        }
        if (offset > OverlayContract.CopyBytes || offset + size > OverlayContract.CopyBytes)
        {
            return Refuse(0xB0, OverlayReason.Range);
        }

        for (var other = 0; other < 5; other++)
        {
            var entry = _entries[other];
            if (other != table && entry.State != OverlayState.Free && entry.Offset < offset + size
                && offset < entry.Offset + OverlayContract.Maps[other].Bytes)
            {
                return Refuse(0xB0, OverlayReason.Overlap);
            }
        }

        if (_entries[table].State == OverlayState.On)
        {
            return Refuse(0xB1, OverlayReason.TableOn);
        }

        Flash(table).CopyTo(_copy, offset);
        _entries[table] = new OverlayEntry(OverlayState.Loaded, offset, 0);
        return Reply(0xA0);
    }

    private byte[] Write(int table, int at, byte[] data)
    {
        if (table > 4)
        {
            return Refuse(0xB0, OverlayReason.Table);
        }

        var size = OverlayContract.Maps[table].Bytes;
        if (data.Length is 0 or > OverlayContract.MostWrite || at > size || at + data.Length > size)
        {
            return Refuse(0xB0, OverlayReason.Range);
        }

        var entry = _entries[table];
        if (entry.State == OverlayState.Free)
        {
            return Refuse(0xB1, OverlayReason.NotLoaded);
        }

        if (entry.State == OverlayState.On)
        {
            return Refuse(0xB1, OverlayReason.TableOn);
        }

        if (DropWrites > 0)
        {
            DropWrites--;
            return Reply(0xA0);
        }

        data.CopyTo(_copy, Storage(table) + at);
        _entries[table] = entry with { State = OverlayState.Loaded };
        return Reply(0xA0);
    }

    private byte[] On(int table, uint crc)
    {
        if (table > 4)
        {
            return Refuse(0xB0, OverlayReason.Table);
        }

        if (!Running)
        {
            return Refuse(0xB1, OverlayReason.NotRunning);
        }

        if (_entries[table].State == OverlayState.Free)
        {
            return Refuse(0xB1, OverlayReason.NotLoaded);
        }

        if (Verify(table, crc) is { } reason)
        {
            return Refuse(0xB1, reason);
        }

        _entries[table] = _entries[table] with { State = OverlayState.On, ContentCrc32 = crc };
        return Reply(0xA0);
    }

    private byte[] Off(int table)
    {
        if (_entries[table].State == OverlayState.On)
        {
            _entries[table] = _entries[table] with { State = OverlayState.Ready };
        }

        return Reply(0xA0);
    }

    private byte[] AllOn()
    {
        if (!Running)
        {
            return Refuse(0xB1, OverlayReason.NotRunning);
        }

        for (var table = 0; table < 5; table++)
        {
            if (_entries[table].State == OverlayState.Ready && Verify(table, _entries[table].ContentCrc32) is { } reason)
            {
                return Refuse(0xB1, reason);
            }
        }

        for (var table = 0; table < 5; table++)
        {
            if (_entries[table].State == OverlayState.Ready)
            {
                _entries[table] = _entries[table] with { State = OverlayState.On };
            }
        }

        return Reply(0xA0);
    }

    private byte[] Free(int table)
    {
        if (table > 4)
        {
            return Refuse(0xB0, OverlayReason.Table);
        }

        if (_entries[table].State == OverlayState.On)
        {
            return Refuse(0xB1, OverlayReason.TableOn);
        }

        _entries[table] = new OverlayEntry(OverlayState.Free, 0, 0);
        return Reply(0xA0);
    }

    private OverlayReason? Verify(int table, uint crc)
    {
        var copy = Copy(table);
        var flash = Flash(table);
        if (Checksum(copy) != crc)
        {
            return OverlayReason.Integrity;
        }
        if (Version == 3) return null;

        for (var i = 0; i < copy.Length; i++)
        {
            var delta = (int)copy[i] - flash[i];
            if (table == 2 && delta < 0)
            {
                return OverlayReason.Leaner;
            }

            if (delta is < -8 or > 8)
            {
                return OverlayReason.EditLimit;
            }
        }

        return null;
    }

    private static uint Checksum(byte[] bytes)
    {
        var remainder = uint.MaxValue;
        foreach (var value in bytes)
        {
            remainder ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var low = remainder & 1;
                remainder >>= 1;
                if (low != 0)
                {
                    remainder ^= 0xEDB88320;
                }
            }
        }

        return remainder ^ uint.MaxValue;
    }

    private byte[] StatusReply()
    {
        var payload = new List<byte> { 0xA0, (byte)'L', (byte)'T', (byte)'O', (byte)'V', (byte)Version, (byte)((Running ? 1 : 0) | (Version == 3 ? 0 : 2)) };
        var free = Version == 3 ? OverlayV3.CopyBytes : OverlayContract.CopyBytes;
        for (var table = 0; table < 5; table++)
        {
            var entry = _entries[table];
            payload.AddRange(new[] { (byte)entry.State, (byte)0, (byte)(entry.Offset >> 8), (byte)entry.Offset,
                (byte)(entry.ContentCrc32 >> 24), (byte)(entry.ContentCrc32 >> 16), (byte)(entry.ContentCrc32 >> 8), (byte)entry.ContentCrc32 });
            if (entry.State != OverlayState.Free)
            {
                free -= OverlayContract.Maps[table].Bytes;
            }
        }

        payload.AddRange(new[] { (byte)(free >> 8), (byte)free });
        if (Version == 3)
            foreach (var value in new[] { _session, _revision })
                payload.AddRange(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        return Reply(payload.ToArray());
    }

    private bool OffDue() => !Running || _now - _seen >= TimeSpan.FromSeconds(OverlayContract.SilenceSeconds);

    private void Housekeeping()
    {
        if (_initialised && OffDue())
        {
            AllOff();
        }
    }

    private void AllOff()
    {
        for (var table = 0; table < 5; table++)
        {
            if (_entries[table].State == OverlayState.On)
            {
                _entries[table] = _entries[table] with { State = OverlayState.Ready };
            }
        }
    }

    private void Wipe() => Array.Fill(_entries, new OverlayEntry(OverlayState.Free, 0, 0));

    private byte[] Flash(int table) => _flash.AsSpan(OverlayContract.Maps[table].Address & 0x7FFFF, OverlayContract.Maps[table].Bytes).ToArray();

    private byte[] Copy(int table) => _copy.AsSpan(Storage(table), OverlayContract.Maps[table].Bytes).ToArray();

    private static byte[] Accept(Action action)
    {
        action();
        return Reply(0xA0);
    }

    private static byte[] Refuse(byte status, OverlayReason reason) => Reply(status, (byte)reason);

    private static byte[] Reply(params byte[] body) => OverlayContract.Frame(body);
}
