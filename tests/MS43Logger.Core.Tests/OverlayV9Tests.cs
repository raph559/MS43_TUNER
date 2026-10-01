using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>Protocol 9: V8 plus the watched map. Sub 12 sets the watch; sub 11 logs with the stock payload and the record.</summary>
public sealed class OverlayV9Tests
{
    private static readonly byte[] PopsDefaults = [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14];

    private static FakeOverlayV4Ecu Ecu(int version = 9)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        PopsDefaults.CopyTo(image, 0x4FF80);
        return new(image, version);
    }

    private static byte[] Frame(params byte[] payload)
    {
        var frame = new byte[payload.Length + 3]; frame[0] = 0x12; frame[1] = (byte)frame.Length;
        payload.CopyTo(frame, 2); frame[^1] = frame.Take(frame.Length - 1).Aggregate((byte)0, (a, b) => (byte)(a ^ b));
        return frame;
    }

    [Fact]
    public void DetectionAcceptsVersion9WithMagicC0D9()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(139, detection.Catalogue!.Count);
        Assert.Equal(OverlayV9.Maps, detection.Catalogue);
        ecu.HeaderMagic = 0xC0D8; Assert.Equal("not understood", client.Detect().Problem);
    }

    [Fact]
    public void Version8EcusAreUnchanged()
    {
        var detection = new OverlayClient(Ecu(8)).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(928, OverlayV5.Budget(8));
        Assert.Equal(700, OverlayV5.PoolAEnd(8));
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

    [Fact]
    public void PlannerKeepsPoolAWithin680ForVersion9()
    {
        Assert.Equal(908, OverlayV5.Budget(9));
        Assert.Equal(680, OverlayV5.PoolAEnd(9));
        Assert.True(OverlayV5.Fits(672, 8, 9));
        Assert.False(OverlayV5.Fits(676, 8, 9));
        Assert.Equal(OverlayV9.Maps, OverlayV5.Catalogue(9));
    }

    public static TheoryData<int[]> FittingSets => new() { new[] { 1, 2, 3, 4 }, new[] { 0, 1, 3, 4 }, new[] { 0, 1, 2, 4 }, new[] { 0, 1, 2, 3 } };

    [Theory, MemberData(nameof(FittingSets))] // RF5: V8's fitting sets still fit in 680 bytes of pool A plus pool B
    public void ThePlannerFitsV8sCombinations(int[] tables)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var table in tables)
        {
            var copy = detection.Flash[table].ToArray(); copy[0] = (byte)(copy[0] == 255 ? 254 : copy[0] + 1);
            var result = client.Send(table, copy, detection.Flash[table], client.Status());
            Assert.Null(result.Refused);
        }
    }

    private static byte[] Group(int table, byte config)
    {
        // The flashed bytes of the X001 group, with the activation byte changed; RAL's byte 7 is its neighbour.
        var image = FakeOverlayEcu.Image(); var flash = image.AsSpan(0x7132C + (table - 124) * 8, 8).ToArray();
        flash[0] = config; return flash;
    }

    [Fact] // RF7
    public void TriggersAreValidFromVersion9Only()
    {
        var flash = Group(124, 1);
        Assert.Null(OverlayV9.ValidateFor(9, 124, Group(124, 0x97), flash));
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(8, 124, Group(124, 0x97), flash));
        Assert.Null(OverlayV9.ValidateFor(9, 125, Group(125, 0x80), Group(125, 1)));
        Assert.Null(OverlayV9.ValidateFor(9, 126, Group(126, 0x97), Group(126, 0)));
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(9, 126, Group(126, 2), Group(126, 0)));
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(9, 124, Group(124, 0xA0), flash));
        var neighbour = Group(126, 0x97); neighbour[7] ^= 1;
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(9, 126, neighbour, Group(126, 0)));
        Assert.Equal(0x97, OverlayV9.TriggersFor(124).Single(t => t.Label == "A/C on + clutch").Value);
        Assert.Equal(0x82, OverlayV9.TriggersFor(124).Single(t => t.Label == "ASC off + clutch").Value);
        Assert.Equal(0x82, OverlayV8.Triggers.Single(t => t.Label == "ASC off").Value);
        Assert.Equal(["Off", "“−” pressed", "ASC off + “−”", "A/C on + “−”"], OverlayV9.TriggersFor(126).Select(t => t.Label));
    }

    [Fact]
    public void VersionInReadsTheHeader()
    {
        var image = FakeOverlayEcu.Image();
        Assert.Equal(0, OverlayHeader.VersionIn(image));
        "LTOV"u8.CopyTo(image.AsSpan(0x4B544)); image[0x4B548] = 9;
        Assert.Equal(9, OverlayHeader.VersionIn(image));
    }

    [Fact] // RF7: a V8 ECU's stock code would misread a trigger, so the app never sends one there
    public void AnAcTriggerIsRefusedBeforeSendingToAV8Ecu()
    {
        var ecu = Ecu(8); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[124].ToArray(); copy[0] = 0x97;
        var requests = ecu.Requests.Count;
        Assert.Equal(OverlayReason.Range, client.Send(124, copy, detection.Flash[124], client.Status()).Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        var v9 = Ecu(9); var v9Client = new OverlayClient(v9); var v9Detection = v9Client.Detect();
        Assert.Null(v9Client.Send(124, copy, v9Detection.Flash[124], v9Client.Status()).Refused);
    }
}
