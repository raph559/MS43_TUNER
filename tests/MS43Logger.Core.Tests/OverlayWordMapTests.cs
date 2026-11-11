using System.Buffers.Binary;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V7's part of the current overlay: the 16-bit maps 127..137 and the overflow directory that tracks them.</summary>
public sealed class OverlayWordMapTests
{
    private static FakeOverlayEcu Ecu() => CurrentOverlayTests.Ecu();

    [Fact]
    public void TheTypedMapsKeepEveryEarlierId()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.All(detection.Catalogue!.Skip(127).Take(11), m => { Assert.Equal(16, m.ElementBits); Assert.Equal(4, m.Flags); });
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
    }

    [Fact]
    public void AllNewMapsFitTogetherAndUpperIdsSurviveOverflowReadbackAndLiveWordPatches()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var map in OverlayCatalogue.Maps.Take(138).Skip(127))
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
        Assert.Equal(68, OverlayMemory.TrackingBytes(status));
        Assert.Equal(896 - 612 - 68, status.FreeBytes);
        var pool = ecu.OverflowPool(); pool[208 + 137 / 8] ^= 1 << (137 % 8);
        Assert.Throws<InvalidDataException>(() => OverlayMemory.ParseStatus(ecu.StatusFrame(), pool));
        Assert.Null(client.AllOff()); Assert.Null(client.Free(137));
        Assert.Equal(status.FreeBytes + 32 + 8, client.Status().FreeBytes);
        Assert.Null(client.Revert()); Assert.Equal(896, client.Status().FreeBytes);
    }
}
