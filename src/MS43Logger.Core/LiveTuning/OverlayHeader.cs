namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// The overlay's header at 0C:B544, read with DS2 command 06: <c>LTOV</c>, then little-endian words for the version, the
/// code's start and end and the data's end (offsets in segment 0Ch), and the table count. The table list fills the bytes
/// from the code's end to the data's end.
/// </summary>
public sealed record OverlayHeader(int Version, int CodeStart, int CodeEnd, int DataEnd, int TableCount, int Minor = 0)
{
    /// <summary>The name the app shows: "8", or "8.5" for V8 with the wider pops throttle opening.</summary>
    public string Label => Minor == 0 ? Version.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{Version}.{Minor}";

    /// <summary>Reads the signature and version; detection decides whether the version is supported.</summary>
    public static OverlayHeader? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || bytes[0] != 'L' || bytes[1] != 'T' || bytes[2] != 'O' || bytes[3] != 'V')
        {
            return null;
        }

        return new OverlayHeader(bytes[4], Word(bytes, 6), Word(bytes, 8), Word(bytes, 10), Word(bytes, 12), bytes[5]);
    }

    /// <summary>The overlay version a bin carries in its header at file 0x4B544, or 0 when it has no overlay.</summary>
    public static int VersionIn(ReadOnlySpan<byte> image) => HasOverlay(image) ? image[0x4B548] : 0;

    /// <summary>The minor version of the header's word (its high byte): 5 on V8.5, else 0.</summary>
    public static int MinorIn(ReadOnlySpan<byte> image) => HasOverlay(image) ? image[0x4B549] : 0;

    private static bool HasOverlay(ReadOnlySpan<byte> image) =>
        image.Length >= 0x4B54A && image[0x4B544] == 'L' && image[0x4B545] == 'T' && image[0x4B546] == 'O' && image[0x4B547] == 'V';

    /// <summary>The table list: per table its page and page offset (as a physical address), its bytes and its flags.</summary>
    public static IReadOnlyList<(int Address, int Bytes, int Flags)> ParseTableList(ReadOnlySpan<byte> bytes)
    {
        var tables = new List<(int, int, int)>();
        for (var at = 0; at + 8 <= bytes.Length; at += 8)
        {
            tables.Add((Word(bytes, at) * 0x4000 + Word(bytes, at + 2), Word(bytes, at + 4), Word(bytes, at + 6)));
        }

        return tables;
    }

    private static int Word(ReadOnlySpan<byte> bytes, int at) => bytes[at] | bytes[at + 1] << 8;
}
