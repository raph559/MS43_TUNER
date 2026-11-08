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

    /// <summary>The flashed limiter as the bin holds it (the bin carries no "use live settings" flag).</summary>
    public static LimiterSettings Read(BinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new LimiterSettings(image.Bytes.Slice(GearsAt, 6).ToArray(), image.Bytes[HysteresisAt], image.Bytes[ModeAt] != 0, false);
    }

    /// <summary>
    /// Writes the settings into the document through its history, one step per changed item; <see cref="LimiterSettings.Payload"/>
    /// checks them first (each limit above the hysteresis).
    /// </summary>
    public static void Write(CalibrationDocument document, EditHistory history, Items items, LimiterSettings settings, string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Payload();
        var source = document.Image.ToArray(); settings.GearLimits.CopyTo(source, GearsAt);
        var plans = new[] { TableEdits.CopyFrom(document, items.Gears, BinImage.FromBytes(source)),
            TableEdits.SetConstantRaw(document, items.Hysteresis, settings.Hysteresis),
            TableEdits.SetConstantRaw(document, items.Mode, settings.IgnitionCut ? 1 : 0) };
        foreach (var plan in plans.Where(p => p.Changes.Count > 0)) history.Apply(plan, $"{label}: {plan.Item.Title}");
    }
}
