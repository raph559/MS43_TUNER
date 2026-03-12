using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MS43Logger.Core.Tuning;

/// <summary>A TunerPro XDF definition: tables, constants and flags with their storage and scaling.</summary>
public sealed class XdfDefinition
{
    private const int SupportedTypeFlags = 0x01 | 0x02;

    private readonly Dictionary<int, XdfTable> _tablesById = new();
    private readonly Dictionary<int, XdfConstant> _constantsById = new();
    private readonly List<string> _warnings = new();
    private readonly bool _defaultSigned;
    private readonly bool _defaultLsbFirst;

    private XdfDefinition(XElement root, string sourceName)
    {
        SourceName = sourceName;
        var header = root.Element("XDFHEADER") ?? throw new InvalidDataException($"{sourceName}: XDF header is missing.");
        Title = header.Element("deftitle")?.Value.Trim() ?? string.Empty;

        var baseOffset = header.Element("BASEOFFSET");
        var offset = ParseLong(baseOffset?.Attribute("offset")?.Value) ?? 0;
        BaseOffset = baseOffset?.Attribute("subtract")?.Value == "1" ? -offset : offset;

        var defaults = header.Element("DEFAULTS");
        _defaultSigned = defaults?.Attribute("signed")?.Value == "1";
        _defaultLsbFirst = defaults?.Attribute("lsbfirst")?.Value != "0";

        Categories = header.Elements("CATEGORY")
            .Where(category => ParseLong(category.Attribute("index")?.Value) is not null)
            .GroupBy(category => (int)ParseLong(category.Attribute("index")!.Value)!.Value + 1)
            .ToDictionary(group => group.Key, group => group.First().Attribute("name")?.Value ?? string.Empty);

        Tables = root.Elements("XDFTABLE").Select(ParseTable).ToList();
        Constants = root.Elements("XDFCONSTANT").Select(ParseConstant).ToList();
        Flags = root.Elements("XDFFLAG").Select(ParseFlag).ToList();

        foreach (var table in Tables)
        {
            if (!_tablesById.TryAdd(table.UniqueId, table))
            {
                _warnings.Add($"Duplicate table id 0x{table.UniqueId:X}: '{table.Title}' ignored for links, '{_tablesById[table.UniqueId].Title}' kept.");
            }
        }

        foreach (var constant in Constants)
        {
            if (!_constantsById.TryAdd(constant.UniqueId, constant))
            {
                _warnings.Add($"Duplicate constant id 0x{constant.UniqueId:X}: '{constant.Title}' ignored for links, '{_constantsById[constant.UniqueId].Title}' kept.");
            }
        }

        foreach (var table in Tables)
        {
            foreach (var axis in new[] { table.X, table.Y })
            {
                if (axis.LinkedTableId is int link && !_tablesById.ContainsKey(link))
                {
                    _warnings.Add($"Table '{table.Title}' axis {axis.Id} links to missing table 0x{link:X}.");
                }
            }
        }
    }

    public string SourceName { get; }

    public string Title { get; }

    public long BaseOffset { get; }

    /// <summary>Category names keyed by the value used in <c>CATEGORYMEM category</c> (the CATEGORY index + 1).</summary>
    public IReadOnlyDictionary<int, string> Categories { get; }

    public IReadOnlyList<XdfTable> Tables { get; }

    public IReadOnlyList<XdfConstant> Constants { get; }

    public IReadOnlyList<XdfFlag> Flags { get; }

    public IReadOnlyList<string> Warnings => _warnings;

