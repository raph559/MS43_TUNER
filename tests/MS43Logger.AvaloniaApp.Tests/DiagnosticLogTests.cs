using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The CSV log carries the stock payload's overrun, cut, brake and
/// clutch bits on every row, the fault memory, and on a V10 ECU the pops and blip state, so a log explains why pops did or
/// did not come. The columns are always there, as wide on every row as the header; empty where unread. Without a log, or
/// while a Performance run records, the ECU gets the bare poll.
/// </summary>
public sealed class DiagnosticLogTests : IDisposable
{
    private readonly string _folder = TestShell.NewFolder();

    public DiagnosticLogTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting until " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static byte[] Frame(params byte[] payload)
    {
        var frame = new byte[payload.Length + 3]; frame[0] = 0x12; frame[1] = (byte)frame.Length;
        payload.CopyTo(frame, 2); frame[^1] = frame.Take(frame.Length - 1).Aggregate((byte)0, (a, b) => (byte)(a ^ b));
        return frame;
    }

    private static int StockBytes => BundledDefinitions.Load("ms43x").GetMonitorExchange().Response.PacketSize;

    /// <summary>
    /// An ECU that answers DS2 06 from <paramref name="memory"/> (0 where unset): with <paramref name="overlay"/>, a V10.1 header
    /// at 0C:B544 and the overlay's log poll (6D 0B), whose stock payload starts with <paramref name="byte0"/>; without, no
    /// header, so the app logs with the stock poll.
    /// </summary>
    private static Func<byte[], byte[]?> Ecu(Dictionary<int, byte> memory, bool overlay, byte byte0 = 0) => frame =>
    {
        if (frame is [0x12, 0x09, 0x06, 0x00, var a2, var a1, var a0, var n, _])
        {
            var address = a2 << 16 | a1 << 8 | a0;
            if (address == 0x0CB544)
            {
                if (!overlay) return null;
                var header = new byte[16]; "LTOV"u8.CopyTo(header); header[4] = OverlayV10.Version; header[5] = OverlayV10.Minor; header[14] = 0xDA; header[15] = 0xC0;
                return Frame([0xA0, .. header]);
            }
            return Frame([0xA0, .. Enumerable.Range(address, n).Select(a => memory.GetValueOrDefault(a))]);
        }
        if (overlay && frame is [0x12, 0x05, 0x6D, 0x0B, _])
        {
            var stock = new byte[StockBytes]; stock[0] = byte0;
            return Frame([0xA0, .. stock, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        }
        return null;
    };

    /// <summary>The MS43X001 fault memory with P1567 stored, queue 2 overflowed, FDF8 as given, and pops' state: 4,000 rpm.</summary>
    private static Dictionary<int, byte> Memory(ushort fdf8)
    {
        var memory = new Dictionary<int, byte>();
        for (var p = 0; p < FaultPaths.All.Count; p++) memory[FaultMemory.DescriptorsAddress + 22 * p] = FaultPaths.All[p].Ds2Code;
        memory[FaultMemory.StoredCountAddress] = 1; memory[FaultMemory.StoredListAddress] = 124; memory[FaultMemory.RecordsAddress + 12 * 124] = 0x28;
        memory[FaultMemory.QueueFlagsAddress] = 1 << 7;
        memory[0xFDF8] = (byte)fdf8; memory[0xFDF9] = (byte)(fdf8 >> 8);
        memory[DiagnosticPayloadSource.PopsAddress + 2] = 125;
        return memory;
    }

    private (MainWindowViewModel Live, EcuPort Port) Live(Func<byte[], byte[]?> ecu, bool logging = true)
    {
        var port = new EcuPort(BundledDefinitions.Load("ms43x")) { Other = ecu };
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x", SelectedPort = "COM1", LogFolder = _folder, EnableCsvLogging = logging,
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
        };
        live.CsvPath = live.NextCsvPath;
        return (live, port);
    }

    private async Task<string[]> Log(Func<byte[], byte[]?> ecu, int samples = 16)
    {
        var (live, _) = Live(ecu);
        var path = live.CsvPath;
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= samples, $"{samples} samples");
        live.Disconnect();
        return File.ReadAllLines(path);
    }

    /// <summary>The DS2 06 reads after the connect's header read: the log's own.</summary>
    private static List<int> LogReads(EcuPort port) => port.Written
        .Where(f => f is [0x12, 0x09, 0x06, 0x00, _, _, _, _, _]).Select(f => f[4] << 16 | f[5] << 8 | f[6])
        .Where(address => address != 0x0CB544).ToList();

    private static Dictionary<string, string> Columns(string header, string row) =>
        header.Split(',').Zip(row.Split(',')).ToDictionary(pair => pair.First, pair => pair.Second);

    [AvaloniaFact]
    public async Task AV10LogCarriesTheFaultsThePayloadBitsAndThePopsAndBlipState()
    {
        var lines = await Log(Ecu(Memory(fdf8: 0x4000 | 1 << 3 | 1 << 7 | 1 << 8 | 1 << 12), overlay: true, byte0: 0x20 | 0x80));
        Assert.Contains(",PUSTATE,PUCSTATE,BTSSTATE,BLSSTATE,CSSTATE,faults,queues,fdf8,pops_window,pops_low_rpm,pops_used,blip,guard",
            lines[0]);
        var width = lines[0].Split(',').Length;
        Assert.All(lines.Skip(1), line => Assert.Equal(width, line.Split(',').Length));
        var last = Columns(lines[0], lines[^1]);
        Assert.Equal(("0", "0", "1", "0", "1"), (last["PUSTATE"], last["PUCSTATE"], last["BTSSTATE"], last["BLSSTATE"], last["CSSTATE"]));
        Assert.Equal(("P1567", "2", "5188"), (last["faults"], last["queues"], last["fdf8"]));
        Assert.Equal(("1", "4000"), (last["pops_window"], last["pops_low_rpm"]));
        Assert.Equal(("0", "2", "0"), (last["pops_used"], last["blip"], last["guard"]));
    }

    [AvaloniaFact] // a stock ECU: the payload bits and the fault memory, no pops or blip state
    public async Task AStockLogCarriesTheFaultsAndLeavesThePopsStateEmpty()
    {
        var lines = await Log(Ecu(Memory(fdf8: 0x4000), overlay: false));
        var last = Columns(lines[0], lines[^1]);
        Assert.Equal(("P1567", "2", "0"), (last["faults"], last["queues"], last["CSSTATE"]));
        Assert.All(new[] { "fdf8", "pops_window", "pops_low_rpm", "pops_used", "blip", "guard" },
            column => Assert.Equal("", last[column]));
    }

    [AvaloniaFact] // without a log the reads are for nothing, so the ECU gets the bare poll
    public async Task WithoutALogTheEcuGetsOnlyThePoll()
    {
        var (live, port) = Live(Ecu(Memory(fdf8: 0x4000), overlay: true), logging: false);
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 16, "16 samples");
        live.Disconnect();
        Assert.Empty(LogReads(port));
    }

    [AvaloniaFact] // a Performance run's timing is the poll's own
    public async Task WhileAPerformanceRunRecordsTheEcuGetsOnlyThePoll()
    {
        var (live, port) = Live(Ecu(Memory(fdf8: 0x4000), overlay: true));
        var recording = true;
        live.DiagnosticsPaused = () => recording;
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 16, "16 samples");
        Assert.Empty(LogReads(port));
        recording = false;
        var from = live.SampleCount;
        await Until(() => live.SampleCount >= from + 16, "16 more samples");
        live.Disconnect();
        Assert.Contains(0xFDF8, LogReads(port));
    }
}
