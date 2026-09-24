using System.IO.Ports;
using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

public class LoggerCoreTests
{
    [Fact]
    public void BundledDefinitionLoadsAndResolvesMonitorExchange()
    {
        var definition = BundledDefinitions.Load("b30");
        var monitor = definition.GetMonitorExchange();

        Assert.Equal(87, definition.Values.Count);
        Assert.Equal(9600, definition.Header.InitialBaud);
        Assert.Equal(Parity.Even, definition.Header.InitialParity);
        Assert.Equal("DATAREQUEST", monitor.Request.Id);
        Assert.Equal("DATAREPLY", monitor.Response.Id);
        Assert.Equal("12-05-0B-B0-AC", BitConverter.ToString(monitor.Request.Bytes));
        Assert.Equal("12-56-A0", BitConverter.ToString(monitor.Response.Header));
        Assert.Equal(82, monitor.Response.PacketSize);
    }

    [Fact]
    public void BundledMs43xDefinitionListsItsBitmasks()
    {
        var definition = BundledDefinitions.Load("ms43x");

        Assert.Contains(new AdxBitmaskDefinition("CHSTATE", "Catalyst Heating", 0x01, 0x08, 0x08), definition.Bitmasks);
        Assert.Contains(new AdxBitmaskDefinition("VANOSACTIVE", "VANOS Active", 0x02, 0x03, 0x02), definition.Bitmasks);
        Assert.DoesNotContain(definition.Values, value => value.Id == "CHSTATE");
    }

    // Tests read clutch, brake and warm-up bits by id; a bit the definition lacks is unknown, never off.
    [Fact]
    public void BitmasksDecodeFromThePayload()
    {
        var ms43x = BundledDefinitions.Load("ms43x");
        var payload = new byte[73];

        payload[0x01] = 0x04;
        Assert.True(ms43x.DecodeBitmask("WUPSTATE", payload));
        Assert.False(ms43x.DecodeBitmask("CHSTATE", payload));

        payload[0x02] = 0x02;
        Assert.True(ms43x.DecodeBitmask("VANOSACTIVE", payload));
        Assert.False(ms43x.DecodeBitmask("VANOSPASSIVE", payload));
        payload[0x02] = 0x00;
        Assert.True(ms43x.DecodeBitmask("VANOSPASSIVE", payload));

        payload[0x00] = 0x80;
        Assert.True(ms43x.DecodeBitmask("cSsTaTe", payload));

        Assert.Null(ms43x.DecodeBitmask("NOSUCHBIT", payload));
        Assert.Null(ms43x.FindBitmask("NOSUCHBIT"));
        Assert.Null(ms43x.DecodeBitmask("WUPSTATE", new byte[1]));
        Assert.Throws<InvalidOperationException>(() => ms43x.FindBitmask("wupstate")!.IsSet(new byte[1]));

        var b30 = BundledDefinitions.Load("b30");
        var stock = new byte[82];
        stock[0] = 0x10;
        Assert.True(b30.DecodeBitmask("CLUTCHSWITCH", stock));
    }

    [Fact]
    public void BundledDefinitionsHaveAStableHash()
    {
        var a = BundledDefinitions.Sha256("ms43x");

        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.Equal(a, BundledDefinitions.Sha256("MS43X"));
        Assert.NotEqual(a, BundledDefinitions.Sha256("b25"));
    }

    [Fact]
    public void StockPayloadsAreEightyTwoBytes() =>
        Assert.Equal(82, LivePayloads.Build(BundledDefinitions.Load("b25"), ("RPM", 3000L)).Length);

    [Fact]
    public void Ms43xSixteenBitChannelsAreLsbFirst()
    {
        // The frame is copied from C167 RAM, so words arrive low byte first; the ADX marks them with <flags>0x00000002</flags>.
        var definition = BundledDefinitions.Load("ms43x");
        var payload = new byte[73];
        payload[0x04] = 0xB0;
        payload[0x05] = 0x69;
        var n = definition.Values.Single(value => value.Id == "N");

        Assert.True(n.LsbFirst);
        Assert.Equal(0x69B0, AdxDefinition.ReadRaw(n, payload));
        Assert.Equal(2480, definition.DecodePayload(payload, new[] { n }).Single().Value);
        Assert.Equal(3, Math.Floor(definition.DecodePayload(payload, definition.Values.Where(value => value.Id == "GEAR")).Single().Value));
    }

