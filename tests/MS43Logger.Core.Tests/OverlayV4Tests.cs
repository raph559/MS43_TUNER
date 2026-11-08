using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV4Tests
{
    private static FakeOverlayV4Ecu Ecu()
    {
        var bytes = FakeOverlayEcu.Image(); Array.Fill(bytes, (byte)219, 0x75D47, 6); bytes[0x70BFF] = 2; bytes[0x7131E] = 1;
        return new(bytes);
    }

    [Fact]
    public void CapacityIncludesSlotsAndPoolBoundaries_AllOriginalMapsFitInAnyOrder()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem); Assert.Equal(124, detection.Flash.Count);
        foreach (var id in new[] { 3, 2, 1, 4, 0 }) Assert.Equal(new SendResult(null, null), client.Send(id, detection.Flash[id], detection.Flash[id], client.Status()));
        Assert.Equal(0, client.Status().FreeBytes); Assert.Equal(0, OverlayV4.LargestBlock(client.Status()));
        Assert.Equal(OverlayReason.Overlap, client.Send(5, detection.Flash[5], detection.Flash[5], client.Status()).Refused);
        Assert.Null(client.Off(0)); Assert.Null(client.Free(0));
        Assert.Equal(320, client.Status().FreeBytes);
        Assert.Equal(new SendResult(null, null), client.Send(5, detection.Flash[5], detection.Flash[5], client.Status()));
        Assert.Equal(288, client.Status().FreeBytes);
        Assert.Null(OverlayV4.Place(6, client.Status())); // Bytes remain, but all five allocation slots are occupied.
    }

    [Fact]
    public async Task SessionRestoresAnAdditionalCatalogueMapAndLimiterAcrossAKeyCycle()
    {
        var ecu = Ecu(); var session = new LiveTuningSession(ecu); var detected = await session.StartAsync(); await session.TickAsync();
        var id = OverlayV4.Maps.Count - 1; var desired = detected.Flash[id].ToArray(); desired[0] = 77;
        Assert.Null((await session.SendAsync(id, desired)).Refused);
        var limiter = new LimiterSettings([200, 201, 202, 203, 204, 205], 3, true, true);
        Assert.Null(await session.SendLimiterAsync(limiter));
        ecu.Reset(); session.Rebind(ecu); await session.StartAsync(); await session.TickAsync();
        Assert.Equal(OverlayState.On, session.Last!.Tables[id].State);
        Assert.Equal(desired, session.SentCopy(id)); Assert.Equal(limiter.Payload(), session.Last.Limiter!.Payload());
        Assert.Null(await session.RevertAsync()); await session.TickAsync();
        Assert.Equal(932, session.Last!.FreeBytes); Assert.False(session.Last.Limiter!.Confirmed);
    }

    [Fact]
    public void LostAcknowledgementReusesTheSameRevisionAndRetainsPacing()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detected = client.Detect();
        var copy = detected.Flash[1].ToArray(); client.Send(1, copy, copy, client.Status());
        ecu.LoseNextPatchReply = true; copy[0]++;
        Assert.Null(client.Send(1, copy, detected.Flash[1], client.Status()).Refused);
        var packets = ecu.Requests.Where(p => p[1] == 9).ToArray(); Assert.Equal(2, packets.Length); Assert.Equal(packets[0], packets[1]);
        Assert.Contains(100, ecu.Waits); Assert.Equal(copy, client.ReadCopy(1, client.Status().Tables[1]));
    }

    [Fact]
    public void MalformedStatusCannotAdvertiseOverlappingOrDuplicateAllocations()
    {
        var ecu = Ecu(); ecu.Entries[0] = new(OverlayState.On, 0, 0); ecu.Entries[1] = new(OverlayState.On, 2, 0);
        Assert.Throws<InvalidDataException>(() => OverlayV4.ParseStatus(ecu.StatusFrame()));
        ecu.Entries[1] = new(OverlayState.On, 700, 0);
        Assert.Throws<InvalidDataException>(() => OverlayV4.ParseStatus(ecu.StatusFrame()));
    }

    [Theory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    [InlineData(4, false)] [InlineData(4, true)]
    [InlineData(5, false)] [InlineData(5, true)]
    [InlineData(6, false)] [InlineData(6, true)]
    [InlineData(7, false)] [InlineData(7, true)]
    [InlineData(8, false)] [InlineData(8, true)]
    public void InterruptedMutationIsReconciledBeforeAnyRetry(byte command, bool beforeExecution)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var desired = detection.Flash[1].ToArray(); desired[0]++;
        if (command > 3)
        {
            Assert.Null(client.Send(1, desired, detection.Flash[1], client.Status()).Refused);
            if (command is 5 or 7) Assert.Null(client.Off(1));
        }
        var previous = ecu.Revision; ecu.Requests.Clear();
        ecu.LoseMutation = command; ecu.LoseBeforeExecution = beforeExecution;
        var refusal = command switch
        {
            <= 3 => client.Send(1, desired, detection.Flash[1], client.Status()).Refused,
            4 => client.Off(1), 5 => client.AllOn(), 6 => client.AllOff(),
            7 => client.Free(1), _ => client.Revert(),
        };
        Assert.Null(refusal);
        Assert.Equal(previous + (command <= 3 ? 3u : 1u), ecu.Revision);
        var attempts = ecu.Requests.Where(p => p is [0x6D, _, ..] && p[1] == command).ToArray();
        Assert.Equal(beforeExecution ? 2 : 1, attempts.Length);
        if (beforeExecution) Assert.Equal(attempts[0], attempts[1]);
        if (command <= 3) Assert.Equal(desired, client.ReadCopy(1, client.Status().Tables[1]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RecoveryRefusesAnUnexpectedResultOrReset(bool reset)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        ecu.LoseMutation = 3;
        ecu.BeforeLostMutationReply = () =>
        {
            if (reset) ecu.Reset(); else ecu.Entries[1] = ecu.Entries[1] with { State = OverlayState.Loaded };
        };
        Assert.Throws<InvalidDataException>(() => client.Send(1, detection.Flash[1], detection.Flash[1], client.Status()));
        Assert.Single(ecu.Requests.Where(p => p is [0x6D, 3, ..]));
    }
}
