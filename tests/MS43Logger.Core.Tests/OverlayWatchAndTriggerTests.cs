using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// V9's parts of the current overlay: the watched map (sub 12 sets the watch; sub 11 logs with the stock payload and the
/// record), and the named triggers of launch control, no-lift shift and rolling anti-lag.
/// </summary>
public sealed class OverlayWatchAndTriggerTests
{
    private static byte[] Frame(params byte[] payload)
    {
        var frame = new byte[payload.Length + 3]; frame[0] = 0x12; frame[1] = (byte)frame.Length;
        payload.CopyTo(frame, 2); frame[^1] = frame.Take(frame.Length - 1).Aggregate((byte)0, (a, b) => (byte)(a ^ b));
        return frame;
    }

    [Fact]
    public void WatchRequestEncodesTheAddressBigEndian()
    {
        Assert.Equal(new byte[] { 0x6D, 0x0C, 0x07, 0x75, 0xD4 }, OverlayWatch.WatchRequest(0x775D4));
        Assert.Equal(new byte[] { 0x6D, 0x0C, 0, 0, 0 }, OverlayWatch.WatchRequest(0));
        Assert.Equal(new byte[] { 0x6D, 0x0B }, OverlayWatch.LogRequest);
    }

    [Fact]
    public void LogReplySplitsIntoTheStockPayloadAndTheRecord()
    {
        var stock = Enumerable.Range(0, 73).Select(i => (byte)(i * 7 + 3)).ToArray();
        byte[] record = [1, 9, 0x00, 0x03, 0x80, 0x00, 0x00, 0x05, 0x40, 0x00, 12];
        var frame = Frame([0xA0, .. stock, .. record]);
        Assert.Equal(88, frame.Length);
        var parsed = OverlayWatch.ParseLogReply(frame, out var payload);
        Assert.Equal(stock, payload.ToArray());
        Assert.Equal(new WatchRecord(WatchKind.MapIndices, 9, 3, 0x8000, 5, 0x4000, 12), parsed);

        Assert.Null(OverlayWatch.ParseLogReply(Frame([0xA0, .. stock, 1, 2, 3]), out _));        // 77 bytes: a stock reply
        var bad = frame.ToArray(); bad[^1] ^= 1;
        Assert.Null(OverlayWatch.ParseLogReply(bad, out _));
    }

    private static byte[] Group(int table, byte config)
    {
        // The flashed bytes of the X001 group, with the activation byte changed; RAL's byte 7 is its neighbour.
        var image = FakeOverlayEcu.Image(); var flash = image.AsSpan(0x7132C + (table - 124) * 8, 8).ToArray();
        flash[0] = config; return flash;
    }

    [Fact]
    public void TriggersAreValid()
    {
        var flash = Group(124, 1);
        Assert.Null(OverlayV10.ValidateCopy(124, Group(124, 0x97), flash));
        Assert.Null(OverlayV10.ValidateCopy(125, Group(125, 0x80), Group(125, 1)));
        Assert.Null(OverlayV10.ValidateCopy(126, Group(126, 0x97), Group(126, 0)));
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(126, Group(126, 2), Group(126, 0)));
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(124, Group(124, 0xA0), flash));
        var neighbour = Group(126, 0x97); neighbour[7] ^= 1;
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(126, neighbour, Group(126, 0)));
        Assert.Equal(0x97, OverlayTriggers.For(124).Single(t => t.Label == "A/C on + clutch").Value);
        Assert.Equal(0x82, OverlayTriggers.For(124).Single(t => t.Label == "ASC off + clutch").Value);
        Assert.Equal(0x82, OverlayTriggers.Pops.Single(t => t.Label == "ASC off").Value);
        Assert.Equal(["Off", "“−” pressed", "ASC off + “−”", "A/C on + “−”"], OverlayTriggers.For(126).Select(t => t.Label));
    }

    [Fact] // a stock bin's own rules (no overlay): the activation bytes take no trigger
    public void TheStockRulesTakeNoTrigger() =>
        Assert.Equal(OverlayReason.Range, OverlaySettings.ValidateStockLaunchCopy(124, Group(124, 0x97), Group(124, 1)));

    [Fact]
    public void VersionInReadsTheHeader()
    {
        var image = FakeOverlayEcu.Image();
        Assert.Equal(0, OverlayHeader.VersionIn(image));
        "LTOV"u8.CopyTo(image.AsSpan(0x4B544)); image[0x4B548] = 10; image[0x4B549] = 3;
        Assert.Equal(10, OverlayHeader.VersionIn(image));
        Assert.Equal(3, OverlayHeader.MinorIn(image));
    }

    [Fact]
    public void AnAcTriggerGoesLive()
    {
        var ecu = CurrentOverlayTests.Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[124].ToArray(); copy[0] = 0x97;
        Assert.Null(client.Send(124, copy, detection.Flash[124], client.Status()).Refused);
    }
}
