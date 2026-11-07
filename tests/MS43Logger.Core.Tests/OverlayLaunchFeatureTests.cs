using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V6's part of the current overlay: the launch control, no-lift shift and rolling anti-lag settings groups 124..126.</summary>
public sealed class OverlayLaunchFeatureTests
{
    private static FakeOverlayEcu Ecu()
    {
        var image = FakeOverlayEcu.CurrentImage(); image[0x7131E] = 1;
        new byte[] { 1, 25, 144, 141, 75, 128, 85, 130, 1, 20, 171, 128, 21, 38, 75, 115,
            0, 43, 128, 75, 72, 85, 130, 94 }.CopyTo(image, 0x7132C);
        return new(image);
    }

    [Fact]
    public void DetectionRequiresTheScalarFlags_AndKeepsTheOriginalCatalogue()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(143, detection.Catalogue!.Count); Assert.Equal(143, detection.Flash.Count);
        Assert.Equal(OverlayV4.Maps, detection.Catalogue.Take(124));
        Assert.Equal(new[] { 0x7132C, 0x71334, 0x7133C }, detection.Catalogue.Skip(124).Take(3).Select(m => m.Address));
        Assert.All(detection.Catalogue.Skip(124).Take(3), m => { Assert.Equal(8, m.Bytes); Assert.Equal(2, m.Flags); });
        Assert.Equal(new[] { 8, 8, 7 }, detection.Catalogue.Skip(124).Take(3).Select(m => m.ScalarGroup!.Fields.Count));
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
        ecu.ScalarFlagsOverride = 0;
        Assert.Equal("not understood", client.Detect().Problem);
        Assert.Throws<InvalidOperationException>(() => client.Status());
    }

    [Fact]
    public void OptionalGroupsUseOnlyTheirAllocatedRam_AndOverflowAccountingIncludesAll143Ids()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Equal(896, client.Status().FreeBytes);
        var ordinary = OverlayV4.Maps.Where(m => m.Index >= 5 && m.Bytes == 64).Take(5).ToArray();
        foreach (var map in ordinary)
            Assert.Null(client.Send(map.Index, detection.Flash[map.Index], detection.Flash[map.Index], client.Status()).Refused);
        foreach (var map in OverlayV6.Maps.Skip(124))
            Assert.Null(client.Send(map.Index, detection.Flash[map.Index], detection.Flash[map.Index], client.Status()).Refused);
        var status = client.Status();
        Assert.Equal(143, status.Tables.Count); Assert.Equal(8, status.Tables.Count(e => e.State == OverlayState.On));
        Assert.Equal(896 - 5 * 64 - 3 * 8 - 44, status.FreeBytes); Assert.Equal(44, OverlayV5.TrackingBytes(status));
        var pool = ecu.OverflowPool(); pool[208 + 126 / 8] &= unchecked((byte)~(1 << (126 % 8)));
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(ecu.StatusFrame(), pool));
        Assert.Null(client.AllOff()); Assert.Null(client.Free(126));
        Assert.Equal(status.FreeBytes + 16, client.Status().FreeBytes);
        Assert.Null(client.Revert()); Assert.Equal(896, client.Status().FreeBytes);
    }

    [Theory]
    [InlineData(124, 3)]
    [InlineData(125, 3)]
    [InlineData(126, 2)]
    public void UnsupportedConfigurationIsRejectedBeforeAnyMutation(int id, byte mode)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[id].ToArray(); copy[0] = mode;
        var status = client.Status(); var requests = ecu.Requests.Count;
        Assert.Equal(OverlayReason.Range, client.Send(id, copy, detection.Flash[id], status).Refused);
        Assert.Equal(requests, ecu.Requests.Count); Assert.Equal(0u, ecu.Session);
    }

    [Fact]
    public void RalNeighbourCannotChange_AndValidActiveSettingsPatchUsesOneEightByteWindow()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = detection.Flash[126].ToArray(); copy[7]++;
        var status = client.Status(); var requests = ecu.Requests.Count;
        Assert.Equal(OverlayReason.Range, client.Send(126, copy, detection.Flash[126], status).Refused);
        Assert.Equal(requests, ecu.Requests.Count);
        copy[7]--; copy[0] = 1;
        Assert.Null(client.Send(126, copy, detection.Flash[126], status).Refused);
        copy[3] = 120; copy[6] = 140;
        Assert.Null(client.Send(126, copy, detection.Flash[126], client.Status()).Refused);
        Assert.Single(ecu.Requests.Where(p => p is [0x6D, 9, ..]));
        Assert.Equal(copy, client.ReadCopy(126, client.Status().Tables[126]));
        Assert.Equal(detection.Flash[126][7], copy[7]);
    }

    [Fact]
    public async Task ConfirmedGroupsRestoreAfterAnEcuReset_AndNlsModeTwoIsSupported()
    {
        var ecu = Ecu(); var session = new LiveTuningSession(ecu); var detection = await session.StartAsync();
        await session.TickAsync(); var copy = detection.Flash[125].ToArray(); copy[0] = 2; copy[7] = 123;
        Assert.Null((await session.SendAsync(125, copy)).Refused);
        ecu.Reset(); await session.TickAsync();
        Assert.Equal(OverlayState.On, session.Last!.Tables[125].State);
        Assert.Equal(copy, session.SentCopy(125));
        Assert.Equal(copy, ecu.Copies.AsSpan(session.Last.Tables[125].Offset, 8).ToArray());
    }
}
