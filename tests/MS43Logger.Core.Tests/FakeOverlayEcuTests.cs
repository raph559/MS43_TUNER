using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class FakeOverlayEcuTests
{
    private static OverlayReason? Send(FakeOverlayEcu ecu, byte[] payload) => OverlayContract.Refusal(ecu.Request(payload));

    private static byte[] Flash(FakeOverlayEcu ecu, int table) => ecu.Read(OverlayContract.Maps[table].Address, OverlayContract.Maps[table].Bytes);

    [Fact]
    public void LoadWriteOn_GoesLive_AndStatusSaysSo()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        var flash = Flash(ecu, 1);
        var copy = flash.Select(b => (byte)(b + 2)).ToArray();

        Assert.Null(Send(ecu, OverlayContract.Load(1, 0)));
        Assert.Null(Send(ecu, OverlayContract.Write(1, 0, copy)));
        Assert.Null(Send(ecu, OverlayContract.On(1, OverlayContract.Crc32(copy))));

        var status = OverlayContract.ParseStatus(ecu.Request(OverlayContract.Status()));
        Assert.Equal(new OverlayEntry(OverlayState.On, 0, OverlayContract.Crc32(copy)), status.Tables[1]);
        Assert.Equal(OverlayContract.CopyBytes - 36, status.FreeBytes);
        Assert.True(status.Running);
        Assert.Equal(copy, ecu.Read(OverlayContract.CopyRamAddress, 36));
        Assert.Equal(status.Tables, ecu.Peek().Tables);
    }

    [Fact]
    public void EveryRefusal_HasItsReason()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        Send(ecu, OverlayContract.Load(0, 0));
        Send(ecu, OverlayContract.On(0, OverlayContract.Crc32(Flash(ecu, 0))));
        var leaner = Flash(ecu, 2);
        leaner[0]--;
        Send(ecu, OverlayContract.Load(2, 400));
        Send(ecu, OverlayContract.Write(2, 0, leaner[..1]));

        Assert.Equal(OverlayReason.Length, Send(ecu, new byte[] { 0x6D }));
        Assert.Equal(OverlayReason.Length, Send(ecu, new byte[] { 0x6D, 0x00, 0x00 }));
        Assert.Equal(OverlayReason.Sub, Send(ecu, new byte[] { 0x6D, 0x09 }));
        Assert.Equal(OverlayReason.Table, Send(ecu, OverlayContract.Load(5, 0)));
        Assert.Equal(OverlayReason.Range, Send(ecu, OverlayContract.Load(3, 497)));
        Assert.Equal(OverlayReason.Range, Send(ecu, OverlayContract.Write(1, 36, new byte[] { 1 })));
        Assert.Equal(OverlayReason.Overlap, Send(ecu, OverlayContract.Load(3, 300)));
        Assert.Equal(OverlayReason.TableOn, Send(ecu, OverlayContract.Free(0)));
        Assert.Equal(OverlayReason.NotLoaded, Send(ecu, OverlayContract.On(4, 0)));
        Assert.Equal(OverlayReason.Integrity, Send(ecu, OverlayContract.On(0, 1)));
        Assert.Equal(OverlayReason.Leaner, Send(ecu, OverlayContract.On(2, OverlayContract.Crc32(leaner))));
        ecu.Running = false;
        Assert.Equal(OverlayReason.NotRunning, Send(ecu, OverlayContract.On(1, 0)));
        Assert.Equal(OverlayReason.NotRunning, Send(ecu, OverlayContract.AllOn()));
    }

    [Fact]
    public void Silence_SwitchesEverythingOff()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        Send(ecu, OverlayContract.Load(1, 0));
        Send(ecu, OverlayContract.On(1, OverlayContract.Crc32(Flash(ecu, 1))));
        ecu.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[1].State);
        ecu.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[1].State);
    }

    [Fact]
    public void AStall_SwitchesEverythingOff_AndKeepsTheCopies()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        Send(ecu, OverlayContract.Load(1, 0));
        Send(ecu, OverlayContract.On(1, OverlayContract.Crc32(Flash(ecu, 1))));
        ecu.Stall();
        Assert.False(ecu.Running);
        Assert.Equal(new OverlayEntry(OverlayState.Ready, 0, OverlayContract.Crc32(Flash(ecu, 1))), ecu.Peek().Tables[1]);
    }

    [Fact]
    public void KeyCycle_WipesTheWindow()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Running = true };
        Send(ecu, OverlayContract.Load(1, 0));
        Send(ecu, OverlayContract.On(1, OverlayContract.Crc32(Flash(ecu, 1))));
        ecu.KeyCycle();
        Assert.False(ecu.Running);
        Assert.All(ecu.Peek().Tables, t => Assert.Equal(new OverlayEntry(OverlayState.Free, 0, 0), t));
    }

    [Fact]
    public void WithoutTheOverlay_HeaderIsFF_And6DIsUnknown()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image(), hasOverlay: false);
        Assert.All(ecu.Read(OverlayContract.HeaderAddress, 16), b => Assert.Equal(0xFF, b));
        Assert.Equal(new byte[] { 0x12, 0x04, 0xFF, 0xE9 }, ecu.Request(OverlayContract.Status()));
    }

    [Fact]
    public void TheHeaderAndTableList_AreTheOverlays()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image());
        var header = OverlayHeader.Parse(ecu.Read(OverlayContract.HeaderAddress, 16))!;
        Assert.Equal(new OverlayHeader(2, 0xB554, 0xBB18, 0xBB40, 5), header);
        var list = OverlayHeader.ParseTableList(ecu.Read(0x0C0000 + header.CodeEnd, header.DataEnd - header.CodeEnd));
        Assert.Equal(OverlayContract.Maps.Select(m => (m.Address, m.Bytes)), list.Select(t => (t.Address, t.Bytes)));
    }

    [Fact]
    public void FailNext_IsThrownOnce()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { FailNext = new IOException("The port is closed.") };
        Assert.Throws<IOException>(() => ecu.Request(OverlayContract.Status()));
        Assert.Equal(0xA0, ecu.Request(OverlayContract.Status())[2]);
    }
}
