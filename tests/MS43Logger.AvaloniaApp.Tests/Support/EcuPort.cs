using System.IO.Ports;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The ECU on a serial port, for the app's connection tests: it echoes each frame (the K-line does) and answers the
/// definition's connect and poll; <see cref="Silent"/> makes it answer nothing, as a key-off does, until it is cleared.
/// Shared by the sessions a test opens one after the other, so disposing it only closes it.
/// </summary>
internal sealed class EcuPort(AdxDefinition definition) : ISerialPortAdapter
{
    private readonly object _gate = new();
    private readonly Queue<byte> _receive = new();

    public bool Silent { get; set; }

    /// <summary>Answers every other frame (null: no answer), such as the live-tuning overlay's 06 and 6D requests.</summary>
    public Func<byte[], byte[]?>? Other { get; set; }

    public int Connects { get; private set; }

    /// <summary>Every frame written to the port, in order.</summary>
    public List<byte[]> Written { get; } = new();

    public string PortName => "COM1";

    public bool IsOpen { get; private set; }

    public int BaudRate { get; set; }

    public Parity Parity { get; set; }

    public int DataBits { get; set; }

    public StopBits StopBits { get; set; }

    public int ReadTimeout { get; set; }

    public int WriteTimeout { get; set; }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public void DiscardInBuffer()
    {
        lock (_gate)
        {
            _receive.Clear();
        }
    }

    public void DiscardOutBuffer()
    {
    }

    public int ReadByte()
    {
        lock (_gate)
        {
            return _receive.Count > 0 ? _receive.Dequeue() : throw new TimeoutException();
        }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        var frame = buffer.AsSpan(offset, count).ToArray();
        lock (_gate)
        {
            Written.Add(frame);
            if (Silent)
            {
                return;
            }

            var connect = definition.GetConnectExchange();
            var monitor = definition.GetMonitorExchange();
            byte[]? reply = null;
            if (frame.SequenceEqual(connect.Request.Bytes))
            {
                Connects++;
                reply = connect.Response.Header;
            }
            else if (frame.SequenceEqual(monitor.Request.Bytes))
            {
                reply = monitor.Response.Header.Concat(new byte[monitor.Response.PacketSize]).Append((byte)0).ToArray();
                reply[^1] = reply[..^1].Aggregate((byte)0, (x, b) => (byte)(x ^ b));
            }
            else if (Other is { } other)
            {
                reply = other(frame);
            }

            foreach (var value in frame.Concat(reply ?? Array.Empty<byte>()))
            {
                _receive.Enqueue(value);
            }
        }
    }

    public void Dispose() => IsOpen = false;
}