    [Fact]
    public void Ms43xBundledDefinitionMatchesCurrentFirmwareReplyShape()
    {
        var definition = BundledDefinitions.Load("ms43x");
        var monitor = definition.GetMonitorExchange();

        Assert.Equal("DATAREQUEST", monitor.Request.Id);
        Assert.Equal("DATAREPLY", monitor.Response.Id);
        Assert.Equal("12-05-0B-B0-AC", BitConverter.ToString(monitor.Request.Bytes));
        Assert.Equal("12-4D-A0", BitConverter.ToString(monitor.Response.Header));
        Assert.Equal(73, monitor.Response.PacketSize);
    }

    [Fact]
    public void DecodePayloadEvaluatesMathLinksAndLookupTables()
    {
        var definition = BundledDefinitions.Load("b30");
        var payload = new byte[82];

        payload[0x00] = 0x05;
        payload[0x06] = 0x13;
        payload[0x07] = 0x88;
        payload[0x10] = 100;
        payload[0x13] = 0x01;
        payload[0x14] = 0x90;
        payload[0x15] = 0x03;
        payload[0x16] = 0xE8;
        payload[0x22] = 0x03;
        payload[0x26] = 0x03;
        payload[0x27] = 0xE8;

        var channels = definition.MatchChannels(
        [
            "RPM",
            "MAF",
            "IDLELOAD",
            "PARTLOAD",
            "FULLLOAD",
            "XVOMAP3",
            "DUTYCYCLE%",
            "COOLANTTEMP",
        ]);

        var decoded = definition.DecodePayload(payload, channels)
            .ToDictionary(value => value.Definition.Id, value => value.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(5000d, decoded["RPM"]);
        Assert.Equal(100d, decoded["MAF"], 3);
        Assert.Equal(21.194781d, decoded["IDLELOAD"], 6);
        Assert.Equal(0d, decoded["PARTLOAD"]);
        Assert.Equal(0d, decoded["FULLLOAD"]);
        Assert.Equal(1d, decoded["XVOMAP3"]);
        Assert.Equal(22.222222d, decoded["DUTYCYCLE%"], 5);
        Assert.Equal(26.705882d, decoded["COOLANTTEMP"], 5);
    }

    [Fact]
    public void SessionHandlesEchoAndExtractsPayload()
    {
        var definition = BundledDefinitions.Load("b30");
        var fakePort = new FakeSerialPortAdapter("COM_TEST");
        var connectExchange = definition.GetConnectExchange();
        var monitorExchange = definition.GetMonitorExchange();
        var disconnectExchange = definition.GetDisconnectExchange();

        var payload = Enumerable.Range(0, 82).Select(value => (byte)value).ToArray();
        fakePort.QueueWriteResponse(connectExchange.Response.Header);
        fakePort.QueueWriteResponse(monitorExchange.Request.Bytes.Concat(BuildFrame(monitorExchange.Response.Header, payload)).ToArray());
        fakePort.QueueWriteResponse(disconnectExchange.Response.Header);

        using var session = new Ds2Session(definition, fakePort);
        session.Connect();
        var loggedPayload = session.PollPayload();
        session.Disconnect();

        Assert.Equal(payload, loggedPayload);
        Assert.Equal(1, session.LastAttempts);
        Assert.Equal(connectExchange.Request.Bytes, fakePort.Writes[0]);
        Assert.Equal(monitorExchange.Request.Bytes, fakePort.Writes[1]);
        Assert.Equal(disconnectExchange.Request.Bytes, fakePort.Writes[2]);
        Assert.Contains(9600, fakePort.BaudHistory);
        Assert.Contains(125000, fakePort.BaudHistory);
        Assert.False(fakePort.IsOpen);
    }

    [Fact]
    public void SessionRetriesPollingAfterSingleTimeout()
    {
        var definition = BundledDefinitions.Load("b30");
        var fakePort = new FakeSerialPortAdapter("COM_TEST");
        var connectExchange = definition.GetConnectExchange();
        var monitorExchange = definition.GetMonitorExchange();
        var disconnectExchange = definition.GetDisconnectExchange();

        var payload = Enumerable.Range(0, 82).Select(value => (byte)value).ToArray();
        fakePort.QueueWriteResponse(connectExchange.Response.Header);
        fakePort.QueueWriteResponse([]);
        fakePort.QueueWriteResponse(BuildFrame(monitorExchange.Response.Header, payload));
        fakePort.QueueWriteResponse(disconnectExchange.Response.Header);

        using var session = new Ds2Session(definition, fakePort);
        session.Connect();

        var before = System.Diagnostics.Stopwatch.GetTimestamp();
        var loggedPayload = session.PollPayload();

        session.Disconnect();

        Assert.Equal(payload, loggedPayload);
        // The reply answered the second request, sent once the first had timed out (final review, finding 2).
        Assert.Equal(2, session.LastAttempts);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(before, session.LastRequestTimestamp!.Value)
            >= TimeSpan.FromMilliseconds(monitorExchange.Response.ListenTimeout * 0.8));
        Assert.Equal(4, fakePort.Writes.Count);
        Assert.Equal(monitorExchange.Request.Bytes, fakePort.Writes[1]);
        Assert.Equal(monitorExchange.Request.Bytes, fakePort.Writes[2]);
    }