    public static XdfDefinition Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "XDFFORMAT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{sourceName}: not a TunerPro XDF document.");
        }

        return new XdfDefinition(root, sourceName);
    }

    public static XdfDefinition LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream, Path.GetFileName(path));
    }

    public XdfTable? FindTableById(int uniqueId) => _tablesById.GetValueOrDefault(uniqueId);

    public XdfConstant? FindConstantById(int uniqueId) => _constantsById.GetValueOrDefault(uniqueId);

    public XdfItem? FindByTitle(string title) =>
        Tables.FirstOrDefault(item => Same(item.Title, title)) as XdfItem
        ?? Constants.FirstOrDefault(item => Same(item.Title, title)) as XdfItem
        ?? Flags.FirstOrDefault(item => Same(item.Title, title));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private XdfTable ParseTable(XElement element)
    {
        var title = element.Element("title")?.Value.Trim() ?? string.Empty;
        var z = element.Elements("XDFAXIS").FirstOrDefault(axis => axis.Attribute("id")?.Value == "z");
        var zData = ParseData(z?.Element("EMBEDDEDDATA"), title, requireAddress: true)!;
        var zElement = z ?? new XElement("XDFAXIS");
        var zAxis = new XdfZ(
            zData,
            ParseMath(zElement.Element("MATH")),
            zElement.Element("units")?.Value ?? string.Empty,
            ParseInt(zElement.Element("decimalpl")?.Value),
            ParseInt(zElement.Element("outputtype")?.Value),
            ParseDouble(zElement.Element("min")?.Value),
            ParseDouble(zElement.Element("max")?.Value));

        return new XdfTable(
            ParseId(element),
            title,
            Description(element),
            ParseCategories(element),
            ParseAxis(element, "x", title, zData.Cols),
            ParseAxis(element, "y", title, zData.Rows),
            zAxis);
    }

    private XdfAxis ParseAxis(XElement table, string id, string title, int defaultCount)
    {
        var element = table.Elements("XDFAXIS").FirstOrDefault(axis => axis.Attribute("id")?.Value == id);
        if (element is null)
        {
            return new XdfAxis(id, string.Empty, defaultCount, null, null, Array.Empty<string>(), ParseMath(null), null);
        }

        int? link = null;
        var embedInfo = element.Element("embedinfo");
        if (embedInfo is not null && embedInfo.Attribute("type")?.Value == "3")
        {
            link = (int?)ParseLong(embedInfo.Attribute("linkobjid")?.Value);
            if (link is null)
            {
                _warnings.Add($"Table '{title}' axis {id} has a link without a target; its index labels are used.");
            }
        }

        var labels = element.Elements("LABEL")
            .OrderBy(label => ParseInt(label.Attribute("index")?.Value) ?? 0)
            .Select(label => label.Attribute("value")?.Value ?? string.Empty)
            .ToList();

        return new XdfAxis(
            id,
            element.Element("units")?.Value ?? string.Empty,
            ParseInt(element.Element("indexcount")?.Value) ?? defaultCount,
            ParseData(element.Element("EMBEDDEDDATA"), title, requireAddress: false),
            link,
            labels,
            ParseMath(element.Element("MATH")),
            ParseInt(element.Element("decimalpl")?.Value));
    }

    private XdfConstant ParseConstant(XElement element)
    {
        var title = element.Element("title")?.Value.Trim() ?? string.Empty;
        return new XdfConstant(
            ParseId(element),
            title,
            Description(element),
            ParseCategories(element),
            ParseData(element.Element("EMBEDDEDDATA"), title, requireAddress: true)!,
            ParseMath(element.Element("MATH")),
            element.Element("units")?.Value ?? string.Empty,
            ParseInt(element.Element("decimalpl")?.Value),
            ParseInt(element.Element("outputtype")?.Value),
            ParseDouble(element.Element("rangelow")?.Value),
            ParseDouble(element.Element("rangehigh")?.Value));
    }

    private XdfFlag ParseFlag(XElement element)
    {
        var title = element.Element("title")?.Value.Trim() ?? string.Empty;
        return new XdfFlag(
            ParseId(element),
            title,
            Description(element),
            ParseCategories(element),
            ParseData(element.Element("EMBEDDEDDATA"), title, requireAddress: true)!,
            ParseLong(element.Element("mask")?.Value) ?? 0);
    }

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

    private static XdfMath ParseMath(XElement? element)
    {
        if (element is null)
        {
            return new XdfMath("X", new[] { new XdfVariable("X", XdfVariableKind.Raw, null) });
        }

        var variables = element.Elements("VAR")
            .Select(variable =>
            {
                var id = variable.Attribute("id")?.Value ?? "X";
                return variable.Attribute("type")?.Value switch
                {
                    null => new XdfVariable(id, XdfVariableKind.Raw, null),
                    "link" => new XdfVariable(id, XdfVariableKind.Link, (int?)ParseLong(variable.Attribute("linkid")?.Value)),
                    _ => new XdfVariable(id, XdfVariableKind.Unsupported, null),
                };
            })
            .ToList();

        return new XdfMath(element.Attribute("equation")?.Value ?? "X", variables);
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
