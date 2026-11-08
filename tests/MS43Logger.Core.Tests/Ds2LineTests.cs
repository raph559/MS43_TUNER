using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

public class Ds2LineTests
{
    private static readonly AdxDefinition Definition = BundledDefinitions.Load("ms43x");

    private static byte[] Frame(params byte[] body)
    {
        var frame = new byte[body.Length + 3];
        frame[0] = 0x12;
        frame[1] = (byte)frame.Length;
        body.CopyTo(frame, 2);
        frame[^1] = frame[..^1].Aggregate((byte)0, (x, b) => (byte)(x ^ b));
        return frame;
    }

    private static byte[] MonitorReply()
    {
        var response = Definition.GetMonitorExchange().Response;
        var frame = response.Header.Concat(new byte[response.PacketSize]).Append((byte)0).ToArray();
        frame[^1] = frame[..^1].Aggregate((byte)0, (x, b) => (byte)(x ^ b));
        return frame;
    }

    /// <summary>The ECU on a port: it echoes every frame (the K-line does), answers the connect, the polls, and <paramref name="other"/>.</summary>
    private static (Ds2Session Session, ScriptedPort Port) Connected(Func<byte[], byte[]?> other)
    {
        var port = new ScriptedPort("COM_TEST");
        var connect = Definition.GetConnectExchange();
        var monitor = Definition.GetMonitorExchange();
        var monitorReply = MonitorReply();
        port.Respond = frame =>
        {
            var reply = frame.SequenceEqual(connect.Request.Bytes) ? connect.Response.Header
                : frame.SequenceEqual(monitor.Request.Bytes) ? monitorReply
                : other(frame);
            return reply is null ? frame : frame.Concat(reply).ToArray();
        };
        var session = new Ds2Session(Definition, port);
        session.Connect();
        return (session, port);
    }

    [Fact]
    public void Request_SendsTheFrame_AndReturnsTheReplyNotTheEcho()
    {
        var (session, port) = Connected(_ => new byte[] { 0x12, 0x04, 0xA0, 0xB6 });
        var reply = session.Request(OverlayContract.Status());
        Assert.Equal(new byte[] { 0x12, 0x04, 0xA0, 0xB6 }, reply);
        Assert.Equal(OverlayContract.Frame(OverlayContract.Status()), port.Writes[^1]);
    }

    [Fact]
    public void Request_TimesOut_WithoutAReply()
    {
        var (session, _) = Connected(_ => null);
        Assert.Throws<TimeoutException>(() => session.Request(OverlayContract.Status(), listenMilliseconds: 60));
    }

    [Fact]
    public void Request_NeedsAConnection()
    {
        using var session = new Ds2Session(Definition, new ScriptedPort("COM_TEST"));
        Assert.Throws<InvalidOperationException>(() => session.Request(OverlayContract.Status()));
    }

    [Fact]
    public void Read_SplitsIntoChunksOf123_AndJoinsTheData()
    {
        var reads = new List<(int Address, int Count)>();
        var (session, _) = Connected(frame =>
        {
            var address = frame[4] << 16 | frame[5] << 8 | frame[6];
            reads.Add((address, frame[7]));
            return Frame(new byte[] { 0xA0 }.Concat(Enumerable.Range(0, frame[7]).Select(i => (byte)(address + i))).ToArray());
        });

        var data = session.Read(0x077315, 320);

        Assert.Equal(new[] { (0x077315, 123), (0x077315 + 123, 123), (0x077315 + 246, 74) }, reads);
        Assert.Equal(Enumerable.Range(0, 320).Select(i => (byte)(0x077315 + i)), data);
    }

    [Fact]
    public void Read_ThrowsOnARefusal()
    {
        var (session, _) = Connected(_ => new byte[] { 0x12, 0x04, 0xB0, 0xA6 });
        var refused = Assert.Throws<Ds2RefusedException>(() => session.Read(0x077315, 4));
        Assert.Equal((0x077315, (byte)0xB0), (refused.Address, refused.Status));
    }

