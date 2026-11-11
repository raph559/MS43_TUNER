using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V8's part of the current overlay: the pops settings group 138, whose defaults sit in the overlay's own flash at 0xCFF80.</summary>
public sealed class OverlayPopsTests
{
    private static readonly byte[] Defaults = OverlayV104Settings.Defaults[..8];

    private static byte[] With(int index, byte value)
    {
        var bytes = Defaults.ToArray(); bytes[index] = value; return bytes;
    }

    [Fact]
    public void DetectionReadsEntry138()
    {
        var ecu = CurrentOverlayTests.Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        var pops = detection.Catalogue![138];
        Assert.Equal((138, 0xCFF80, 8, 2), (pops.Index, pops.Address, pops.Bytes, pops.Flags));
        Assert.Equal(8, pops.ScalarGroup!.Fields.Count);
        Assert.Equal(Defaults, detection.Flash[138]);
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
    }

    [Theory]
    [InlineData(0, 3)] [InlineData(0, 0x7F)] [InlineData(0, 0xA0)] [InlineData(1, 0)] [InlineData(1, 51)] [InlineData(3, 49)] [InlineData(3, 148)]
    [InlineData(4, 49)] [InlineData(4, 220)] [InlineData(5, 143)] [InlineData(6, 201)]
    public void Group138OutOfRangeBytesAreRefusedBeforeSending(int index, byte value)
    {
        var ecu = CurrentOverlayTests.Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var requests = ecu.Requests.Count;
        var result = client.Send(OverlaySettings.Pops, With(index, value), detection.Flash[OverlaySettings.Pops], client.Status());
        Assert.Equal(OverlayReason.Range, result.Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
    }

    [Theory] // 80h..9Fh: a chosen bit of the ASC (153h) or A/C (615h) frame
    [InlineData(0x80)] [InlineData(0x97)] [InlineData(0x9F)]
    public void ACanBitSwitchIsAccepted(byte value)
    {
        var copy = Defaults.ToArray(); copy[0] = value;
        Assert.Null(OverlayV10.ValidateCopy(OverlaySettings.Pops, copy, Defaults));
    }

    [Fact]
    public void Group138InRangeIsSentAndReadBack()
    {
        var ecu = CurrentOverlayTests.Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        byte[] on = [1, 50, 0, 70, 219, 255, 200, 255];
        Assert.Null(client.Send(OverlaySettings.Pops, on, detection.Flash[OverlaySettings.Pops], client.Status()).Refused);
        Assert.Equal(on, client.ReadCopy(OverlaySettings.Pops, client.Status().Tables[OverlaySettings.Pops]));
    }

    [Theory]
    [InlineData(124)] [InlineData(125)] [InlineData(138)] [InlineData(140)]
    public void ChangingOneSettingOfAGroupSendsTheWholeGroup(int group)
    {
        var ecu = CurrentOverlayTests.Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[group].ToArray(); copy[0] = 1;
        var result = client.Send(group, copy, detection.Flash[group], client.Status());
        Assert.Null(result.Refused);
        Assert.Equal(copy, client.ReadCopy(group, client.Status().Tables[group]));
        var write = Assert.Single(ecu.Requests, p => p[0] == 0x6D && p[1] == 2);
        Assert.Equal(0, write[11] << 8 | write[12]); Assert.Equal(8, write.Length - 13);
    }

    [Fact]
    public void TheStatusReports896Free()
    {
        var client = new OverlayClient(CurrentOverlayTests.Ecu()); client.Detect();
        Assert.Equal(896, client.Status().FreeBytes);
    }
}
