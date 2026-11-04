namespace MS43Logger.Core.LiveTuning;

/// <summary>Compatibility entry points for the current V10.4 overlay.</summary>
public static class OverlayV10
{
    public const int Version = OverlayProtocol.Version, Minor = OverlayProtocol.Minor;
    public const int PoolABytes = OverlayMemory.PoolAEnd, CopyBytes = OverlayMemory.Budget;
    public const int Reserved = OverlaySettings.Reserved, Blip = OverlaySettings.Blip, BlipGears = OverlaySettings.BlipGears, BlipRatios = OverlaySettings.BlipRatios;
    public const string Label = OverlayProtocol.Label, BlipCategory = OverlaySettings.BlipCategory;

    public static IReadOnlyDictionary<int, IReadOnlyList<(byte Low, byte High)>> Limits => OverlaySettings.Limits;
    public static IReadOnlyList<(byte Low, byte High)> PopsLimits => OverlaySettings.PopsLimits;
    public static OverlayScalarGroup PopsGroup => OverlaySettings.PopsGroup;
    public static OverlayScalarGroup BlipGroup => OverlaySettings.BlipGroup;
    public static OverlayScalarGroup BlipGearsGroup => OverlaySettings.BlipGearsGroup;
    public static OverlayScalarGroup BlipRatiosGroup => OverlaySettings.BlipRatiosGroup;
    public static IReadOnlyList<OverlayMap> Maps => OverlayCatalogue.Maps;
    public static bool IsCurrent(OverlayHeader? header) => OverlayProtocol.IsCurrent(header);
    public static bool IsCurrent(ReadOnlySpan<byte> image) => OverlayProtocol.IsCurrent(image);
    public static OverlayReason? ValidateCopy(int table, ReadOnlySpan<byte> copy, ReadOnlySpan<byte> flash) => OverlaySettings.ValidateCopy(table, copy, flash);
    public static string? Refusal(int table, ReadOnlySpan<byte> copy) => OverlaySettings.Refusal(table, copy);
}
