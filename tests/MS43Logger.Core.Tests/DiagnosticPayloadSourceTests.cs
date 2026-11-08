using System.Diagnostics;
using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// The log's diagnostic columns (V10.1 plan, Task 4, as the V10.1 review reshaped them): the stored faults and the task
/// queues when logging starts and every 20 s, and on a V10 ECU FDF8 every 0.5 s, with at most one DS2 06 exchange
/// between two polls and none while the log is off. A read that fails keeps the last value and the poll goes on (RF4).
/// </summary>
public sealed class DiagnosticPayloadSourceTests
{
    /// <summary>Answers DS2 06 reads from a byte map (0 where unset) on a clock the test moves; can time out.</summary>
    private sealed class MemoryLine : IDs2Line
    {
        public Dictionary<int, byte> Memory { get; } = new();
        public List<int> Reads { get; } = new();
        public long Now { get; set; }
        public long MonotonicMilliseconds => Now;
        public Func<int, bool> TimesOut { get; set; } = _ => false;
        public long LastRequestAt { get; private set; }

        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            Assert.Equal(0x06, payload[0]);
            var address = payload[2] << 16 | payload[3] << 8 | payload[4];
            int n = payload[5];
            Reads.Add(address);
            LastRequestAt = Stopwatch.GetTimestamp();
            if (TimesOut(address)) throw new TimeoutException();
            var frame = new byte[n + 4];
            frame[0] = 0x12; frame[1] = (byte)frame.Length; frame[2] = 0xA0;
            for (var i = 0; i < n; i++) frame[3 + i] = Memory.GetValueOrDefault(address + i);
            for (var i = 0; i < frame.Length - 1; i++) frame[^1] ^= frame[i];
            return frame;
        }

        public MemoryLine WithLayout()
        {
            for (var p = 0; p < FaultPaths.All.Count; p++) Memory[FaultMemory.DescriptorsAddress + 22 * p] = FaultPaths.All[p].Ds2Code;
            return this;
        }

        /// <summary>Stores <paramref name="faults"/>, oldest first: each path with its status byte (bits 0-3 the type, 5 stored).</summary>
        public MemoryLine Stored(params (int Path, byte Status)[] faults)
        {
            Memory[FaultMemory.StoredCountAddress] = (byte)faults.Length;
            for (var i = 0; i < faults.Length; i++)
            {
                Memory[FaultMemory.StoredListAddress + i] = (byte)faults[i].Path;
                Memory[FaultMemory.RecordsAddress + 12 * faults[i].Path] = faults[i].Status;
            }
            return this;
        }

