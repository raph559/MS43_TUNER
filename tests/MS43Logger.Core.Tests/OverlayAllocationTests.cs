using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>V5's part of the current overlay: the two copy pools, the overflow directory and its lookup index.</summary>
public sealed class OverlayAllocationTests
{
    private static FakeOverlayEcu Ecu()
    {
        var bytes = FakeOverlayEcu.CurrentImage(); bytes[0x7131E] = 1;
        return new(bytes);
    }

    private static (FakeOverlayEcu Ecu, OverlayClient Client, OverlayDetection Detection, int[] Ids) Loaded(int count = 12)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        var ids = OverlayV5.Catalogue.Where(m => m.Index >= 5 && m.Bytes == 64).Take(count).Select(m => m.Index).ToArray();
        Assert.Equal(count, ids.Length);
        foreach (var id in ids) Assert.Null(client.Send(id, detection.Flash[id], detection.Flash[id], client.Status()).Refused);
        return (ecu, client, detection, ids);
    }

    [Fact]
    public void TwelveMapsRemainEditable_AndTrackingIsChargedAndFreed()
    {
        var (ecu, client, detection, ids) = Loaded();
        var status = client.Status();
        Assert.Equal(12, status.Tables.Count(e => e.State == OverlayState.On));
        Assert.Equal(76, OverlayV5.TrackingBytes(status));
        Assert.Equal(896 - 12 * 64 - 76, status.FreeBytes);
        var extra = ids[^1]; var copy = detection.Flash[extra].ToArray(); copy[0] ^= 0x80; copy[^1] ^= 0x80;
        ecu.LoseNextPatchReply = true;
        Assert.Null(client.Send(extra, copy, detection.Flash[extra], status).Refused);
        Assert.Equal(copy, client.ReadCopy(extra, client.Status().Tables[extra]));
        Assert.Null(client.Off(extra)); Assert.Null(client.Free(extra));
        Assert.Equal(status.FreeBytes + 72, client.Status().FreeBytes);
        Assert.Null(client.Send(extra, copy, detection.Flash[extra], client.Status()).Refused);
        Assert.Equal(status.FreeBytes, client.Status().FreeBytes);
        Assert.Null(client.AllOff()); Assert.DoesNotContain(client.Status().Tables, e => e.State == OverlayState.On);
        Assert.Null(client.AllOn()); Assert.Equal(12, client.Status().Tables.Count(e => e.State == OverlayState.On));
        Assert.Null(client.Revert()); Assert.Equal(896, client.Status().FreeBytes);
    }

    [Fact]
    public void MoreThanTwentySmallMapsCanBeLoadedWithoutASeparateCountLimit()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect(); var count = 0;
        foreach (var map in OverlayV5.Catalogue.OrderBy(m => m.Bytes).ThenBy(m => m.Index))
        {
            var status = client.Status();
            if (OverlayV5.Place(map.Index, status) is null) continue;
            Assert.Null(client.Send(map.Index, detection.Flash[map.Index], detection.Flash[map.Index], status).Refused); count++;
        }
        Assert.True(count > 20, $"{count} maps");
        Assert.InRange(client.Status().FreeBytes, 0, 896);
        foreach (var map in OverlayV5.Catalogue) Assert.Null(OverlayV5.Place(map.Index, client.Status()) is { } p
            && client.Status().Tables[map.Index].State == OverlayState.Free ? p : null);
    }

    [Fact]
    public void SnapshotRetriesIfAutomaticFallbackChangesOnlyOverflowEntries()
    {
        var (ecu, client, _, ids) = Loaded(8);
        foreach (var id in ids.Take(5)) { Assert.Null(client.Off(id)); Assert.Null(client.Free(id)); }
        var before = ecu.Revision; var fired = false;
        ecu.BeforeOverflowRead = () => { if (!fired) { fired = true; ecu.AutomaticDisable(); } };
        var state = client.Status();
        Assert.True(fired); Assert.Equal(before, ecu.Revision);
        Assert.All(ids.Skip(5), id => Assert.Equal(OverlayState.Ready, state.Tables[id].State));
    }

    [Fact]
    public void SnapshotRefusesContinuousChangesAndInvalidLinksOrOverlaps()
    {
        var (ecu, client, _, ids) = Loaded(8);
        ecu.BeforeOverflowRead = ecu.AutomaticDisable;
        Assert.Throws<InvalidDataException>(() => client.Status()); ecu.BeforeOverflowRead = null;
        var frame = ecu.StatusFrame(); var pool = ecu.OverflowPool(); var head = frame[44] * 4;
        pool[head + 3] = frame[44];
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(frame, pool));
        pool = ecu.OverflowPool(); pool[head + 2] = (byte)((704 + head) / 4);
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(frame, pool));
        pool = ecu.OverflowPool(); pool[head + 1] = (byte)ids[0];
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(frame, pool));
        pool = ecu.OverflowPool(); pool[^1] ^= 0x80;
        Assert.Throws<InvalidDataException>(() => OverlayV5.ParseStatus(frame, pool));
    }

    [Fact]
    public void LastExtraRecordRemovalReleasesItsLookupIndex()
    {
        var (_, client, _, ids) = Loaded(6);
        var before = client.Status(); Assert.Equal(28, OverlayV5.TrackingBytes(before));
        Assert.Null(client.Off(ids[5])); Assert.Null(client.Free(ids[5]));
        var after = client.Status();
        Assert.Equal(before.FreeBytes + 64 + 28, after.FreeBytes);
        Assert.Equal(0, OverlayV5.TrackingBytes(after));
    }

    [Fact]
    public async Task KeyCycleRestoresMoreThanFiveEditedMapsAndLimiter()
    {
        var ecu = Ecu(); var session = new LiveTuningSession(ecu); var detection = await session.StartAsync(); await session.TickAsync();
        var ids = OverlayV5.Catalogue.Where(m => m.Index >= 5 && m.Bytes <= 64).Take(10).Select(m => m.Index).ToArray();
        foreach (var id in ids)
        {
            var copy = detection.Flash[id].ToArray(); copy[0] ^= 0x40;
            Assert.Null((await session.SendAsync(id, copy)).Refused);
        }
        Assert.Null(await session.SendLimiterAsync(new([200, 201, 202, 203, 204, 205], 3, true, true)));
        ecu.Reset(); session.Rebind(ecu); await session.StartAsync(); await session.TickAsync();
        Assert.All(ids, id => Assert.Equal(OverlayState.On, session.Last!.Tables[id].State));
        Assert.True(session.Last!.Limiter!.Enabled);
        Assert.All(ids, id => Assert.Equal((byte)(detection.Flash[id][0] ^ 0x40), session.SentCopy(id)![0]));
    }
}
