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
        Assert.Equal(new byte[] { 0x6D, 0x0C, 0x07, 0x75, 0xD4 }, OverlayV9.WatchRequest(0x775D4));
        Assert.Equal(new byte[] { 0x6D, 0x0C, 0, 0, 0 }, OverlayV9.WatchRequest(0));
        Assert.Equal(new byte[] { 0x6D, 0x0B }, OverlayV9.LogRequest);
    }

    [Fact]
    public void LogReplySplitsIntoTheStockPayloadAndTheRecord()
    {
        var stock = Enumerable.Range(0, 73).Select(i => (byte)(i * 7 + 3)).ToArray();
        byte[] record = [1, 9, 0x00, 0x03, 0x80, 0x00, 0x00, 0x05, 0x40, 0x00, 12];
        var frame = Frame([0xA0, .. stock, .. record]);
        Assert.Equal(88, frame.Length);
        var parsed = OverlayV9.ParseLogReply(frame, out var payload);
        Assert.Equal(stock, payload.ToArray());
        Assert.Equal(new WatchRecord(WatchKind.MapIndices, 9, 3, 0x8000, 5, 0x4000, 12), parsed);

        Assert.Null(OverlayV9.ParseLogReply(Frame([0xA0, .. stock, 1, 2, 3]), out _));        // 77 bytes: a stock reply
        var bad = frame.ToArray(); bad[^1] ^= 1;
        Assert.Null(OverlayV9.ParseLogReply(bad, out _));
    }
}
