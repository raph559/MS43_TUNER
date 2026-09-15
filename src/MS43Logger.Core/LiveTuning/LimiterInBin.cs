using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.LiveTuning;

/// <summary>The hardcut in a bin: the manual gear limits (6 bytes at 0x75D47), the hysteresis (0x70BFF) and the cut mode (0x7131E).</summary>
public static class LimiterInBin
{
    public const int GearsAt = 0x75D47, HysteresisAt = 0x70BFF, ModeAt = 0x7131E;

    public sealed record Items(XdfTable Gears, XdfConstant Hysteresis, XdfConstant Mode);

    /// <summary>The three XDF items, or null when the XDF lacks one.</summary>
    public static Items? Find(XdfDefinition xdf)
    {
        ArgumentNullException.ThrowIfNull(xdf);
        static bool At(XdfData data, int address, int count) => data is { ElementBits: 8, Signed: false, IsSupported: true }
            && data.Address is { } actual && BinImage.ToOffset(actual) == address && data.Count == count;
        var gears = xdf.Tables.FirstOrDefault(t => At(t.Z.Data, GearsAt, 6));
        var hysteresis = xdf.Constants.FirstOrDefault(c => At(c.Data, HysteresisAt, 1));
        var mode = xdf.Constants.FirstOrDefault(c => At(c.Data, ModeAt, 1));
        return gears is null || hysteresis is null || mode is null ? null : new Items(gears, hysteresis, mode);
    }
}
