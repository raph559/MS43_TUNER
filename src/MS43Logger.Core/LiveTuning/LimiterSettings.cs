namespace MS43Logger.Core.LiveTuning;

public sealed record LimiterSettings(byte[] GearLimits, byte Hysteresis, bool IgnitionCut, bool Enabled,
    bool Confirmed = false, bool AppliedIgnitionCut = false)
{
    public byte[] Payload()
    {
        if (GearLimits.Length != 6 || GearLimits.Any(limit => limit <= Hysteresis))
            throw new ArgumentException("Each limiter threshold must be greater than its hysteresis.");
        return [.. GearLimits, Hysteresis, (byte)((IgnitionCut ? 1 : 0) | (Enabled ? 0x80 : 0))];
    }

    public static LimiterSettings Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8 || (bytes[7] & ~0xE1) != 0) throw new InvalidDataException("Invalid limiter state.");
        return new(bytes[..6].ToArray(), bytes[6], (bytes[7] & 1) != 0, (bytes[7] & 0x80) != 0,
            (bytes[7] & 0x20) != 0, (bytes[7] & 0x40) != 0);
    }
}
