using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>The fault memory read over DS2 command 06: list A (stored), each path's state record, the layout check.</summary>
public sealed class FaultMemoryTests
{
    /// <summary>Answers DS2 06 reads from a byte map; every address not set reads 0.</summary>
    private sealed class MemoryLine : IDs2Line
    {
        public Dictionary<int, byte> Memory { get; } = new();
        public List<int> Reads { get; } = new();

        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            Assert.Equal(0x06, payload[0]);
            var address = payload[2] << 16 | payload[3] << 8 | payload[4];
            int n = payload[5];
            Reads.Add(address);
            var frame = new byte[n + 4];
            frame[0] = 0x12; frame[1] = (byte)frame.Length; frame[2] = 0xA0;
            for (var i = 0; i < n; i++) frame[3 + i] = Memory.GetValueOrDefault(address + i);
            for (var i = 0; i < frame.Length - 1; i++) frame[^1] ^= frame[i];
            return frame;
        }

        /// <summary>The MS43X001 layout: each path's DS2 code at the start of its descriptor in flash.</summary>
        public MemoryLine WithLayout()
        {
            for (var p = 0; p < FaultPaths.All.Count; p++) Memory[FaultMemory.DescriptorsAddress + 22 * p] = FaultPaths.All[p].Ds2Code;
            return this;
        }

        public MemoryLine Stored(params (int Path, byte[] Record)[] faults)
        {
            Memory[FaultMemory.StoredCountAddress] = (byte)faults.Length;
            for (var i = 0; i < faults.Length; i++)
            {
                Memory[FaultMemory.StoredListAddress + i] = (byte)faults[i].Path;
                for (var b = 0; b < faults[i].Record.Length; b++) Memory[FaultMemory.RecordsAddress + 12 * faults[i].Path + b] = faults[i].Record[b];
            }
            return this;
        }
    }

    // status: bits 0-3 the type bits, 5 stored, 6 present now, 7 sporadic; then debounce, frequency, heal.
    private static byte[] Record(byte status, byte frequency = 1) => [status, 0, frequency, 0x28, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void ReadsTheStoredFaultsOldestFirst()
    {
        var line = new MemoryLine().WithLayout().Stored((124, Record(0x20 | 0x40 | 0x08, 3)), (0, Record(0x20 | 0x01)));
        var faults = FaultMemory.ReadStored(line);
        Assert.Equal(2, faults.Count);
        Assert.Equal(("msw_tog", "P1567", 3, true, false, 3), (faults[0].Name, faults[0].PCode, faults[0].Type, faults[0].Present, faults[0].Sporadic, faults[0].Frequency));
        Assert.Equal(("maf", "P0103", 0, false, true), (faults[1].Name, faults[1].PCode, faults[1].Type, faults[1].Present, faults[1].Mil));
        Assert.Equal(0x2D, faults[0].Ds2Code);
    }

    [Fact]
    public void AnEmptyMemoryHasNoFaults()
    {
        Assert.Empty(FaultMemory.ReadStored(new MemoryLine().WithLayout().Stored()));
    }

    [Theory]
    [InlineData(0x21, 0, "P0103")] [InlineData(0x22, 1, "P0102")] [InlineData(0x24, 2, "P0102")] [InlineData(0x28, 3, "P0102")]
    [InlineData(0x20, 3, "P0102")] [InlineData(0x26, 1, "P0102")]
    public void TheTypeIsTheLowestTypeBitAndPicksItsPCode(byte status, int type, string pcode)
    {
        var fault = Assert.Single(FaultMemory.ReadStored(new MemoryLine().WithLayout().Stored((0, Record(status)))));
        Assert.Equal((type, pcode), (fault.Type, fault.PCode));
    }

    [Fact]
    public void SporadicIsBitSeven()
    {
        var fault = Assert.Single(FaultMemory.ReadStored(new MemoryLine().WithLayout().Stored((1, Record(0x80 | 0x20 | 0x04)))));
        Assert.True(fault.Sporadic);
        Assert.False(fault.Present);
    }

    [Fact]
    public void AnotherFaultMemoryLayoutIsRefusedBeforeAnyListIsRead()
    {
        var line = new MemoryLine().Stored((124, Record(0x28)));
        Assert.Throws<NotSupportedException>(() => FaultMemory.ReadStored(line));
        Assert.DoesNotContain(FaultMemory.StoredListAddress, line.Reads);
    }

    [Fact]
    public void AnOutOfRangePathIndexIsSkipped()
    {
        var line = new MemoryLine().WithLayout().Stored((124, Record(0x28)));
        line.Memory[FaultMemory.StoredCountAddress] = 2;
        line.Memory[FaultMemory.StoredListAddress + 1] = 200;
        Assert.Equal("msw_tog", Assert.Single(FaultMemory.ReadStored(line)).Name);
    }

    [Fact]
    public void ThreePathsHaveNoXdfNameAndShowTheirIndex()
    {
        var fault = Assert.Single(FaultMemory.ReadStored(new MemoryLine().WithLayout().Stored((83, Record(0x28)))));
        Assert.Equal("path 83", fault.Label);
    }

    [Theory]
    [InlineData(0x00, 0x00, "")] [InlineData(0x40, 0x00, "1")] [InlineData(0x80, 0x00, "2")] [InlineData(0x00, 0x01, "3")]
    [InlineData(0xC0, 0x01, "1,2,3")] [InlineData(0x3F, 0xFE, "")]
    public void TheQueueOverflowsAreFd84Bits6To8(byte low, byte high, string queues)
    {
        var line = new MemoryLine();
        line.Memory[FaultMemory.QueueFlagsAddress] = low;
        line.Memory[FaultMemory.QueueFlagsAddress + 1] = high;
        Assert.Equal(queues, string.Join(',', FaultMemory.ReadQueueOverflows(line)));
    }

    [RealFileFact(TestData.CarBin)]
    public void TheTableMatchesTheCarFilesDescriptors()
    {
        var bin = File.ReadAllBytes(TestData.Find(TestData.CarBin)!);
        int Word(int at) => bin[at] | bin[at + 1] << 8;
        for (var p = 0; p < FaultPaths.All.Count; p++)
        {
            var descriptor = 0x6D08E + 22 * p;
            Assert.Equal(bin[descriptor], FaultPaths.All[p].Ds2Code);
            for (var t = 0; t < 4; t++) Assert.Equal(Word(0x70000 + Word(descriptor + 2 + 2 * t)), FaultPaths.All[p].Words[t]);
        }
    }
}
