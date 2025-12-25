using System.IO.Ports;

namespace MS43Logger.Core;

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
