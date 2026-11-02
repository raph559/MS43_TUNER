using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class OverlayClientTests
{
    private static (FakeOverlayEcu Ecu, OverlayClient Client, OverlayDetection Detection) Running(byte[]? image = null)
    {
        var ecu = new FakeOverlayEcu(image ?? FakeOverlayEcu.Image()) { Running = true };
        var client = new OverlayClient(ecu);
        return (ecu, client, client.Detect());
    }

    private static bool Is6D(byte[] payload) => payload[0] == OverlayContract.Command;

    /// <summary>Adds three raw steps at rows 10 to 13 and load columns 12 to 15 of the RON98 ignition map.</summary>
    private static byte[] IgnitionBlock(byte[] flash)
    {
        var copy = (byte[])flash.Clone();
        foreach (var row in new[] { 10, 11, 12, 13 })
            foreach (var column in new[] { 12, 13, 14, 15 })
                copy[row * 16 + column] += 3;
        return copy;
    }

    [Fact]
    public void Detect_FindsTheOverlay_AndReadsTheFlashedMaps()
    {
        var (ecu, _, detection) = Running();
        Assert.Null(detection.Problem);
        Assert.Equal(new OverlayHeader(2, 0xB554, 0xBB18, 0xBB40, 5), detection.Header);
        Assert.Equal(5, detection.Flash.Count);
        Assert.All(OverlayContract.Maps, m => Assert.Equal(ecu.Read(m.Address, m.Bytes), detection.Flash[m.Index]));
        Assert.DoesNotContain(ecu.Requests, Is6D);
    }

    [Fact]
    public void Detect_WithoutTheOverlay_SaysNotFound_AndSendsNo6D()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image(), hasOverlay: false);
        var detection = new OverlayClient(ecu).Detect();
        Assert.Equal("not found", detection.Problem);
        Assert.Null(detection.Header);
        Assert.Empty(detection.Flash);
        Assert.DoesNotContain(ecu.Requests, Is6D);
    }

    [Fact]
    public void Detect_UsesTheEcusBytes_AndReportsADifferentBin()            // Review Focus 3
    {
        var (_, _, detection) = Running(FakeOverlayEcu.Image(seed: 7));
        var openBin = FakeOverlayEcu.Image();
        var map = OverlayContract.Maps[0];
        Assert.Equal(FakeOverlayEcu.Image(seed: 7).AsSpan(map.Address, map.Bytes).ToArray(), detection.Flash[0]);
        Assert.NotEqual(openBin.AsSpan(map.Address, map.Bytes).ToArray(), detection.Flash[0]);
    }

    [Fact]
    public void Send_LoadsWritesAndSwitchesOn_WithTheCompleteContentCrc()
    {
        var (_, client, detection) = Running();
        var copy = IgnitionBlock(detection.Flash[0]);

        Assert.Equal(new SendResult(null, null), client.Send(0, copy, detection.Flash[0], client.Status()));

        var entry = client.Status().Tables[0];
        Assert.Equal((OverlayState.On, OverlayContract.Crc32(copy)), (entry.State, entry.ContentCrc32));
        Assert.Equal(copy, client.ReadCopy(0, entry));
    }

    [Fact]
    public void Send_ALeanerLambdaCell_IsSent()                              // the edit limits of protocol 2 are gone
    {
        var (_, client, detection) = Running();
        var leaner = (byte[])detection.Flash[2].Clone();
        leaner[3]--;

        Assert.Null(client.Send(2, leaner, detection.Flash[2], client.Status()).Refused);
    }

    [Fact]
    public void Send_RefusesAStatusOfAnotherVersion()
    {
        var (_, client, detection) = Running();
        var other = client.Status() with { Version = 9 };
        Assert.Throws<ArgumentException>(() => client.Send(0, detection.Flash[0], detection.Flash[0], other));
    }

    [Fact]
    public void Switches_AndRevert_GoToTheEcu()
    {
        var (_, client, detection) = Running();
        Assert.Null(client.Send(1, detection.Flash[1], detection.Flash[1], client.Status()).Refused);
        Assert.Null(client.Off(1));
        Assert.Equal(OverlayState.Ready, client.Status().Tables[1].State);
        Assert.Null(client.AllOn());
        Assert.Equal(OverlayState.On, client.Status().Tables[1].State);
        Assert.Null(client.AllOff());
        Assert.Null(client.On(1, OverlayContract.Crc32(detection.Flash[1])));
        Assert.Equal(OverlayState.On, client.Status().Tables[1].State);
        Assert.Null(client.Revert());
        Assert.Equal(OverlayState.Free, client.Status().Tables[1].State);
    }
}
