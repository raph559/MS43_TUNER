using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MS43Logger.Core.Tuning;

/// <summary>A TunerPro XDF definition: tables, constants and flags with their storage and scaling.</summary>
public sealed class XdfDefinition
{
    private const int SupportedTypeFlags = 0x01 | 0x02;
    private readonly List<string> _warnings = new();
    private readonly bool _defaultSigned;
    private readonly bool _defaultLsbFirst;

    public string SourceName { get; }

    public string Title { get; }

    public long BaseOffset { get; }

    /// <summary>Category names keyed by the value used in <c>CATEGORYMEM category</c> (the CATEGORY index + 1).</summary>
    public IReadOnlyDictionary<int, string> Categories { get; }

    public IReadOnlyList<string> Warnings => _warnings;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private XdfData? ParseData(XElement? element, string title, bool requireAddress)
    {
        if (element is null)
        {
            return requireAddress ? new XdfData(null, 8, false, true, 1, 1, "no storage defined") : null;
        }

        var rawAddress = ParseLong(element.Attribute("mmedaddress")?.Value);
        if (rawAddress is null && !requireAddress)
        {
            return null;
        }

        long? address = rawAddress is long value ? value + BaseOffset : null;
        var bits = ParseInt(element.Attribute("mmedelementsizebits")?.Value) ?? 8;
        var flagsText = element.Attribute("mmedtypeflags")?.Value;
        var flags = ParseLong(flagsText);
        var signed = flags is long f1 ? (f1 & 0x01) != 0 : _defaultSigned;
        var lsbFirst = flags is long f2 ? (f2 & 0x02) != 0 : _defaultLsbFirst;
        var rows = ParseInt(element.Attribute("mmedrowcount")?.Value) ?? 1;
        var cols = ParseInt(element.Attribute("mmedcolcount")?.Value) ?? 1;
        var major = ParseInt(element.Attribute("mmedmajorstridebits")?.Value) ?? 0;
        var minor = ParseInt(element.Attribute("mmedminorstridebits")?.Value) ?? 0;

        string? unsupported = null;
        if (address is null)
        {
            unsupported = "no address";
        }
        else if (bits is not (8 or 16 or 32))
        {
            unsupported = $"{bits}-bit elements are not supported";
        }
        else if (flags is long f3 && (f3 & ~SupportedTypeFlags) != 0)
        {
            unsupported = $"type flags {flagsText} are not supported";
        }
        else if (major != 0 || minor != 0)
        {
            unsupported = $"strides {major}/{minor} are not supported";
        }
        else if (rows < 1 || cols < 1)
        {
            unsupported = $"size {rows}×{cols} is not valid";
        }

        if (unsupported is not null && requireAddress)
        {
            _warnings.Add($"'{title}': {unsupported}; shown read-only.");
        }

        return new XdfData(address, bits, signed, lsbFirst, Math.Max(rows, 1), Math.Max(cols, 1), unsupported);
    }

    private static int ParseId(XElement element) => (int)(ParseLong(element.Attribute("uniqueid")?.Value) ?? -1);

    private static string Description(XElement element) =>
        (element.Element("description")?.Value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static IReadOnlyList<int> ParseCategories(XElement element) =>
        element.Elements("CATEGORYMEM")
            .Select(member => ParseInt(member.Attribute("category")?.Value))
            .OfType<int>()
            .ToList();

    private static long? ParseLong(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int? ParseInt(string? text) => ParseLong(text) is long value ? (int)value : null;

    private static double? ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
