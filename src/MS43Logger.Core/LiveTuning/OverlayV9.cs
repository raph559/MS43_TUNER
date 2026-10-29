namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for watch logging and the earlier trigger rules.</summary>
public static class OverlayV9
{
    public const int Version = 9, PoolABytes = 680, CopyBytes = 908, LogReplyBytes = 88, StockPayloadBytes = 73;

    /// <summary>V8's limits, with the pops throttle opening up to 20.0 °TPS (V8's 5.0 gave only crackle).</summary>
    public static IReadOnlyList<(byte Low, byte High)> PopsLimits { get; } =
        Array.AsReadOnly(OverlayV8.Limits.Select((limit, i) => i == PopsThrottle ? ((byte)0, (byte)200) : limit).ToArray());

    private const int PopsThrottle = 6;

    public static OverlayScalarGroup PopsGroup { get; } = OverlayV8.PopsGroup with
    {
        Fields = OverlayV8.PopsGroup.Fields.Select(field => field.Offset != PopsThrottle ? field : field with
        {
            Definition = field.Definition! with
            {
                Description = "Added to the stock throttle setpoint during pops, at most 20.0 °TPS. Pops stops as soon as the engine "
                    + "speed rises 160 rpm, so the car cannot keep pulling.",
                RangeHigh = 20.0,
            },
        }).ToArray(),
    };

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV8.Maps
        .Select(map => map.Index == OverlayV8.Pops ? map with { ScalarGroup = PopsGroup } : map).ToArray());

    public static IReadOnlyList<(byte Value, string Label, string Short)> TriggersFor(int table) => OverlayTriggers.For(table);

    /// <summary>
    /// V9's rule for a copy of a settings group: LC and NLS take an activation byte of 0..2 or a trigger (80h..9Fh), RAL
    /// 0..1 or a trigger, and RAL keeps its neighbour rule; pops takes a throttle opening up to 200 (20.0 °TPS);
    /// everything else as V8.
    /// </summary>
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash)
    {
        if (table == OverlayV8.Pops) return ValidatePops(copy);
        if (table is < OverlayV6.LaunchControl or > OverlayV6.RollingAntiLag || copy.Length != 8 || !OverlayV8.IsCanBit(copy[0]))
            return OverlayV8.ValidateCopy(table, copy, flash);
        var stock = copy.ToArray(); stock[0] = 1;                 // the trigger stands for an enabled mode
        return OverlayV8.ValidateCopy(table, stock, flash);
    }

    /// <summary>
    /// The rule of <paramref name="version"/>: V10's from version 10, V9's from 9, V8's below; V8.5 is V8 with V9's pops
    /// limits. Before V10 no group above pops exists, so a copy of one is refused.
    /// </summary>
    public static OverlayReason? ValidateFor(int version, int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash, int minor = 0) =>
        version >= OverlayV10.Version ? OverlayV10.ValidateCopy(table, copy, flash, minor)
        : table > OverlayV8.Pops ? OverlayReason.Range
        : version >= Version ? ValidateCopy(table, copy, flash)
        : table == OverlayV8.Pops && WidePops(version, minor) ? ValidatePops(copy)
        : OverlayV8.ValidateCopy(table, copy, flash);

    /// <summary>Whether this overlay takes the pops throttle opening up to 20.0 °TPS: V9, and V8.5.</summary>
    public static bool WidePops(int version, int minor) => version >= Version || (version == OverlayV8.Version && minor >= 5);

    /// <summary>The catalogue with this overlay's pops definition, for a bin's version: V10's groups from version 10.</summary>
    public static IReadOnlyList<OverlayMap> MapsFor(int version, int minor) =>
        version >= OverlayV10.Version ? OverlayV10.MapsFor(minor) : WidePops(version, minor) ? Maps : OverlayV8.Maps;

    private static OverlayReason? ValidatePops(ReadOnlySpan<byte> copy) => ValidatePops(copy, PopsLimits);

    internal static OverlayReason? ValidatePops(ReadOnlySpan<byte> copy, IReadOnlyList<(byte Low, byte High)> limits) =>
        OverlaySettings.ValidatePops(copy, limits);
    public static byte[] WatchRequest(uint address) => OverlayWatch.WatchRequest(address);
    public static byte[] LogRequest => OverlayWatch.LogRequest;
    public static WatchRecord? ParseLogReply(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> payload) => OverlayWatch.ParseLogReply(frame, out payload);
}
