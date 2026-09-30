using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class LiveTuningSessionTests
{
    private static async Task<(FakeOverlayEcu Ecu, LiveTuningSession Session)> Started(Action<FakeOverlayEcu>? before = null)
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        before?.Invoke(ecu);
        var session = new LiveTuningSession(ecu);
        await session.StartAsync();
        await session.TickAsync();
        return (ecu, session);
    }

    private static byte[] Plus(LiveTuningSession session, int table, int steps) =>
        session.Detection!.Flash[table].Select(b => (byte)(b + steps)).ToArray();

    [Fact]
    public async Task Tick_SendsAStatus_AndRaisesStatusChanged()
    {
        var (ecu, session) = await Started();
        OverlayStatus? seen = null;
        session.StatusChanged += (_, status) => seen = status;
        var sent = ecu.Requests.Count;

        await session.TickAsync();

        Assert.Equal(new byte[] { 0x6D, 0x00 }, ecu.Requests[sent]);
        Assert.NotNull(seen);
        Assert.Same(seen, session.Last);
        Assert.True(seen!.Running);
    }

    [Fact]
    public async Task WithoutTheOverlay_NothingIsSent()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image(), hasOverlay: false);
        var session = new LiveTuningSession(ecu);
        Assert.Equal("not found", (await session.StartAsync()).Problem);
        await session.TickAsync();
        Assert.DoesNotContain(ecu.Requests, r => r[0] == OverlayContract.Command);
    }

    [Fact]
    public async Task Send_PutsTheMapLive_AndRemembersIt()
    {
        var (ecu, session) = await Started();
        var copy = Plus(session, 1, 2);

        Assert.Equal(new SendResult(null, null), await session.SendAsync(1, copy));

        Assert.Equal(MapIntent.Live, session.Intent(1));
        Assert.Equal(copy, session.SentCopy(1));
        Assert.Equal(OverlayState.On, session.Last!.Tables[1].State);
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
    }

    [Fact]
    public async Task AfterAKeyCycle_WhatWasSentIsSentAgain_AndLiveGoesBackOn()
    {
        var (ecu, session) = await Started();
        var copy = Plus(session, 1, 2);
        await session.SendAsync(1, copy);

        ecu.KeyCycle();
        await session.TickAsync();
        Assert.Equal(OverlayState.Free, session.Last!.Tables[1].State);    // stopped: nothing can go on yet
        ecu.Running = true;
        await session.TickAsync();

        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        Assert.Equal(copy, new OverlayClient(ecu).ReadCopy(1, ecu.Peek().Tables[1]));
        Assert.Equal(OverlayState.On, session.Last!.Tables[1].State);
    }

    [Fact]
    public async Task AfterAStall_LiveMapsGoBackOnOnceTheEngineRuns()
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));

        ecu.Stall();
        await session.TickAsync();
        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[1].State);
        ecu.Running = true;
        await session.TickAsync();

        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
    }

    [Fact]
    public async Task ASentButOffMap_ComesBackOff()
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));
        Assert.Null(await session.SetOnAsync(1, false));
        Assert.Equal(MapIntent.SentOff, session.Intent(1));

        ecu.KeyCycle();
        ecu.Running = true;
        await session.TickAsync();

        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[1].State);
        Assert.Equal(MapIntent.SentOff, session.Intent(1));
    }

    [Fact]
    public async Task SendWhileStopped_IsRefusedAndShown()                       // Review Focus 2
    {
        var (ecu, session) = await Started();
        ecu.Stall();

        var result = await session.SendAsync(1, Plus(session, 1, 2));

        Assert.Equal(OverlayReason.NotRunning, result.Refused);
        Assert.Single(ecu.Requests, r => r is [0x6D, 0x03, ..]);
        Assert.Equal(MapIntent.None, session.Intent(1));
        await session.TickAsync();
        Assert.Single(ecu.Requests, r => r is [0x6D, 0x03, ..]);                // no retry on the next tick
    }

    [Fact]
    public async Task MapsTheAppDidNotSend_AreAdopted()                          // Review Focus 4
    {
        var flash = FakeOverlayEcu.Image().AsSpan(OverlayContract.Maps[1].Address, 36).ToArray();
        var copy = flash.Select(b => (byte)(b + 1)).ToArray();
        var (_, session) = await Started(ecu =>
        {
            ecu.Request(OverlayContract.Load(1, 100));
            ecu.Request(OverlayContract.Write(1, 0, copy));
            ecu.Request(OverlayContract.On(1, OverlayContract.Crc32(copy)));
            ecu.Request(OverlayContract.Load(3, 200));
        });

        Assert.Equal(MapIntent.Live, session.Intent(1));
        Assert.Equal(copy, session.SentCopy(1));
        Assert.Equal(MapIntent.SentOff, session.Intent(3));
        Assert.Equal(MapIntent.None, session.Intent(0));
    }

    [Fact]
    public async Task APortError_StopsTheSessionAndReports()                     // Review Focus 5
    {
        var (ecu, session) = await Started();
        Exception? stopped = null;
        session.Stopped += (_, error) => stopped = error;
        ecu.FailNext = new IOException("The port is closed.");

        await session.TickAsync();

        Assert.True(session.IsStopped);
        Assert.IsType<IOException>(stopped);
        Assert.Contains("The port is closed.", session.Problem);
        var sent = ecu.Requests.Count;
        await session.TickAsync();
        Assert.Equal(sent, ecu.Requests.Count);
    }

    [Fact]
    public async Task ATimeout_IsNotFatal()
    {
        var (ecu, session) = await Started();
        ecu.FailNext = new TimeoutException();
        await session.TickAsync();
        Assert.False(session.IsStopped);
        await session.TickAsync();
        Assert.NotNull(session.Last);
    }

    [Fact]
    public async Task Revert_ForgetsEveryIntent()
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));
        await session.SendAsync(3, Plus(session, 3, 1));

        Assert.Null(await session.RevertAsync());

        Assert.All(Enumerable.Range(0, 5), i => Assert.Equal(MapIntent.None, session.Intent(i)));
        Assert.All(ecu.Peek().Tables, t => Assert.Equal(OverlayState.Free, t.State));
        ecu.KeyCycle();
        ecu.Running = true;
        await session.TickAsync();
        Assert.All(ecu.Peek().Tables, t => Assert.Equal(OverlayState.Free, t.State));
    }

    [Fact]
    public async Task TheBigSwitch_FlipsWhatWasSent()
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));
        await session.SendAsync(3, Plus(session, 3, 1));

        Assert.Null(await session.SetAllAsync(false));
        Assert.Equal(new[] { MapIntent.SentOff, MapIntent.SentOff }, new[] { session.Intent(1), session.Intent(3) });
        Assert.Null(await session.SetAllAsync(true));
        Assert.Equal(new[] { OverlayState.On, OverlayState.On }, new[] { ecu.Peek().Tables[1].State, ecu.Peek().Tables[3].State });
        Assert.Equal(MapIntent.Live, session.Intent(3));
    }

    [Fact]
    public async Task ASentOffMap_LeftOnByALostOff_IsSwitchedOffNextTick()   // final review I2a
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));
        await session.SetOnAsync(1, false);
        ecu.KeyCycle();
        ecu.Running = true;
        var lost = true;
        ecu.FailWhen = r => r is [0x6D, 0x04, ..] && lost && !(lost = false) ? new TimeoutException() : null;

        await session.TickAsync();                                           // sent again and on; its off is lost
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        await session.TickAsync();

        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[1].State);
        Assert.Equal(MapIntent.SentOff, session.Intent(1));
    }

    [Fact]
    public async Task ALoadedMapItHadSent_IsPutBack()                           // final review I2b
    {
        var (ecu, session) = await Started();
        var v1 = Plus(session, 1, 2);
        await session.SendAsync(1, v1);
        ecu.Stall();
        Assert.Equal(OverlayReason.NotRunning, (await session.SendAsync(1, Plus(session, 1, 3))).Refused);
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[1].State);       // the ECU holds the refused copy, unconfirmed

        ecu.Running = true;
        await session.TickAsync();

        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        Assert.Equal(v1, new OverlayClient(ecu).ReadCopy(1, ecu.Peek().Tables[1]));
    }

    [Fact]
    public async Task AMalformedReply_DoesNotStopTheSession()                   // final review I6
    {
        var (ecu, session) = await Started();
        ecu.ReplyNext = new byte[] { 0x12, 0x04, 0xA0, 0xB6 };              // a late accept where the status should be

        await session.TickAsync();

        Assert.False(session.IsStopped);
        await session.TickAsync();
        Assert.NotNull(session.Last);
    }

    [Fact]
    public void AnAcceptOfTheWrongLength_IsNotTheOverlays()
    {
        Assert.Throws<InvalidDataException>(() => OverlayContract.Refusal(new byte[] { 0x12, 0x05, 0xA0, 0x00, 0xB7 }));
    }

    [Fact]
    public async Task Send_RightAfterAKeyCycle_UsesAFreshStatus()               // raised minor 5
    {
        var (ecu, session) = await Started();
        await session.SendAsync(2, session.Detection!.Flash[2]);
        await session.SendAsync(3, Plus(session, 3, 1));
        await session.SendAsync(4, Plus(session, 4, 1));                       // 576 bytes used: a 320-byte map would not fit
        ecu.KeyCycle();
        ecu.Running = true;

        var result = await session.SendAsync(0, Plus(session, 0, 1));          // no tick since: the last status is stale

        Assert.Equal(new SendResult(null, null), result);
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[0].State);
    }

    [Fact]
    public async Task ARebind_KeepsWhatWasSent_WhenTheFlashIsTheSame()          // final review C1
    {
        var (ecu, session) = await Started();
        var copy = Plus(session, 1, 2);
        await session.SendAsync(1, copy);
        ecu.KeyCycle();

        session.Rebind(ecu);
        await session.StartAsync();
        ecu.Running = true;
        await session.TickAsync();

        Assert.Equal(MapIntent.Live, session.Intent(1));
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        Assert.Equal(copy, new OverlayClient(ecu).ReadCopy(1, ecu.Peek().Tables[1]));
    }

    [Fact]
    public async Task ARebind_ForgetsWhatWasSent_WhenAnotherBinIsFlashed()
    {
        var (ecu, session) = await Started();
        await session.SendAsync(1, Plus(session, 1, 2));
        var other = new FakeOverlayEcu(FakeOverlayEcu.Image(seed: 3)) { Running = true };

        session.Rebind(other);
        await session.StartAsync();
        await session.TickAsync();

        Assert.Equal(MapIntent.None, session.Intent(1));
        Assert.Null(session.SentCopy(1));
        Assert.All(other.Peek().Tables, t => Assert.Equal(OverlayState.Free, t.State));
    }
}
