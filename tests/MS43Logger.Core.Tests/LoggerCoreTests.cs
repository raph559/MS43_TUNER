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
