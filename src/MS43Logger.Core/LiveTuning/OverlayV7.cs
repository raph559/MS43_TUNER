namespace MS43Logger.Core.LiveTuning;

/// <summary>The catalogue through the word-map additions, before pops.</summary>
public static class OverlayV7
{
    public const int Version = 7, IndexBytes = 20;
    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayCatalogue.Maps.Take(138).ToArray());
}
