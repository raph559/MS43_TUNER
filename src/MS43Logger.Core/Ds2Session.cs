using System.IO.Ports;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core;

public sealed class Ds2Session : IDisposable, IPayloadSource, IDs2Line
{
    private const int PollRetryCount = 2;
    private readonly AdxDefinition _definition;
    private readonly ISerialPortAdapter _port;

    // One exchange on the wire at a time: the polls, the requests, the connect and the disconnect take turns.
    private readonly object _line = new();
    private bool _connected;
    private bool _disposed;

    public Ds2Session(AdxDefinition definition, ISerialPortAdapter port)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public string PortName => _port.PortName;

    public bool IsConnected => _connected;

    /// <inheritdoc />
    public long? LastRequestTimestamp { get; private set; }

    /// <inheritdoc />
    public int LastAttempts { get; private set; }

    public static Ds2Session Create(AdxDefinition definition, string portName)
    {
        return new Ds2Session(definition, new SerialPortAdapter(portName));
    }

    public void Connect()
    {
        ThrowIfDisposed();
        EnsurePortOpen();
        ConfigurePort(_definition.Header.InitialBaud, _definition.Header.InitialParity);

        lock (_line)
        {
            ExecuteExchange(_definition.GetConnectExchange());
        }

        var activeBaud = _definition.GetMonitorExchange().Request.Baud ?? _definition.Header.InitialBaud;
        ConfigurePort(activeBaud, _definition.Header.InitialParity);
        _connected = true;
    }

    public byte[] PollPayload()
    {
        ThrowIfDisposed();
        if (!_connected)
        {
            throw new InvalidOperationException("The DS2 session is not connected.");
        }

        var exchange = _definition.GetMonitorExchange();
        TimeoutException? lastTimeout = null;

        for (var attempt = 1; attempt <= PollRetryCount; attempt++)
        {
            try
            {
                // Each attempt sends the request again: the reply's values are latched after this one went out.
                var sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
                byte[] responseFrame;
                lock (_line)
                {
                    responseFrame = ExecuteExchange(exchange);
                }

                var payload = ExtractPayload(responseFrame, exchange.Response);
                LastRequestTimestamp = sentAt;
                LastAttempts = attempt;
                return payload;
            }
            catch (TimeoutException ex) when (attempt < PollRetryCount)
            {
                lastTimeout = ex;
            }
        }

        throw lastTimeout ?? new TimeoutException($"Timed out waiting for '{exchange.Response.Id}' on {PortName}.");
    }

