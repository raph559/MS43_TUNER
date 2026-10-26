using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>Protocol 8: V7 plus the pops settings group 138, whose defaults sit in the overlay's own flash at 0xCFF80.</summary>
public sealed class OverlayV8Tests
{
    private static readonly byte[] Defaults = [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14];

    private static FakeOverlayV4Ecu Ecu(int version = 8)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        Defaults.CopyTo(image, 0x4FF80);
        return new(image, version);
    }

    private static byte[] With(int index, byte value)
    {
        var bytes = Defaults.ToArray(); bytes[index] = value; return bytes;
    }

    [Fact]
    public void DetectionAcceptsVersion8WithMagicC0D8AndEntry138()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(139, detection.Catalogue!.Count);
        Assert.Equal(OverlayV7.Maps, detection.Catalogue.Take(138));
        var pops = detection.Catalogue[138];
        Assert.Equal((138, 0xCFF80, 8, 2), (pops.Index, pops.Address, pops.Bytes, pops.Flags));
        Assert.Equal(8, pops.ScalarGroup!.Fields.Count);
        Assert.Equal(Defaults, detection.Flash[138]);
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
        ecu.HeaderMagic = 0xC0D7; Assert.Equal("not understood", client.Detect().Problem);
    }

    [Fact]
    public void Version7EcusStillShowNoPopsGroup()
    {
        var detection = new OverlayClient(Ecu(7)).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(138, detection.Catalogue!.Count);
        Assert.DoesNotContain(detection.Catalogue, m => m.Index == OverlayV8.Pops);
    }

    [Theory]
    [InlineData(0, 3)] [InlineData(0, 0x7F)] [InlineData(0, 0xA0)] [InlineData(1, 0)] [InlineData(1, 51)] [InlineData(3, 127)] [InlineData(3, 211)]
    [InlineData(4, 49)] [InlineData(4, 220)] [InlineData(5, 143)] [InlineData(6, 51)]
    public void Group138OutOfRangeBytesAreRefusedBeforeSending(int index, byte value)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var requests = ecu.Requests.Count;
        var result = client.Send(OverlayV8.Pops, With(index, value), detection.Flash[OverlayV8.Pops], client.Status());
        Assert.Equal(OverlayReason.Range, result.Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
    }

    [Theory] // 80h..9Fh: a chosen bit of the ASC (153h) or A/C (615h) frame
    [InlineData(0x80)] [InlineData(0x97)] [InlineData(0x9F)]
    public void ACanBitSwitchIsAccepted(byte value)
    {
        var copy = Defaults.ToArray(); copy[0] = value;
        Assert.Null(OverlayV8.ValidateCopy(OverlayV8.Pops, copy, Defaults));
    }

    [Fact]
    public void Group138InRangeIsSentAndReadBack()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        byte[] on = [1, 50, 0, 128, 219, 255, 50, 255];
        Assert.Null(client.Send(OverlayV8.Pops, on, detection.Flash[OverlayV8.Pops], client.Status()).Refused);
        Assert.Equal(on, client.ReadCopy(OverlayV8.Pops, client.Status().Tables[OverlayV8.Pops]));
    }

    [Theory]
    [InlineData(6, 124)] [InlineData(7, 125)] [InlineData(8, 124)] [InlineData(8, 138)]
    public void ChangingOneSettingOfAGroupSendsTheWholeGroup(int version, int group)
    {
        var ecu = Ecu(version); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[group].ToArray(); copy[0] = 1;
        var result = client.Send(group, copy, detection.Flash[group], client.Status());
        Assert.Null(result.Refused);
        Assert.Equal(copy, client.ReadCopy(group, client.Status().Tables[group]));
        var write = Assert.Single(ecu.Requests, p => p[0] == 0x6D && p[1] == 2);
        Assert.Equal(0, write[11] << 8 | write[12]); Assert.Equal(8, write.Length - 13);
    }

    [Fact]
    public void PlannerKeepsPoolAWithin700ForVersion8()
    {
        Assert.True(OverlayV5.Fits(692, 8, 8));
        Assert.False(OverlayV5.Fits(696, 8, 8));
        Assert.True(OverlayV5.Fits(696, 8, 7));
        var client = new OverlayClient(Ecu()); client.Detect();
        Assert.Equal(928, client.Status().FreeBytes);
    }
}
