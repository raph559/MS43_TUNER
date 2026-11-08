using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public sealed class OverlayV2SafetyTests
{
    private static (FakeOverlayEcu Ecu, OverlayClient Client, OverlayDetection Detection) Connected(int version = 2)
    {
        var image = FakeOverlayEcu.Image();
        foreach (var map in OverlayContract.Maps)
        {
            Array.Fill(image, (byte)100, map.Address, map.Bytes);
        }

        var ecu = new FakeOverlayEcu(image) { Running = true, Version = version };
        var client = new OverlayClient(ecu);
        return (ecu, client, client.Detect());
    }

    [Fact]
    public void CancellingDroppedEdits_DoNotActivateAnUnchangedCopy()
    {
        var (ecu, client, found) = Connected();
        var copy = (byte[])found.Flash[1].Clone();
        copy[0] += 3;
        copy[2] -= 3;
        ecu.DropWrites = 4;

        Assert.Equal(OverlayReason.Integrity, client.Send(1, copy, found.Flash[1], client.Status()).Refused);
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[1].State);
        Assert.DoesNotContain(ecu.Requests, r => r is [0x6D, 0x03, ..]);
        Assert.Equal(2, ecu.Requests.Count(r => r is [0x6D, 0x01, ..]));
        Assert.Equal(found.Flash[1], client.ReadCopy(1, ecu.Peek().Tables[1]));
    }

    [Fact]
    public void CancellingDroppedEdits_RetryTheWholeSnapshotOnceBeforeActivation()
    {
        var (ecu, client, found) = Connected();
        var copy = (byte[])found.Flash[1].Clone();
        copy[0] += 3;
        copy[2] -= 3;
        ecu.DropWrites = 2;

        Assert.Null(client.Send(1, copy, found.Flash[1], client.Status()).Refused);
        Assert.Equal(copy, client.ReadCopy(1, ecu.Peek().Tables[1]));
        Assert.Single(ecu.Requests, r => r is [0x6D, 0x03, ..]);
        Assert.Equal(2, ecu.Requests.Count(r => r is [0x6D, 0x01, ..]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorrectCrcForIntendedContent_RejectsPermutationOrCorruption(bool permutation)
    {
        var (ecu, _, found) = Connected();
        var intended = (byte[])found.Flash[1].Clone();
        intended[0] += 3;
        intended[1] -= 3;
        var received = (byte[])intended.Clone();
        if (permutation)
        {
            (received[0], received[1]) = (received[1], received[0]);
        }
        else
        {
            received[0] ^= 1;
        }

        ecu.Request(OverlayContract.Load(1, 0));
        ecu.Request(OverlayContract.Write(1, 0, received));
        Assert.Equal(OverlayReason.Integrity,
            OverlayContract.Refusal(ecu.Request(OverlayContract.On(1, OverlayContract.Crc32(intended)))));
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[1].State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostReadback_RefusesPermutedOrCorruptWritesBeforeAnyOn(bool permutation)
    {
        var (ecu, _, _) = Connected();
        var client = new OverlayClient(new ChangeWrite(ecu, permutation));
        var found = client.Detect();
        var intended = (byte[])found.Flash[1].Clone();
        intended[0] += 3;
        intended[1] -= 3;

        Assert.Equal(OverlayReason.Integrity, client.Send(1, intended, found.Flash[1], client.Status()).Refused);
        Assert.DoesNotContain(ecu.Requests, r => r is [0x6D, 0x03, ..]);
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[1].State);
    }

    [Theory]
    [InlineData(0, 9, OverlayReason.EditLimit)]
    [InlineData(0, -9, OverlayReason.EditLimit)]
    [InlineData(1, 9, OverlayReason.EditLimit)]
    [InlineData(2, 9, OverlayReason.EditLimit)]
    [InlineData(2, -1, OverlayReason.Leaner)]
    [InlineData(3, -9, OverlayReason.EditLimit)]
    [InlineData(4, 9, OverlayReason.EditLimit)]
    public void OutOfBoundsEdits_AreRefusedWithoutAModifyingRequest(int table, int delta, OverlayReason reason)
    {
        var (ecu, client, found) = Connected();
        var copy = (byte[])found.Flash[table].Clone();
        copy[0] = (byte)(copy[0] + delta);
        var status = client.Status();
        var before = ecu.Requests.Count;

        Assert.Equal(reason, client.Send(table, copy, found.Flash[table], status).Refused);
        Assert.Equal(before, ecu.Requests.Count);
    }

    [Theory]
    [InlineData(0, -8)]
    [InlineData(1, 8)]
    [InlineData(2, 8)]
    [InlineData(3, 8)]
    [InlineData(4, -8)]
    public void BoundaryValues_AreAcceptedRelativeToFlash(int table, int delta)
    {
        var (ecu, client, found) = Connected();
        var copy = (byte[])found.Flash[table].Clone();
        copy[0] = (byte)(copy[0] + delta);
        Assert.Null(client.Send(table, copy, found.Flash[table], client.Status()).Refused);
        Assert.Equal(OverlayState.On, ecu.Peek().Tables[table].State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FirmwarePolicy_RefusesAnExtremeCellEvenWithItsCorrectCrc(int table)
    {
        var (ecu, _, found) = Connected();
        var copy = (byte[])found.Flash[table].Clone();
        copy[0] = 255;
        ecu.Request(OverlayContract.Load(table, 0));
        ecu.Request(OverlayContract.Write(table, 0, copy[..1]));
        Assert.Equal(OverlayReason.EditLimit,
            OverlayContract.Refusal(ecu.Request(OverlayContract.On(table, OverlayContract.Crc32(copy)))));
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[table].State);
    }

    [Fact]
    public void RepeatedEdits_CannotRatchetTheLimitAwayFromFlash()
    {
        var (ecu, client, found) = Connected();
        var copy = (byte[])found.Flash[1].Clone();
        copy[0] += 8;
        Assert.Null(client.Send(1, copy, found.Flash[1], client.Status()).Refused);
        copy[0]++;
        Assert.Equal(OverlayReason.EditLimit, client.Send(1, copy, found.Flash[1], client.Status()).Refused);
        Assert.Equal(108, client.ReadCopy(1, ecu.Peek().Tables[1])[0]);
    }

    [Fact]
    public void AllOn_RechecksTheStoredContentCrcBeforeChangingAnyReadyState()
    {
        var (ecu, client, found) = Connected();
        Assert.Null(client.Send(1, found.Flash[1], found.Flash[1], client.Status()).Refused);
        Assert.Null(client.Send(3, found.Flash[3], found.Flash[3], client.Status()).Refused);
        Assert.Null(client.AllOff());
        ecu.CorruptCopy(3, 0, 101);

        Assert.Equal(OverlayReason.Integrity, client.AllOn());
        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[1].State);
        Assert.Equal(OverlayState.Ready, ecu.Peek().Tables[3].State);
    }

    [Fact]
    public void LegacyOverlay_IsExplicitlyUnsupportedAndNeverReceives6D()
    {
        var (ecu, client, found) = Connected(version: 1);
        Assert.Contains("Unsupported live-tuning overlay version 1", found.Problem);
        Assert.Empty(found.Flash);
        Assert.Throws<InvalidOperationException>(() => client.On(1, 0));
        Assert.Throws<InvalidOperationException>(() => client.AllOn());
        Assert.Throws<InvalidOperationException>(() => client.Status());
        Assert.DoesNotContain(ecu.Requests, r => r[0] == 0x6D);
    }

    [Fact]
    public void FirmwareV2_RefusesTheOldTwoByteOnWithoutActivation()
    {
        var (ecu, _, _) = Connected();
        ecu.Request(OverlayContract.Load(1, 0));
        Assert.Equal(OverlayReason.Length,
            OverlayContract.Refusal(ecu.Request(new byte[] { 0x6D, 0x03, 0x01, 0x00, 0x00 })));
        Assert.Equal(OverlayState.Loaded, ecu.Peek().Tables[1].State);
    }

    [Fact]
    public void CorruptMemoryReadChecksum_StopsDetectionBeforeAnyLiveCommand()
    {
        var (ecu, _, _) = Connected();
        var client = new OverlayClient(new ChangeReply(ecu, (payload, reply) =>
        {
            if (payload[0] == 0x06)
            {
                reply[^1] ^= 1;
            }

            return reply;
        }));
        Assert.Throws<InvalidDataException>(() => client.Detect());
        Assert.Throws<InvalidOperationException>(() => client.On(1, 0));
        Assert.DoesNotContain(ecu.Requests, r => r[0] == 0x6D);
    }

    [Fact]
    public void CorruptWriteAcknowledgement_NeverLeadsToActivation()
    {
        var (ecu, _, _) = Connected();
        var client = new OverlayClient(new ChangeReply(ecu, (payload, reply) =>
        {
            if (payload is [0x6D, 0x02, ..])
            {
                reply[^1] ^= 1;
            }

            return reply;
        }));
        var found = client.Detect();
        var copy = (byte[])found.Flash[1].Clone();
        copy[0]++;
        Assert.Throws<InvalidDataException>(() => client.Send(1, copy, found.Flash[1], client.Status()));
        Assert.DoesNotContain(ecu.Requests, r => r is [0x6D, 0x03, ..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(51)]
    public void StatusReply_RequiresTheAddressLengthAndChecksum(int at)
    {
        var (ecu, _, _) = Connected();
        var reply = ecu.Request(OverlayContract.Status());
        reply[at] ^= 1;
        Assert.Throws<InvalidDataException>(() => OverlayContract.ParseStatus(reply));
    }

    [Fact]
    public void AnUnknownRefusalReason_IsNotAcceptedAsAValidResponse()
    {
        Assert.Throws<InvalidDataException>(() => OverlayContract.Refusal(OverlayContract.Frame(new byte[] { 0xB1, 0xFF })));
    }

    private sealed class ChangeWrite(IDs2Line inner, bool permutation) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            var bytes = payload.ToArray();
            if (bytes is [0x6D, 0x02, ..] && bytes.Length >= 7)
            {
                if (permutation)
                {
                    (bytes[5], bytes[6]) = (bytes[6], bytes[5]);
                }
                else
                {
                    bytes[5] ^= 1;
                }
            }

            return inner.Request(bytes, listenMilliseconds);
        }
    }

    private sealed class ChangeReply(IDs2Line inner, Func<byte[], byte[], byte[]> change) : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400) =>
            change(payload.ToArray(), inner.Request(payload, listenMilliseconds));
    }
}
