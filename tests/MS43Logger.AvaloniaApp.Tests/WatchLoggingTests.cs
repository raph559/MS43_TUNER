using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The logger reads the overlay header after the connect: V9 polls with the watch, anything else exactly as today.</summary>
public sealed class WatchLoggingTests
{
    private static readonly byte[] StockPoll = [0x12, 0x05, 0x0B, 0xB0, 0xAC];

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

    /// <summary>An ECU whose overlay header (DS2 06 at 0C:B544) reads <paramref name="version"/>; V9 answers subs 11 and 12.</summary>
    private static Func<byte[], byte[]?> Overlay(int version, int stockBytes) => frame =>
    {
        if (frame is [0x12, 0x09, 0x06, 0x00, 0x0C, 0xB5, 0x44, 0x10, _])
        {
            var header = new byte[16]; "LTOV"u8.CopyTo(header); header[4] = (byte)version;
            header[14] = (byte)(0xD0 + version); header[15] = 0xC0;
            return Frame([0xA0, .. header]);
        }
        if (version >= 9 && frame is [0x12, 0x08, 0x6D, 0x0C, var a2, var a1, var a0, _]) return Frame(0xA0, a2, a1, a0);
        if (version >= 9 && frame is [0x12, 0x05, 0x6D, 0x0B, _])
            return Frame([0xA0, .. new byte[stockBytes], 1, 3, 0, 2, 0x80, 0, 0, 4, 0x40, 0, 12]);
        return null;
    };

    private static (MainWindowViewModel Live, EcuPort Port) Live(Func<byte[], byte[]?>? other)
    {
        var port = new EcuPort(BundledDefinitions.Load("ms43x")) { Other = other };
        var live = new MainWindowViewModel(new ManualTime(), () => ["COM1"])
        {
            SelectedVariant = "ms43x", SelectedPort = "COM1",
            SessionFactory = (definition, _) => new Ds2Session(definition, port),
        };
        return (live, port);
    }

    private static int StockBytes => BundledDefinitions.Load("ms43x").GetMonitorExchange().Response.PacketSize;

    [AvaloniaFact]
    public async Task AV8EcuIsPolledExactlyAsToday()
    {
        var (live, port) = Live(Overlay(8, StockBytes));
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 3, "three samples");
        live.Disconnect();
        Assert.Null(live.Watch);
        var polls = port.Written.Where(f => f.Length > 2 && f[2] != 0x91 && f[2] != 0x06).ToList();
        Assert.NotEmpty(polls);
        Assert.All(polls, f => Assert.Equal(StockPoll, f));
    }

    [AvaloniaFact]
    public async Task AStockEcuWithNoOverlayIsPolledExactlyAsToday()
    {
        var (live, port) = Live(null);                              // the header read gets no answer
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 3, "three samples");
        live.Disconnect();
        Assert.Null(live.Watch);
        Assert.All(port.Written.Where(f => f.Length > 2 && f[2] is not (0x91 or 0x06)), f => Assert.Equal(StockPoll, f));
    }

    [AvaloniaFact] // an overlay this app does not know may log differently: it gets the stock poll
    public async Task AnUnknownLaterOverlayIsPolledExactlyAsToday()
    {
        var (live, port) = Live(Overlay(11, StockBytes));
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 3, "three samples");
        live.Disconnect();
        Assert.Null(live.Watch);
        Assert.All(port.Written.Where(f => f.Length > 2 && f[2] is not (0x91 or 0x06)), f => Assert.Equal(StockPoll, f));
    }

    private async Task PolledWithTheWatch(int version)
    {
        var (live, port) = Live(Overlay(version, StockBytes));
        LiveSample? last = null;
        live.SampleShown += (_, sample) => last = sample;
        await live.ConnectAsync();
        Assert.NotNull(live.Watch);
        live.Watch!.SetWatch(0x775D4);
        await Until(() => live.Watch?.Watched == 0x775D4 && last?.WatchedAddress == 0x775D4, "a sample of the watched map");
        live.Disconnect();
        Assert.Equal(new WatchRecord(WatchKind.MapIndices, 3, 2, 0x8000, 4, 0x4000, 12), last!.Watch);
        Assert.DoesNotContain(port.Written, f => f.SequenceEqual(StockPoll));
        Assert.Contains(port.Written, f => f is [0x12, 0x08, 0x6D, 0x0C, 0x07, 0x75, 0xD4, _]);
    }

    [AvaloniaFact] // V8's rule: with the overlay quiet, its live settings go back to flash after 3 s; V9's log frames must not keep it alive
    public async Task AQuietOverlayIsLoggedWithTheStockPollUntilANewSession()
    {
        var (live, port) = Live(Overlay(9, StockBytes));
        await live.ConnectAsync();
        await Until(() => live.SampleCount >= 3, "three samples");
        live.QuietOverlay(true);
        var from = port.Written.Count;
        var count = live.SampleCount;
        await Until(() => live.SampleCount >= count + 3, "three quiet samples");
        var quiet = port.Written.Skip(from + 1).ToList();                      // the poll in flight may still be sub 11
        Assert.DoesNotContain(quiet, f => f.Length > 2 && f[2] == 0x6D);
        Assert.Contains(quiet, f => f.SequenceEqual(StockPoll));

        live.QuietOverlay(false);
        from = port.Written.Count;
        await Until(() => port.Written.Skip(from).Any(f => f is [0x12, 0x05, 0x6D, 0x0B, _]), "sub 11 again");
        live.Disconnect();
    }

    [CarAvaloniaFact]
    public async Task AStoppedLiveTuningSessionQuietsTheOverlay()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTime());
        try
        {
            ReadOnlySpan<byte> header = [(byte)'L', (byte)'T', (byte)'O', (byte)'V', 10, 4], pops = OverlayV104Settings.Defaults;
            for (var i = 0; i < header.Length; i++) shell.Tune.Document!.Image.WriteElement(new XdfData(0x4B544 + i, 8, false, true, 1, 1, null), 0, header[i]);
            for (var i = 0; i < pops.Length; i++) shell.Tune.Document!.Image.WriteElement(new XdfData(0x4FF80 + i, 8, false, true, 1, 1, null), 0, pops[i]);
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var ecu = new StoppingLine(new FakeOverlayEcu(shell.Tune.Document!.Image.ToArray()));
            await shell.LiveTuning.AttachLine(ecu);
            Assert.False(shell.Live.OverlayQuiet);
            ecu.Fail = true;
            await shell.LiveTuning.TickAsync();
            Assert.True(shell.Live.OverlayQuiet);
        }
        finally { shell.LiveTuning.Dispose(); shell.Fun.Dispose(); window.Close(); }
    }

    /// <summary>A line that breaks: every request throws <see cref="IOException"/> once <see cref="Fail"/> is set.</summary>
    private sealed class StoppingLine(IDs2Line inner) : IDs2Line
    {
        public bool Fail { get; set; }
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400) =>
            Fail ? throw new IOException("The port was closed.") : inner.Request(payload, listenMilliseconds);
    }
}