    [Fact]
    public void ExpressionEvaluatorSupportsBitwiseAndUsedByMs43xDefinitions()
    {
        var value = ExpressionEvaluator.Evaluate("(X&8191)*IS", new Dictionary<string, double>
        {
            ["X"] = 0x2005,
            ["IS"] = 0.5,
        });

        Assert.Equal(2.5d, value, 6);
    }

    [Fact]
    public void Ms43xDefinitionResolvesLinkedHashesWithMissingLeadingZeroes()
    {
        var definition = BundledDefinitions.Load("ms43x");
        var channels = definition.MatchChannels(["MAPVE1"]);
        var payload = new byte[73];

        var decoded = definition.DecodePayload(payload, channels);

        Assert.Single(decoded);
    }

    private static byte[] BuildFrame(IReadOnlyList<byte> header, IReadOnlyList<byte> payload)
    {
        var frame = new byte[header.Count + payload.Count + 1];

        for (var index = 0; index < header.Count; index++)
        {
            frame[index] = header[index];
        }

        for (var index = 0; index < payload.Count; index++)
        {
            frame[header.Count + index] = payload[index];
        }

        byte checksum = 0;
        for (var index = 0; index < frame.Length - 1; index++)
        {
            checksum ^= frame[index];
        }

        frame[^1] = checksum;
        return frame;
    }

    private sealed class FakeSerialPortAdapter : ISerialPortAdapter
    {
        private readonly Queue<byte[]> _queuedResponses = new();
        private readonly Queue<byte> _receiveBuffer = new();
        private int _baudRate;

        public FakeSerialPortAdapter(string portName)
        {
            PortName = portName;
        }

        public string PortName { get; }

        public bool IsOpen { get; private set; }

        public List<byte[]> Writes { get; } = new();

        public List<int> BaudHistory { get; } = new();

        public int BaudRate
        {
            get => _baudRate;
            set
            {
                _baudRate = value;
                BaudHistory.Add(value);
            }
        }

        public Parity Parity { get; set; }

        public int DataBits { get; set; }

        public StopBits StopBits { get; set; }

        public int ReadTimeout { get; set; }

        public int WriteTimeout { get; set; }

        public void QueueWriteResponse(byte[] responseBytes)
        {
            _queuedResponses.Enqueue(responseBytes);
        }

        public void Open()
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public void DiscardInBuffer()
        {
            _receiveBuffer.Clear();
        }

        public void DiscardOutBuffer()
        {
        }

        public int ReadByte()
        {
            if (_receiveBuffer.Count == 0)
            {
                throw new TimeoutException();
            }

            return _receiveBuffer.Dequeue();
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            Writes.Add(copy);

            if (_queuedResponses.Count == 0)
            {
                return;
            }

            foreach (var value in _queuedResponses.Dequeue())
            {
                _receiveBuffer.Enqueue(value);
            }
        }

        public void Dispose()
        {
            IsOpen = false;
        }
    }
}