        public void Word(int address, ushort value) { Memory[address] = (byte)value; Memory[address + 1] = (byte)(value >> 8); }
    }

    /// <summary>The logger's own poll: counts the calls and says when its request went out.</summary>
    private sealed class Poll(long? timestamp = null) : IPayloadSource
    {
        public int Calls { get; private set; }
        public long StartedAt { get; private set; }
        public byte[] PollPayload() { Calls++; StartedAt = Stopwatch.GetTimestamp(); return [1, 2, 3]; }
        public long? LastRequestTimestamp => timestamp;
        public int LastAttempts => 2;
    }

    private static int Count(MemoryLine line, int address) => line.Reads.Count(a => a == address);

    /// <summary>Polls every 60 ms (the car's pace) from the line's clock to <paramref name="until"/>, one exchange at most each.</summary>
    private static void PollUntil(DiagnosticPayloadSource source, MemoryLine line, long until)
    {
        for (; line.Now < until; line.Now += 60)
        {
            var before = line.Reads.Count;
            source.PollPayload();
            Assert.True(line.Reads.Count - before <= 1, $"{line.Reads.Count - before} reads before the poll at {line.Now} ms");
        }
    }

    [Fact]
    public void TheFaultRoundGoesOneReadAPollAndChecksTheLayoutOnce()
    {
        var line = new MemoryLine().WithLayout().Stored((124, 0x28), (0, 0x21)); line.Word(FaultMemory.QueueFlagsAddress, 1 << 6 | 1 << 8);
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: false);
        PollUntil(source, line, 1_000);
        Assert.Equal(("P1567 P0103", "1 3"), (source.Faults, source.Queues));
        // the queue flags, the layout in two reads, the count, the list, a record each: six polls
        Assert.Equal([FaultMemory.QueueFlagsAddress, FaultMemory.DescriptorsAddress, FaultMemory.DescriptorsAddress + Ds2Memory.MostRead,
            FaultMemory.StoredCountAddress, FaultMemory.StoredListAddress, FaultMemory.RecordsAddress + 12 * 124, FaultMemory.RecordsAddress],
            line.Reads);
        line.Stored(); line.Word(FaultMemory.QueueFlagsAddress, 0);
        PollUntil(source, line, 19_980);
        Assert.Equal(1, Count(line, FaultMemory.StoredCountAddress));
        PollUntil(source, line, 21_000);
        Assert.Equal(("none", "none"), (source.Faults, source.Queues));
        Assert.Equal(1, Count(line, FaultMemory.DescriptorsAddress));              // the layout is known for the session
        Assert.Equal(2, Count(line, FaultMemory.StoredCountAddress));
    }

    [Fact]
    public void OnAV10EcuFdf8IsReadEveryHalfSecondFirst()
    {
        var line = new MemoryLine().WithLayout();
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: true);
        source.PollPayload();
        Assert.Equal([0xFDF8], line.Reads);                                         // the state first, the faults on the next polls
        PollUntil(source, line, 3_000);
        Assert.InRange(Count(line, 0xFDF8), 6, 7);
        Assert.DoesNotContain(line.Reads, a => a is 0xFD2C or 0xFD2E);              // the payload carries those bits
    }

    [Fact] // RF4, the review: a log that is off, or a Performance run that is recording, gets the bare poll
    public void NothingMoreIsReadWhileTheLogIsOff()
    {
        var line = new MemoryLine().WithLayout();
        var on = false;
        var poll = new Poll();
        var source = new DiagnosticPayloadSource(poll, line, overlayState: true, active: () => on);
        PollUntil(source, line, 5_000);
        Assert.Empty(line.Reads);
        Assert.True(poll.Calls > 80);
        on = true;
        PollUntil(source, line, 5_500);
        Assert.Contains(0xFDF8, line.Reads);
        Assert.Contains(FaultMemory.QueueFlagsAddress, line.Reads);
    }

    /// <summary>Pops' state at 0x41FBC: POPS_LEFT (word) and POPS_MIN (807F units).</summary>
    private static void Pops(MemoryLine line, ushort left, byte lowest)
    {
        line.Word(DiagnosticPayloadSource.PopsAddress, left);
        line.Memory[DiagnosticPayloadSource.PopsAddress + 2] = lowest;
    }

    [Fact]
    public void TheStateColumnsSayWhatTheBitsMean()
    {
        var line = new MemoryLine().WithLayout();
        line.Word(0xFDF8, 0x4000 | 1 << 3 | 1 << 4 | 1 << 7 | 1 << 13);
        Pops(line, left: 150, lowest: 125);
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: true);
        PollUntil(source, line, 500);
        Assert.Equal(["faults", "queues", "fdf8", "pops_window", "pops_low_rpm", "pops_used", "blip", "guard"],
            DiagnosticPayloadSource.CsvHeader);
        Assert.Equal(["none", "none", "6098", "1", "4000", "1", "0", "1"], source.CsvValues());
        line.Word(0xFDF8, 0x4000 | 1 << 8);
        Pops(line, left: 0, lowest: 124);
        PollUntil(source, line, 1_000);                                             // the window's end: its state is read once more
        Assert.Equal(["4100", "0", "3968", "0", "1", "0"], source.CsvValues().Skip(2));
        line.Word(0xFDF8, 0x4000 | 1 << 8 | 1 << 12);
        PollUntil(source, line, 1_500);
        Assert.Equal("2", source.CsvValues()[6]);                                   // blip: 0 off, 1 rising, 2 holding
    }

    [Fact] // pops' state is read during a window and once after it, never without one
    public void PopsStateComesWithTheWindowOnly()
    {
        var line = new MemoryLine().WithLayout();
        line.Word(0xFDF8, 0x4000);
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: true);
        PollUntil(source, line, 3_000);
        Assert.DoesNotContain(DiagnosticPayloadSource.PopsAddress, line.Reads);    // no window, no read
        Assert.Equal("", source.CsvValues()[4]);
        line.Word(0xFDF8, 0x4000 | 1 << 3);
        PollUntil(source, line, 5_000);                                             // a 2 s window: a read after each FDF8 read
        Assert.InRange(Count(line, DiagnosticPayloadSource.PopsAddress), 3, 4);
        line.Word(0xFDF8, 0x4000);
        var during = Count(line, DiagnosticPayloadSource.PopsAddress);
        PollUntil(source, line, 8_000);
        Assert.Equal(during + 1, Count(line, DiagnosticPayloadSource.PopsAddress)); // once more after the window, then none
        Assert.Equal("", source.CsvValues()[4]);                                    // and over a second old: empty
    }

    [Fact] // the V10.1 review's minor: 20 paths have no P-code, so their name stands in for P0000
    public void CodesWithoutAPCodeAreNamed()
    {
        var line = new MemoryLine().WithLayout().Stored((124, 0x28), (12, 0x24), (156, 0x21));
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: false);
        PollUntil(source, line, 1_000);
        Assert.Equal("P1567 t_es tam_can", source.Faults);
    }

    [Fact] // the review: one lost reply never leaves a stale state in the log
    public void TheStateCellsEmptyOnceTheLastGoodReadIsASecondOld()
    {
        var line = new MemoryLine().WithLayout(); line.Word(0xFDF8, 0x4000 | 1 << 3);
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: true);
        source.PollPayload();
        Assert.Equal("1", source.CsvValues()[3]);
        line.TimesOut = address => address == 0xFDF8;
        line.Now = 600; source.PollPayload();                                       // a lost reply: the last value, still fresh
        Assert.Equal("1", source.CsvValues()[3]);
        line.Now = 1_001;
        Assert.All(source.CsvValues().Skip(2), value => Assert.Equal("", value));
    }

    [Fact] // RF4: only three state reads in a row with no answer wait 20 s
    public void ThreeStateTimeoutsInARowWait20s()
    {
        var line = new MemoryLine { TimesOut = address => address == 0xFDF8 }.WithLayout();
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: true);
        PollUntil(source, line, 1_400);
        Assert.Equal(3, Count(line, 0xFDF8));                                       // at 0, 0.54 and 1.08 s
        PollUntil(source, line, 19_980);
        Assert.Equal(3, Count(line, 0xFDF8));
        line.TimesOut = _ => false;
        PollUntil(source, line, 21_200);                                            // due again at 21.08 s
        Assert.Equal(4, Count(line, 0xFDF8));
        PollUntil(source, line, 22_300);
        Assert.Equal(6, Count(line, 0xFDF8));
    }

    [Fact] // RF4
    public void AFailedStepEndsTheRoundAndKeepsTheValues()
    {
        var line = new MemoryLine().WithLayout().Stored((124, 0x28));
        var poll = new Poll();
        var source = new DiagnosticPayloadSource(poll, line, overlayState: false);
        PollUntil(source, line, 1_000);
        Assert.Equal("P1567", source.Faults);
        line.TimesOut = address => address == FaultMemory.StoredListAddress;
        line.Stored();
        line.Stored((0, 0x21));
        PollUntil(source, line, 21_000);
        Assert.Equal("P1567", source.Faults);                                       // the round broke at the list: as it was
        line.TimesOut = _ => false;
        PollUntil(source, line, 41_000);
        Assert.Equal("P0103", source.Faults);
        Assert.Equal(41_000 / 60 + 1, poll.Calls);
    }

    [Fact] // the review: FD84 means what it means on MS43X001 only
    public void AnotherLayoutSaysUnsupportedForBothAndIsNotReadAgain()
    {
        var line = new MemoryLine();
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: false);
        PollUntil(source, line, 1_000);
        Assert.Equal(("unsupported", "unsupported"), (source.Faults, source.Queues));
        var reads = line.Reads.Count;
        PollUntil(source, line, 41_000);
        Assert.Equal(reads, line.Reads.Count);
    }

    [Fact] // RF4: an ECU that answers no DS2 06 costs one timed-out read per 20 s, the queue flags'
    public void AnEcuThatAnswersNoMemoryReadCostsOneTimeoutPer20s()
    {
        var line = new MemoryLine { TimesOut = _ => true };
        var source = new DiagnosticPayloadSource(new Poll(), line, overlayState: false);
        PollUntil(source, line, 19_980);
        Assert.Equal([FaultMemory.QueueFlagsAddress], line.Reads);
        PollUntil(source, line, 20_100);
        Assert.Equal(2, line.Reads.Count);
        Assert.Equal(("", ""), (source.Faults, source.Queues));
    }

    [Fact]
    public void TheRequestTimeIsTheLoggersOwnPoll()
    {
        var line = new MemoryLine().WithLayout();
        var timed = new DiagnosticPayloadSource(new Poll(12345), line, overlayState: true);
        timed.PollPayload();
        Assert.Equal(12345L, timed.LastRequestTimestamp);
        var poll = new Poll();
        var source = new DiagnosticPayloadSource(poll, line, overlayState: true);
        source.PollPayload();
        Assert.InRange(source.LastRequestTimestamp!.Value, line.LastRequestAt, poll.StartedAt);
        Assert.Equal(2, source.LastAttempts);
    }
}
