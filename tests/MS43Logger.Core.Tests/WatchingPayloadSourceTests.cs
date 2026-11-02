using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>With the overlay the logger polls with sub 11 and keeps the watch record each reply carries.</summary>
public sealed class WatchingPayloadSourceTests
{
    private static FakeOverlayV4Ecu Ecu() => new(FakeOverlayEcu.Image(), 9);

    private static List<byte[]> Overlay(FakeOverlayV4Ecu ecu) => ecu.Requests.Where(r => r[0] == 0x6D).ToList();

    private static readonly WatchRecord A = new(WatchKind.MapIndices, 7, 3, 0x8000, 5, 0x4000, 12);

    [Fact]
    public void PollsWithSub11AndReturnsTheStockPayload()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        Assert.Equal(ecu.StockPayload, source.PollPayload());
        Assert.Equal([0x6D, 0x0B], ecu.Requests[^1]);
        Assert.NotNull(source.LastRequestTimestamp);
    }

    [Fact]
    public void AWatchIsSentOnceBeforeTheNextPoll()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        source.SetWatch(0x775D4);
        source.PollPayload(); source.PollPayload();
        Assert.Equal([[0x6D, 0x0C, 0x07, 0x75, 0xD4], [0x6D, 0x0B], [0x6D, 0x0B]], Overlay(ecu));
        Assert.Equal(0x775D4u, source.Watched);
        source.SetWatch(null); source.PollPayload();
        Assert.Equal([0x6D, 0x0C, 0, 0, 0], Overlay(ecu)[^2]);
        Assert.Null(source.Watched);
    }

    [Fact] // the emulator lost the first request after the speed switch: the watch goes again until it is acknowledged
    public void AWatchIsSentAgainUntilAcknowledged()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        ecu.DropWatches = 1;
        source.SetWatch(0x775D4);
        source.PollPayload();
        Assert.Null(source.Watched);
        source.PollPayload();
        Assert.Equal(0x775D4u, source.Watched);
        Assert.Equal(2, Overlay(ecu).Count(r => r[1] == 0x0C));
    }

    [Fact]
    public void TheRecordComesWithTheAddressWatchedWhenItsPollWentOut()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        source.SetWatch(0x775D4); source.PollPayload();
        ecu.LogRecord = A;
        source.PollPayload();
        Assert.Equal(A, source.LastRecord);
        Assert.Equal(0x775D4u, source.LastRecordWatched);
    }

    [Fact] // RF4
    public void NoRecordFromBeforeTheNewWatchsAck()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        source.SetWatch(0x775D4); source.PollPayload();
        ecu.LogRecord = A;
        ecu.BeforeLogReply = () => { source.SetWatch(0x77315); ecu.BeforeLogReply = null; };  // the screen changes map mid-poll
        source.PollPayload();
        Assert.Equal(A, source.LastRecord);
        Assert.Equal(0x775D4u, source.LastRecordWatched);                                     // A's record stays A's
        source.PollPayload();
        Assert.Equal(0x77315u, source.LastRecordWatched);
        Assert.Equal(WatchKind.None, source.LastRecord!.Kind);                               // nothing read since the new watch
    }

    [Fact]
    public void TheLoggersSamplesCarryTheRecord()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        source.SetWatch(0x775D4); source.PollPayload();
        ecu.LogRecord = A;
        source.PollPayload();
        Assert.Equal((A, (uint?)0x775D4), LiveLogger.WatchOf(source));
        Assert.Equal((null, null), LiveLogger.WatchOf(new WatchingPayloadSourceTestsStock()));
    }

    [Fact] // V10.1's log: the diagnostic columns wrap the logger's source, and its samples still carry the watch record
    public void TheRecordComesThroughTheDiagnosticSource()
    {
        var ecu = Ecu(); var source = new WatchingPayloadSource(ecu);
        var logged = new DiagnosticPayloadSource(source, ecu, overlayState: false);
        source.SetWatch(0x775D4); logged.PollPayload();
        ecu.LogRecord = A;
        Assert.Equal(ecu.StockPayload, logged.PollPayload());
        Assert.Equal((A, (uint?)0x775D4), LiveLogger.WatchOf(logged));
    }

    [Fact] // V8's rule: live settings go back to flash 3 s after the overlay hears nothing, so a quiet source sends it nothing
    public void AQuietSourceLogsWithTheStockPollAndSendsTheOverlayNothing()
    {
        var ecu = Ecu(); var stock = new WatchingPayloadSourceTestsStock(); var source = new WatchingPayloadSource(ecu, stock);
        source.SetWatch(0x775D4); source.PollPayload();
        ecu.LogRecord = A; source.PollPayload();
        var before = Overlay(ecu).Count;

        source.Quiet = true;
        source.SetWatch(0x77315);
        Assert.Equal(new byte[73], source.PollPayload());
        Assert.Equal(1, stock.Polls);
        Assert.Equal(before, Overlay(ecu).Count);
        Assert.Null(source.LastRecord);
        Assert.Null(source.LastRecordWatched);

        source.Quiet = false;
        source.PollPayload();
        Assert.Equal([[0x6D, 0x0C, 0x07, 0x73, 0x15], [0x6D, 0x0B]], Overlay(ecu).Skip(before));
    }

    private sealed class WatchingPayloadSourceTestsStock : IPayloadSource
    {
        public int Polls { get; private set; }
        public byte[] PollPayload() { Polls++; return new byte[73]; }
    }
}