    /// <inheritdoc />
    public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
    {
        ThrowIfDisposed();
        if (!_connected)
        {
            throw new InvalidOperationException("The DS2 session is not connected.");
        }

        var request = OverlayContract.Frame(payload);
        lock (_line)
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(request, 0, request.Length);
            return ReadResponseFrame(request, new AdxListenPacket("request", "request", null, new byte[] { 0x12 }, 0, listenMilliseconds));
        }
    }

    public void Disconnect()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_port.IsOpen && _connected)
            {
                lock (_line)
                {
                    ExecuteExchange(_definition.GetDisconnectExchange());
                }

                ConfigurePort(_definition.Header.InitialBaud, _definition.Header.InitialParity);
            }
        }
        finally
        {
            _connected = false;
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Disconnect();
        }
        finally
        {
            _port.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void EnsurePortOpen()
    {
        ConfigurePort(_definition.Header.InitialBaud, _definition.Header.InitialParity);
        if (!_port.IsOpen)
        {
            _port.Open();
        }
    }

    private void ConfigurePort(int baudRate, Parity parity)
    {
        _port.BaudRate = baudRate;
        _port.Parity = parity;
        _port.DataBits = 8;
        _port.StopBits = StopBits.One;
        _port.ReadTimeout = 50;
        _port.WriteTimeout = 500;
    }

    private byte[] ExecuteExchange(AdxExchange exchange)
    {
        if (exchange.Request.Baud is int sendBaud)
        {
            ConfigurePort(sendBaud, _definition.Header.InitialParity);
        }

        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        _port.Write(exchange.Request.Bytes, 0, exchange.Request.Bytes.Length);

        return ReadResponseFrame(exchange.Request.Bytes, exchange.Response);
    }

    private byte[] ReadResponseFrame(byte[] requestBytes, AdxListenPacket expectedResponse)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(expectedResponse.ListenTimeout);
        while (DateTime.UtcNow <= deadline)
        {
            if (!TryReadValidFrame(deadline, out var frame))
            {
                break;
            }

            if (frame is null)
            {
                continue;
            }

            if (FramesEqual(frame, requestBytes))
            {
                continue;
            }

            if (!FrameStartsWith(frame, expectedResponse.Header))
            {
                continue;
            }

            return frame;
        }

        throw new TimeoutException($"Timed out waiting for '{expectedResponse.Id}' on {PortName}.");
    }

    private static byte[] ExtractPayload(byte[] frame, AdxListenPacket response)
    {
        if (frame.Length < response.Header.Length + 1)
        {
            throw new InvalidOperationException($"Invalid DS2 frame for '{response.Id}'.");
        }

        var payloadLength = frame.Length - response.Header.Length - 1;
        if (response.PacketSize > 0 && payloadLength != response.PacketSize)
        {
            throw new InvalidOperationException(
                $"Unexpected payload length {payloadLength} for '{response.Id}' (expected {response.PacketSize}).");
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            Buffer.BlockCopy(frame, response.Header.Length, payload, 0, payloadLength);
        }

        return payload;
    }

    private bool TryReadValidFrame(DateTime deadline, out byte[]? frame)
    {
        frame = null;

        while (DateTime.UtcNow <= deadline)
        {
            if (!TryReadByte(deadline, out var firstByte))
            {
                return false;
            }

            if (!TryReadByte(deadline, out var secondByte))
            {
                return false;
            }

            var frameLength = secondByte;
            if (frameLength < 3)
            {
                continue;
            }

            var candidate = new byte[frameLength];
            candidate[0] = (byte)firstByte;
            candidate[1] = (byte)secondByte;

            if (!TryReadBytes(candidate, 2, frameLength - 2, deadline))
            {
                return false;
            }

            if (!HasValidChecksum(candidate))
            {
                continue;
            }

            frame = candidate;
            return true;
        }

        return false;
    }

    private bool TryReadBytes(byte[] buffer, int offset, int count, DateTime deadline)
    {
        for (var index = 0; index < count; index++)
        {
            if (!TryReadByte(deadline, out var value))
            {
                return false;
            }

            buffer[offset + index] = (byte)value;
        }

        return true;
    }

    private bool TryReadByte(DateTime deadline, out int value)
    {
        while (DateTime.UtcNow <= deadline)
        {
            try
            {
                value = _port.ReadByte();
                return value >= 0;
            }
            catch (TimeoutException)
            {
            }
        }

        value = -1;
        return false;
    }

    private static bool FramesEqual(IReadOnlyList<byte> left, IReadOnlyList<byte> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool FrameStartsWith(IReadOnlyList<byte> frame, IReadOnlyList<byte> prefix)
    {
        if (frame.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (frame[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidChecksum(IEnumerable<byte> frame)
    {
        byte checksum = 0;
        foreach (var value in frame)
        {
            checksum ^= value;
        }

        return checksum == 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public interface ISerialPortAdapter : IDisposable
{
    string PortName { get; }

    bool IsOpen { get; }

    int BaudRate { get; set; }

    Parity Parity { get; set; }

    int DataBits { get; set; }

    StopBits StopBits { get; set; }

    int ReadTimeout { get; set; }

    int WriteTimeout { get; set; }

    void Open();

    void Close();

    void DiscardInBuffer();

    void DiscardOutBuffer();

    int ReadByte();

    void Write(byte[] buffer, int offset, int count);
}

public sealed class SerialPortAdapter : ISerialPortAdapter
{
    private readonly SerialPort _port;

    public SerialPortAdapter(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _port = new SerialPort(portName);
    }

    public string PortName => _port.PortName;

    public bool IsOpen => _port.IsOpen;

    public int BaudRate
    {
        get => _port.BaudRate;
        set => _port.BaudRate = value;
    }

    public Parity Parity
    {
        get => _port.Parity;
        set => _port.Parity = value;
    }

    public int DataBits
    {
        get => _port.DataBits;
        set => _port.DataBits = value;
    }

    public StopBits StopBits
    {
        get => _port.StopBits;
        set => _port.StopBits = value;
    }

    public int ReadTimeout
    {
        get => _port.ReadTimeout;
        set => _port.ReadTimeout = value;
    }

    public int WriteTimeout
    {
        get => _port.WriteTimeout;
        set => _port.WriteTimeout = value;
    }

    public void Open() => _port.Open();

    public void Close() => _port.Close();

    public void DiscardInBuffer() => _port.DiscardInBuffer();

    public void DiscardOutBuffer() => _port.DiscardOutBuffer();

    public int ReadByte() => _port.ReadByte();

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public void Dispose() => _port.Dispose();
}
