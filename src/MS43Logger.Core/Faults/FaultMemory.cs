using System.Numerics;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Faults;

/// <summary>
/// One stored fault. Type: 0 above maximum, 1 below minimum, 2 no signal, 3 implausible. Present: the fault is there now;
/// Sporadic: it has been absent at least once. Mil: the path asks for the check-engine light.
/// </summary>
public sealed record EcuFault(int Path, string? Name, byte Ds2Code, int Type, string PCode, bool Present, bool Sporadic,
    bool Mil, int Frequency)
{
    public string Label => Name ?? $"path {Path}";

    public string TypeText => Type switch { 0 => "above maximum", 1 => "below minimum", 2 => "no signal", _ => "implausible" };
}

/// <summary>
/// Read-only MS43X001 fault memory over DS2 command 06. List A, the
/// stored paths oldest first, is at DPP2:9974h with its count at 81BBh; each path's 12-byte state record is at 91DAh + 12 x
/// path. Physical: 41974h, 401BBh, 411DAh. The layout is recognised by each path's DS2 code at its descriptor in flash
/// (0ED08Eh + 22 x path), checked on the first ten; another layout reads nothing.
/// </summary>
public static class FaultMemory
{
    public const int StoredListAddress = 0x41974, StoredCountAddress = 0x401BB, RecordsAddress = 0x411DA, DescriptorsAddress = 0xED08E;
    public const int RecordSize = 12;
    private const int DescriptorSize = 22, LayoutCheck = 10;

    /// <summary>The descriptor bytes the layout check reads from <see cref="DescriptorsAddress"/>: the first ten paths'.</summary>
    public const int LayoutBytes = DescriptorSize * LayoutCheck;

    /// <summary>Whether <paramref name="descriptors"/> (<see cref="LayoutBytes"/> from <see cref="DescriptorsAddress"/>) are MS43X001's.</summary>
    public static bool MatchesLayout(ReadOnlySpan<byte> descriptors)
    {
        var paths = FaultPaths.All;
        if (descriptors.Length < LayoutBytes) return false;
        for (var p = 0; p < LayoutCheck; p++)
            if (descriptors[DescriptorSize * p] != paths[p].Ds2Code) return false;
        return true;
    }

    /// <summary>
    /// The firmware's sticky overflow flags for its three task queues, FD84 bits 6, 7 and 8 for Q1 > Q2 > Q3: a queue was full
    /// and its oldest job was dropped. They are set by running out of CPU time, and stay set until the next key-on.
    /// </summary>
    public const int QueueFlagsAddress = 0xFD84;

    /// <summary>The queues that have overflowed since key-on: 1, 2 or 3; empty when none has.</summary>
    public static IReadOnlyList<int> ReadQueueOverflows(IDs2Line line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var flags = line.Read(QueueFlagsAddress, 2);
        return QueueOverflows(flags[0] | flags[1] << 8);
    }

    /// <summary>The queues whose overflow flag is set in the word at <see cref="QueueFlagsAddress"/>.</summary>
    public static IReadOnlyList<int> QueueOverflows(int word) => new[] { 1, 2, 3 }.Where(queue => (word & 1 << (5 + queue)) != 0).ToArray();

    /// <summary>The stored faults, oldest first, each path once. Throws <see cref="NotSupportedException"/> on another layout.</summary>
    public static IReadOnlyList<EcuFault> ReadStored(IDs2Line line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var paths = FaultPaths.All;
        if (!MatchesLayout(line.Read(DescriptorsAddress, LayoutBytes)))                    // two requests, not 29
            throw new NotSupportedException("This ECU's fault memory is not laid out as MS43X001's; nothing was read.");

        int count = line.Read(StoredCountAddress, 1)[0];
        if (count == 0) return Array.Empty<EcuFault>();
        var list = line.Read(StoredListAddress, Math.Min(count, paths.Count));
        var faults = new List<EcuFault>();
        foreach (var path in list.Select(b => (int)b).Where(p => p < paths.Count).Distinct())
            faults.Add(Decode(path, line.Read(RecordsAddress + RecordSize * path, RecordSize)));
        return faults;
    }

    /// <summary>
    /// A path's state record: status (bits 0-3 the type bits, 5 stored, 6 present now, 7 sporadic), debounce, frequency, heal,
    /// environment, time stamp, OBD byte, test counter. The type is the lowest set type bit, or 3 when none is set.
    /// </summary>
    public static EcuFault Decode(int path, ReadOnlySpan<byte> record)
    {
        var entry = FaultPaths.All[path];
        var typeBits = record[0] & 0x0F;
        var type = typeBits == 0 ? 3 : BitOperations.TrailingZeroCount(typeBits);
        return new EcuFault(path, entry.Name, entry.Ds2Code, type, $"P{entry.Words[type] & 0x3FFF:X4}",
            Present: (record[0] & 0x40) != 0, Sporadic: (record[0] & 0x80) != 0, Mil: (entry.Words[0] & 0x8000) != 0, Frequency: record[2]);
    }
}