    private sealed class ReadLine(Func<byte[], byte[]> respond) : IDs2Line
    {
        public List<byte[]> Requests { get; } = [];
        public List<int> Waits { get; } = [];
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            var request = payload.ToArray(); Requests.Add(request); return respond(request);
        }
        public void WaitMilliseconds(int milliseconds) => Waits.Add(milliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    [InlineData(246)]
    public void Read_RetriesOnlyTheTimedOutChunk(int failedOffset)
    {
        var failures = 0;
        var line = new ReadLine(request =>
        {
            var at = request[2] << 16 | request[3] << 8 | request[4];
            if (at == 0x77315 + failedOffset && failures++ < 2) throw new TimeoutException();
            return Frame([0xA0, .. Enumerable.Range(0, request[5]).Select(i => (byte)(at + i))]);
        });
        Assert.Equal(Enumerable.Range(0, 320).Select(i => (byte)(0x77315 + i)), line.Read(0x77315, 320));
        var offsets = line.Requests.Select(r => (r[2] << 16 | r[3] << 8 | r[4]) - 0x77315).ToArray();
        Assert.Equal(new[] { 0, 123, 246 }.SelectMany(at => Enumerable.Repeat(at, at == failedOffset ? 3 : 1)), offsets);
        Assert.All(line.Requests, r => Assert.Equal(0x06, r[0]));
        Assert.Equal(new[] { 20, 20 }, line.Waits);
    }

    [Fact]
    public void Read_StopsAfterThreeTimeouts()
    {
        var line = new ReadLine(_ => throw new TimeoutException("disconnected"));
        Assert.Throws<TimeoutException>(() => line.Read(0x77315, 320));
        Assert.Equal(3, line.Requests.Count);
        Assert.All(line.Requests, r => Assert.Equal(line.Requests[0], r));
    }

    [Fact]
    public void Read_DoesNotRetryRefusalsOrInvalidFrames()
    {
        var refused = new ReadLine(_ => Frame(0xB0));
        Assert.Throws<Ds2RefusedException>(() => refused.Read(0x77315, 4));
        Assert.Single(refused.Requests);
        var invalid = new ReadLine(_ => [0x12, 0x04, 0xA0, 0]);
        Assert.Throws<InvalidDataException>(() => invalid.Read(0x77315, 4));
        Assert.Single(invalid.Requests);
    }

    [Fact]
    public async Task RequestsAndPolls_NeverInterleave()                   // Review Focus 1
    {
        var (session, port) = Connected(_ => new byte[] { 0x12, 0x04, 0xA0, 0xB6 });
        var answers = new List<(byte[] Write, byte[] Answer)>();
        var respond = port.Respond!;
        port.Respond = frame =>
        {
            var answer = respond(frame);
            answers.Add((frame, answer ?? Array.Empty<byte>()));
            return answer;
        };
        var traffic = port.Traffic.Count;

        var polls = Task.Run(() => { for (var i = 0; i < 200; i++) session.PollPayload(); });
        var requests = Task.Run(() => { for (var i = 0; i < 200; i++) session.Request(OverlayContract.Status()); });
        await Task.WhenAll(polls, requests);

        // Between one write and the next, exactly the bytes answering the first are read.
        var log = port.Traffic.Skip(traffic).ToList();
        var writes = 0;
        for (var i = 0; i < log.Count;)
        {
            Assert.Equal('W', log[i].Kind);
            var read = new List<byte>();
            var j = i + 1;
            for (; j < log.Count && log[j].Kind == 'R'; j++)
            {
                read.AddRange(log[j].Bytes);
            }

            Assert.Equal(Convert.ToHexString(answers[writes].Answer), Convert.ToHexString(read.ToArray()));
            writes++;
            i = j;
        }

        Assert.Equal(400, writes);
    }
}
