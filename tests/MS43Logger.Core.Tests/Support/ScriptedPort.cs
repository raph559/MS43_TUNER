using System.IO.Ports;

namespace MS43Logger.Core.Tests;

/// <summary>
/// A serial port for tests. Each write takes the next queued response, or, with <see cref="Respond"/>, whatever it returns
/// for the frame written (the echo included if the responder adds it). It keeps every write and every byte read, so a test
/// can check that exchanges on one port never interleave. Safe to call from several threads: it serialises its own state,
/// never the caller's exchanges.
/// </summary>
internal sealed class ScriptedPort : ISerialPortAdapter
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _queuedResponses = new();
    private readonly Queue<byte> _receiveBuffer = new();
    private int _baudRate;

    public ScriptedPort(string portName)
    {
        PortName = portName;
    }

    public string PortName { get; }

    public bool IsOpen { get; private set; }

    public List<byte[]> Writes { get; } = new();

    public List<int> BaudHistory { get; } = new();

    /// <summary>Every write ("W") and every byte read ("R") in the order they happened.</summary>
    public List<(char Kind, byte[] Bytes)> Traffic { get; } = new();

    /// <summary>When set, answers each write with the bytes it returns (null: no answer); queued responses are ignored.</summary>
    public Func<byte[], byte[]?>? Respond { get; set; }

    public int BaudRate
    {
        get => _baudRate;
        set
        {
            _baudRate = value;
            lock (_gate)
            {
                BaudHistory.Add(value);
            }
        }
    }

    public Parity Parity { get; set; }

    public int DataBits { get; set; }

    public StopBits StopBits { get; set; }

    public int ReadTimeout { get; set; }

    public int WriteTimeout { get; set; }

    public void QueueWriteResponse(byte[] responseBytes)
    {
        lock (_gate)
        {
            _queuedResponses.Enqueue(responseBytes);
        }
    }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void DiscardInBuffer()
    {
        lock (_gate)
        {
            _receiveBuffer.Clear();
        }
    }

    public void DiscardOutBuffer()
    {
    }

    public int ReadByte()
    {
        lock (_gate)
        {
            if (_receiveBuffer.Count == 0)
            {
                throw new TimeoutException();
            }

            var value = _receiveBuffer.Dequeue();
            Traffic.Add(('R', new[] { value }));
            return value;
        }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        lock (_gate)
        {
            Writes.Add(copy);
            Traffic.Add(('W', copy));
            var response = Respond is { } respond ? respond(copy) : _queuedResponses.Count > 0 ? _queuedResponses.Dequeue() : null;
            foreach (var value in response ?? Array.Empty<byte>())
            {
                _receiveBuffer.Enqueue(value);
            }
        }
    }

    public void Dispose() => IsOpen = false;
}
