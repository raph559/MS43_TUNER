namespace MS43Logger.Core.LiveTuning;

/// <summary>The ECU refused a DS2 memory read: its reply's status was not A0.</summary>
public sealed class Ds2RefusedException(int address, byte status)
    : Exception($"The ECU answered status {status:X2} to a read of {address:X6}.")
{
    public int Address { get; } = address;

    public byte Status { get; } = status;
}

/// <summary>DS2 command 06, the read-only memory read: <c>06 00 A2 A1 A0 n</c>, at most 123 bytes a request.</summary>
public static class Ds2Memory
{
    public const int MostRead = 123;
}
