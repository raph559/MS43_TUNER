using System.Buffers.Binary;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV7Tests
{
    private static FakeOverlayV4Ecu Ecu()
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        return new(image, 7);
    }

    [Fact]
    public void TypedCatalogueRequiresV7MagicAndPreservesAllEarlierIds()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem); Assert.Equal(138, detection.Catalogue!.Count);
        Assert.Equal(OverlayV6.Maps, detection.Catalogue.Take(127));
        Assert.All(detection.Catalogue.Skip(127), m => { Assert.Equal(16, m.ElementBits); Assert.Equal(4, m.Flags); });
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
        ecu.HeaderMagic = 0xC0D6; Assert.Equal("not understood", client.Detect().Problem);
    }

    [Fact]
    public void AllNewMapsFitTogetherAndUpperIdsSurviveOverflowReadbackAndLiveWordPatches()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var map in OverlayV7.Maps.Skip(127))
        {
            var data = detection.Flash[map.Index].ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(data, 0xABCD);
            Assert.Null(client.Send(map.Index, data, detection.Flash[map.Index], client.Status()).Refused);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(data.Length - 2), 0xFEDC);
            Assert.Null(client.Send(map.Index, data, detection.Flash[map.Index], client.Status()).Refused);
            Assert.Equal(data, client.ReadCopy(map.Index, client.Status().Tables[map.Index]));
        }
        var status = client.Status();
        Assert.Equal(11, status.Tables.Count(e => e.State == OverlayState.On));
        Assert.Equal(68, OverlayV5.TrackingBytes(status));
        Assert.Equal(932 - 612 - 68, status.FreeBytes);
        var pool = ecu.OverflowPool(); pool[208 + 137 / 8] ^= 1 << (137 % 8);
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(ecu.StatusFrame(), pool, 7));
        Assert.Null(client.AllOff()); Assert.Null(client.Free(137));
        Assert.Equal(status.FreeBytes + 32 + 8, client.Status().FreeBytes);
        Assert.Null(client.Revert()); Assert.Equal(932, client.Status().FreeBytes);
    }

    [Fact]
    public void OriginalFiveStillFitExactlyAndTheSixthItemCannotOverwriteTheIndex()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        for (var id = 0; id < 5; id++)
            Assert.Null(client.Send(id, detection.Flash[id], detection.Flash[id], client.Status()).Refused);
        Assert.Equal(0, client.Status().FreeBytes);
        Assert.Null(OverlayV5.Place(127, client.Status()));
        Assert.Null(client.AllOff()); Assert.Null(client.Free(0));
        // The fifth base record can be reused without allocating an index over the full pool-B maps.
        Assert.NotNull(OverlayV5.Place(127, client.Status()));
        Assert.Null(client.Send(127, detection.Flash[127], detection.Flash[127], client.Status()).Refused);
        Assert.Null(OverlayV5.Place(128, client.Status()));
    }
}
