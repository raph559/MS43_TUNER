using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV3Tests
{
    [Fact]
    public void FiveMapsFit_AndLiveEditsSendOnlyPatchMessages()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Version = 3, Running = true };
        var client = new OverlayClient(ecu);
        var found = client.Detect(); Assert.Null(found.Problem);
        for (var i = 0; i < 5; i++) Assert.Null(client.Send(i, found.Flash[i], found.Flash[i], client.Status()).Refused);
        Assert.All(client.Status().Tables, t => Assert.Equal(OverlayState.On, t.State));
        Assert.Equal(0, client.Status().FreeBytes);
        var desired = found.Flash[1].ToArray(); desired[0] = 0; desired[35] = 255;
        var count = ecu.Requests.Count;
        Assert.Null(client.Send(1, desired, found.Flash[1], client.Status()).Refused);
        var writes = ecu.Requests.Skip(count).Where(p => p[0] == 0x6D && p[1] != 0).ToArray();
        Assert.Equal(2, writes.Length); Assert.All(writes, p => Assert.Equal(9, p[1]));
        Assert.Equal(desired, client.ReadCopy(1, client.Status().Tables[1]));
        Assert.All(client.Status().Tables, t => Assert.Equal(OverlayState.On, t.State));
    }

    [Fact]
    public void LostAcknowledgementRepeatsIdenticalPatchOnce_WithoutDisablingTheMap()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Version = 3, Running = true };
        var client = new OverlayClient(ecu); var found = client.Detect();
        Assert.Null(client.Send(0, found.Flash[0], found.Flash[0], client.Status()).Refused);
        var before = client.Status().Revision;
        var desired = found.Flash[0].ToArray(); desired[13] ^= 127;
        var lost = false;
        ecu.FailAfter = (request, _) => request is [0x6D, 9, ..] && !lost && (lost = true) ? new TimeoutException() : null;
        Assert.Null(client.Send(0, desired, found.Flash[0], client.Status()).Refused);
        var patches = ecu.Requests.Where(p => p is [0x6D, 9, ..]).ToArray();
        Assert.Equal(2, patches.Length); Assert.Equal(patches[0], patches[1]);
        Assert.Equal(before + 1, client.Status().Revision);
        Assert.Equal(desired, client.ReadCopy(0, client.Status().Tables[0]));
        Assert.DoesNotContain(ecu.Requests, p => p is [0x6D, 4, ..]);
        Assert.Equal(new[] { 100 }, ecu.Waits);
    }

    [Fact]
    public void FirstSmallEditIsImmediate_AndOnlyBackToBackGroupsWait()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Version = 3, Running = true };
        var client = new OverlayClient(ecu); var found = client.Detect();
        client.Send(0, found.Flash[0], found.Flash[0], client.Status());
        var desired = found.Flash[0].ToArray(); desired[0]++;
        Assert.Null(client.Send(0, desired, found.Flash[0], client.Status()).Refused);
        Assert.Empty(ecu.Waits);
        ecu.Advance(TimeSpan.FromMilliseconds(150)); desired[8]++;
        Assert.Null(client.Send(0, desired, found.Flash[0], client.Status()).Refused);
        Assert.Empty(ecu.Waits);
        desired[16]++; desired[24]++;
        Assert.Null(client.Send(0, desired, found.Flash[0], client.Status()).Refused);
        Assert.Equal(new[] { 100, 100 }, ecu.Waits);
    }

    [Fact]
    public async Task KeyCycleStartsANewSessionAndRestoresAllFive()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Version = 3, Running = true };
        var session = new LiveTuningSession(ecu); var found = await session.StartAsync();
        for (var i = 0; i < 5; i++) Assert.Null((await session.SendAsync(i, found.Flash[i])).Refused);
        var previous = ecu.Peek().Session;
        ecu.KeyCycle(); ecu.Running = true;
        await session.TickAsync();
        Assert.All(ecu.Peek().Tables, t => Assert.Equal(OverlayState.On, t.State));
        Assert.NotEqual(previous, ecu.Peek().Session);
    }

    [Fact]
    public void PatchWindowsPreserveEverythingOutsideTheEightCells()
    {
        foreach (var map in OverlayContract.Maps)
        {
            var current = new byte[map.Bytes];
            var target = Enumerable.Range(0, map.Bytes).Select(i => (byte)(i * 79 + 17)).ToArray();
            foreach (var at in OverlayV3.ChangedWindows(current, target).ToArray())
            {
                Assert.Equal(0, at % 2);
                var next = current.ToArray(); target.AsSpan(at, 8).CopyTo(next.AsSpan(at));
                var packet = OverlayV3.Patch(map.Index, at, 12, 3, current, next);
                Assert.Equal(34, packet.Length); current = next;
            }
            Assert.Equal(target, current);
        }
    }

    [Fact]
    public async Task InterruptedMultiPatchSendAdoptsCommittedGroupsWithoutRollback()
    {
        var ecu = new FakeOverlayEcu(FakeOverlayEcu.Image()) { Version = 3, Running = true };
        var session = new LiveTuningSession(ecu); var found = await session.StartAsync();
        await session.SendAsync(0, found.Flash[0]);
        var desired = found.Flash[0].ToArray(); desired[0] ^= 7; desired[16] ^= 7;
        var patches = 0;
        ecu.FailWhen = request => request is [0x6D, 9, ..] && ++patches >= 2 ? new TimeoutException() : null;
        await Assert.ThrowsAsync<TimeoutException>(() => session.SendAsync(0, desired));
        ecu.FailWhen = null;
        var requestsBeforeRecovery = ecu.Requests.Count;
        await session.TickAsync();
        var partial = found.Flash[0].ToArray(); partial[0] = desired[0];
        Assert.Equal(partial, session.SentCopy(0));
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[0].State);
        Assert.DoesNotContain(ecu.Requests.Skip(requestsBeforeRecovery), p => p[0] == 0x6D && p[1] != 0);
    }
}
