using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Xml.Linq;

namespace MS43Logger.Core;

public sealed class AdxDefinition
{
    private readonly IReadOnlyDictionary<string, AdxValueDefinition> _valuesByIdHash;
    private readonly IReadOnlyDictionary<string, AdxLookupTable> _lookupTablesByIdHash;
    private readonly IReadOnlyDictionary<string, AdxSendCommand> _sendCommandsById;
    private readonly IReadOnlyDictionary<string, AdxListenPacket> _listenPacketsById;
    private readonly IReadOnlyDictionary<string, AdxMacro> _macrosById;

    private AdxDefinition(
        string sourceName,
        AdxHeader header,
        List<AdxValueDefinition> values,
        Dictionary<string, AdxLookupTable> lookupTablesByIdHash,
        Dictionary<string, AdxSendCommand> sendCommandsById,
        Dictionary<string, AdxListenPacket> listenPacketsById,
        Dictionary<string, AdxMacro> macrosById)
    {
        SourceName = sourceName;
        Header = header;
        Values = values.AsReadOnly();
        _valuesByIdHash = new ReadOnlyDictionary<string, AdxValueDefinition>(
            values.ToDictionary(value => value.IdHash, StringComparer.OrdinalIgnoreCase));
        _lookupTablesByIdHash = new ReadOnlyDictionary<string, AdxLookupTable>(lookupTablesByIdHash);
        _sendCommandsById = new ReadOnlyDictionary<string, AdxSendCommand>(sendCommandsById);
        _listenPacketsById = new ReadOnlyDictionary<string, AdxListenPacket>(listenPacketsById);
        _macrosById = new ReadOnlyDictionary<string, AdxMacro>(macrosById);
    }

    public string SourceName { get; }

    public AdxHeader Header { get; }

    public IReadOnlyList<AdxValueDefinition> Values { get; }

    public static AdxDefinition Load(Stream stream, string sourceName = "definition.adx")
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root ?? throw new InvalidOperationException("ADX document has no root node.");
        if (!string.Equals(root.Name.LocalName, "ADXFORMAT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported ADX document.");
        }

        var headerElement = root.Element("ADXHEADER") ?? throw new InvalidOperationException("ADX header is missing.");
        var defaultsElement = headerElement.Element("DEFAULTS") ?? throw new InvalidOperationException("ADX defaults are missing.");

        var defaults = new AdxDefaults(
            ParseInt((string?)defaultsElement.Attribute("datasizeinbits"), 8),
            ParseInt((string?)defaultsElement.Attribute("sigdigits"), 2),
            ParseInt((string?)defaultsElement.Attribute("outputtype"), 3),
            ParseBoolIndicator((string?)defaultsElement.Attribute("signed"), false),
            ParseBoolIndicator((string?)defaultsElement.Attribute("lsbfirst"), false),
            ParseBoolIndicator((string?)defaultsElement.Attribute("float"), false));

        var header = new AdxHeader(
            Description: (string?)headerElement.Element("desc") ?? string.Empty,
            InitialBaud: ParseInt(headerElement.Element("baud")?.Value, 9600),
            InitialParity: ParseParity(ParseInt(headerElement.Element("parity")?.Value, 2)),
            ConnectCommandId: (string?)headerElement.Element("connectcmd") ?? throw new InvalidOperationException("ADX connect command is missing."),
            MonitorCommandId: (string?)headerElement.Element("monitorcmd") ?? throw new InvalidOperationException("ADX monitor command is missing."),
            DisconnectCommandId: (string?)headerElement.Element("disconnectcmd") ?? throw new InvalidOperationException("ADX disconnect command is missing."),
            Defaults: defaults);

        var values = root.Elements("ADXVALUE")
            .Select(element => ParseValue(element, defaults))
            .OrderBy(value => value.PacketOffset)
            .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookupTables = root.Elements("ADXLOOKUPTABLE")
            .Select(ParseLookupTable)
            .ToDictionary(table => table.IdHash, StringComparer.OrdinalIgnoreCase);

        var sendCommands = root.Elements("ADXCSENDCOMMAND")
            .Select(ParseSendCommand)
            .ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);

        var listenPackets = root.Elements("ADXCLISTENPACKET")
            .Select(ParseListenPacket)
            .ToDictionary(packet => packet.Id, StringComparer.OrdinalIgnoreCase);

        var macros = root.Elements("ADXMACRO")
            .Select(ParseMacro)
            .ToDictionary(macro => macro.Id, StringComparer.OrdinalIgnoreCase);

        return new AdxDefinition(sourceName, header, values, lookupTables, sendCommands, listenPackets, macros);
    }

    public static AdxDefinition LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream, Path.GetFileName(path));
    }

    public AdxExchange GetConnectExchange() => ResolveExchange(Header.ConnectCommandId);

    public AdxExchange GetMonitorExchange() => ResolveExchange(Header.MonitorCommandId);

    public AdxExchange GetDisconnectExchange() => ResolveExchange(Header.DisconnectCommandId);

    public IReadOnlyList<AdxValueDefinition> MatchChannels(IEnumerable<string>? filters)
    {
        if (filters is null)
        {
            return Values;
        }

        var normalizedFilters = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Select(filter => filter.Trim())
            .ToList();

        if (normalizedFilters.Count == 0)
        {
            return Values;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<AdxValueDefinition>();

        foreach (var filter in normalizedFilters)
        {
            var exactMatches = Values
                .Where(value =>
                    string.Equals(value.Id, filter, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.DisplayName, filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var filterMatches = exactMatches.Count > 0
                ? exactMatches
                : Values.Where(value =>
                        value.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        value.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filterMatches.Count == 0)
            {
                throw new InvalidOperationException($"No channel matched '{filter}'.");
            }

            foreach (var match in filterMatches)
            {
                if (seen.Add(match.IdHash))
                {
                    matches.Add(match);
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<DecodedValue> DecodePayload(ReadOnlySpan<byte> payload, IEnumerable<AdxValueDefinition>? selectedValues = null)
    {
        var targets = (selectedValues ?? Values).ToList();
        var cache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var decoded = new List<DecodedValue>(targets.Count);

        foreach (var value in targets)
        {
            decoded.Add(new DecodedValue(value, EvaluateValue(value, payload, cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase))));
        }

        return decoded;
    }

    private AdxExchange ResolveExchange(string macroId)
    {
        if (!_macrosById.TryGetValue(macroId, out var macro))
        {
            throw new InvalidOperationException($"ADX macro '{macroId}' was not found.");
        }

        AdxSendCommand? request = null;
        AdxListenPacket? response = null;

        foreach (var step in macro.Steps)
        {
            for (var repeat = 0; repeat < Math.Max(1, step.RepeatCount); repeat++)
            {
                if (request is null && _sendCommandsById.TryGetValue(step.CommandId, out var sendCommand))
                {
                    request = sendCommand;
                    continue;
                }

                if (request is not null && response is null && _listenPacketsById.TryGetValue(step.CommandId, out var listenPacket))
                {
                    response = listenPacket;
                    break;
                }
            }

            if (request is not null && response is not null)
            {
                break;
            }
        }

        if (request is null || response is null)
        {
            throw new InvalidOperationException($"ADX macro '{macroId}' does not resolve to a send/listen exchange.");
        }

        return new AdxExchange(request, response);
    }

    private double EvaluateValue(
        AdxValueDefinition definition,
        ReadOnlySpan<byte> payload,
        IDictionary<string, double> cache,
        ISet<string> stack)
    {
        if (cache.TryGetValue(definition.IdHash, out var cached))
        {
            return cached;
        }

        if (!stack.Add(definition.IdHash))
        {
            throw new InvalidOperationException($"Circular ADX dependency detected for '{definition.DisplayName}'.");
        }

        try
        {
            var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var needsNativeValue = definition.Variables.Any(variable => variable.Kind == AdxVariableKind.Native);
            var nativeValue = needsNativeValue ? ExtractNativeValue(definition, payload) : 0d;

            foreach (var variable in definition.Variables)
            {
                switch (variable.Kind)
                {
                    case AdxVariableKind.Native:
                        variables[variable.Name] = nativeValue;
                        break;
                    case AdxVariableKind.Link:
                        if (variable.LinkIdHash is null || !_valuesByIdHash.TryGetValue(variable.LinkIdHash, out var linkedDefinition))
                        {
                            throw new InvalidOperationException($"ADX link '{variable.LinkIdHash}' for '{definition.DisplayName}' could not be resolved.");
                        }

                        variables[variable.Name] = EvaluateValue(linkedDefinition, payload, cache, stack);
                        break;
                }
            }

            var computedValue = ExpressionEvaluator.Evaluate(definition.Equation, variables);
            if (definition.LookupIdHash is not null && _lookupTablesByIdHash.TryGetValue(definition.LookupIdHash, out var lookupTable))
            {
                computedValue = lookupTable.Apply(computedValue);
            }

            cache[definition.IdHash] = computedValue;
            return computedValue;
        }
        finally
        {
            stack.Remove(definition.IdHash);
        }
    }

    /// <summary>The channel's integer value as it sits in the payload, before its equation.</summary>
    public static long ReadRaw(AdxValueDefinition value, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.FloatingPoint)
        {
            throw new InvalidOperationException($"'{value.DisplayName}' is a floating-point value and has no integer raw value.");
        }

        return (long)ExtractNativeValue(value, payload);
    }

    private static double ExtractNativeValue(AdxValueDefinition definition, ReadOnlySpan<byte> payload)
    {
        if (definition.SizeInBits <= 0)
        {
            return 0d;
        }

        if (definition.SizeInBits % 8 != 0)
        {
            throw new InvalidOperationException($"Non byte-aligned ADX values are not supported ({definition.DisplayName}).");
        }

        var byteCount = definition.SizeInBits / 8;
        if (definition.PacketOffset < 0 || definition.PacketOffset + byteCount > payload.Length)
        {
            throw new InvalidOperationException(
                $"Payload length {payload.Length} is too short for '{definition.DisplayName}' at offset {definition.PacketOffset}.");
        }

        ulong rawValue = 0;
        if (definition.LsbFirst)
        {
            for (var index = 0; index < byteCount; index++)
            {
                rawValue |= (ulong)payload[definition.PacketOffset + index] << (8 * index);
            }
        }
        else
        {
            for (var index = 0; index < byteCount; index++)
            {
                rawValue = (rawValue << 8) | payload[definition.PacketOffset + index];
            }
        }

        if (definition.FloatingPoint)
        {
            return definition.SizeInBits switch
            {
                32 => BitConverter.Int32BitsToSingle(unchecked((int)rawValue)),
                64 => BitConverter.Int64BitsToDouble(unchecked((long)rawValue)),
                _ => throw new InvalidOperationException($"Unsupported floating point size {definition.SizeInBits} for '{definition.DisplayName}'."),
            };
        }

        if (definition.Signed)
        {
            return SignExtend(rawValue, definition.SizeInBits);
        }

        return rawValue;
    }

    private static long SignExtend(ulong rawValue, int bitCount)
    {
        if (bitCount <= 0 || bitCount > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }

        var shift = 64 - bitCount;
        return ((long)(rawValue << shift)) >> shift;
    }

    private static AdxValueDefinition ParseValue(XElement element, AdxDefaults defaults)
    {
        var mathElement = element.Element("MATH") ?? throw new InvalidOperationException($"ADX value '{(string?)element.Attribute("id")}' is missing a MATH node.");
        var variables = mathElement.Elements("VAR")
            .Select(variable => new AdxVariableReference(
                Name: (string?)variable.Attribute("varID") ?? throw new InvalidOperationException("ADX VAR is missing varID."),
                Kind: ParseVariableKind((string?)variable.Attribute("type")),
                LinkIdHash: NormalizeHash((string?)variable.Attribute("linkIDHash"))))
            .ToList();

        return new AdxValueDefinition(
            Id: (string?)element.Attribute("id") ?? throw new InvalidOperationException("ADX value is missing an id."),
            IdHash: NormalizeHash((string?)element.Attribute("idhash")) ?? throw new InvalidOperationException("ADX value is missing an idhash."),
            Title: (string?)element.Attribute("title") ?? string.Empty,
            Units: element.Element("units")?.Value,
            PacketOffset: ParseInt(element.Element("packetoffset")?.Value, 0),
            SizeInBits: ParseInt(element.Element("sizeinbits")?.Value, defaults.DataSizeBits),
            Signed: ParseBoolIndicator((string?)element.Attribute("signed"), defaults.Signed),
            LsbFirst: ParseBoolIndicator((string?)element.Attribute("lsbfirst"), defaults.LsbFirst),
            FloatingPoint: ParseBoolIndicator((string?)element.Attribute("float"), defaults.FloatingPoint),
            DigitCount: ParseInt(element.Element("digcount")?.Value, defaults.SignificantDigits),
            OutputType: ParseInt(element.Element("outputtype")?.Value, defaults.OutputType),
            Equation: (string?)mathElement.Attribute("equation") ?? "0",
            LookupIdHash: NormalizeHash((string?)mathElement.Attribute("lookupidhash")),
            Variables: variables.AsReadOnly());
    }

    private static AdxLookupTable ParseLookupTable(XElement element)
    {
        var entries = element.Elements("tableentry")
            .Select(entry => new AdxLookupEntry(
                ParseDouble((string?)entry.Attribute("input"), 0d),
                ParseDouble((string?)entry.Attribute("output"), 0d)))
            .OrderBy(entry => entry.Input)
            .ToList();

        return new AdxLookupTable(
            Id: (string?)element.Attribute("id") ?? throw new InvalidOperationException("ADX lookup table is missing an id."),
            IdHash: NormalizeHash((string?)element.Attribute("idhash")) ?? throw new InvalidOperationException("ADX lookup table is missing an idhash."),
            Title: (string?)element.Attribute("title") ?? string.Empty,
            LookupMode: ParseInt(element.Element("lookupmode")?.Value, 3),
            Entries: entries.AsReadOnly());
    }

    private static AdxSendCommand ParseSendCommand(XElement element)
    {
        return new AdxSendCommand(
            Id: (string?)element.Attribute("id") ?? throw new InvalidOperationException("ADX send command is missing an id."),
            Title: (string?)element.Attribute("title") ?? string.Empty,
            Baud: ParseOptionalInt(element.Element("baud")?.Value),
            Bytes: ParseByteString(element.Element("bytestring")?.Value));
    }

    private static AdxListenPacket ParseListenPacket(XElement element)
    {
        return new AdxListenPacket(
            Id: (string?)element.Attribute("id") ?? throw new InvalidOperationException("ADX listen packet is missing an id."),
            Title: (string?)element.Attribute("title") ?? string.Empty,
            Baud: ParseOptionalInt(element.Element("baud")?.Value),
            Header: ParseByteString(element.Element("headerstring")?.Value),
            PacketSize: ParseInt(element.Element("packetsize")?.Value, 0),
            ListenTimeout: ParseInt(element.Element("listentimeout")?.Value, 250));
    }

    private static AdxMacro ParseMacro(XElement element)
    {
        var steps = element.Elements("NODE")
            .Select(node => new AdxMacroStep(
                CommandId: (string?)node.Attribute("commandID") ?? throw new InvalidOperationException("ADX macro step is missing commandID."),
                RepeatCount: ParseInt((string?)node.Attribute("repeatcount"), 1)))
            .ToList();

        return new AdxMacro(
            Id: (string?)element.Attribute("id") ?? throw new InvalidOperationException("ADX macro is missing an id."),
            Title: (string?)element.Attribute("title") ?? string.Empty,
            Steps: steps.AsReadOnly());
    }

    private static AdxVariableKind ParseVariableKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "native" => AdxVariableKind.Native,
            "link" => AdxVariableKind.Link,
            _ => throw new InvalidOperationException($"Unsupported ADX variable type '{value}'."),
        };
    }

    private static Parity ParseParity(int parityCode)
    {
        return parityCode switch
        {
            0 => Parity.None,
            1 => Parity.Odd,
            2 => Parity.Even,
            3 => Parity.Mark,
            4 => Parity.Space,
            _ => Parity.Even,
        };
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(value[2..], 16);
        }

        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static int? ParseOptionalInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseInt(value, 0);
    }

    private static double ParseDouble(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return double.Parse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static bool ParseBoolIndicator(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim() switch
        {
            "0" => false,
            "1" => true,
            _ => bool.Parse(value),
        };
    }

    private static string? NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.StartsWith("0X", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('0');
        if (normalized.Length == 0)
        {
            normalized = "0";
        }

        return "0X" + normalized;
    }

    private static byte[] ParseByteString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        value = value.Trim();
        if (value.Length % 2 != 0)
        {
            throw new InvalidOperationException($"Invalid hex byte string '{value}'.");
        }

        var buffer = new byte[value.Length / 2];
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
        }

        return buffer;
    }
}

public sealed record AdxHeader(
    string Description,
    int InitialBaud,
    Parity InitialParity,
    string ConnectCommandId,
    string MonitorCommandId,
    string DisconnectCommandId,
    AdxDefaults Defaults);

public sealed record AdxDefaults(
    int DataSizeBits,
    int SignificantDigits,
    int OutputType,
    bool Signed,
    bool LsbFirst,
    bool FloatingPoint);

public sealed record AdxValueDefinition(
    string Id,
    string IdHash,
    string Title,
    string? Units,
    int PacketOffset,
    int SizeInBits,
    bool Signed,
    bool LsbFirst,
    bool FloatingPoint,
    int DigitCount,
    int OutputType,
    string Equation,
    string? LookupIdHash,
    IReadOnlyList<AdxVariableReference> Variables)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Id : Title;
}

public enum AdxVariableKind
{
    Native = 0,
    Link = 1,
}

public sealed record AdxVariableReference(string Name, AdxVariableKind Kind, string? LinkIdHash);

public sealed record AdxLookupTable(string Id, string IdHash, string Title, int LookupMode, IReadOnlyList<AdxLookupEntry> Entries)
{
    public double Apply(double input)
    {
        if (Entries.Count == 0)
        {
            return input;
        }

        var exactMatch = Entries.FirstOrDefault(entry => Math.Abs(entry.Input - input) < 0.000001d);
        if (exactMatch is not null)
        {
            return exactMatch.Output;
        }

        if (input <= Entries[0].Input)
        {
            return Entries[0].Output;
        }

        if (input >= Entries[^1].Input)
        {
            return Entries[^1].Output;
        }

        for (var index = 0; index < Entries.Count - 1; index++)
        {
            var current = Entries[index];
            var next = Entries[index + 1];
            if (input > next.Input)
            {
                continue;
            }

            if (Math.Abs(next.Input - current.Input) < 0.000001d)
            {
                return next.Output;
            }

            var ratio = (input - current.Input) / (next.Input - current.Input);
            return current.Output + ((next.Output - current.Output) * ratio);
        }

        return Entries[^1].Output;
    }
}

public sealed record AdxLookupEntry(double Input, double Output);

public sealed record AdxSendCommand(string Id, string Title, int? Baud, byte[] Bytes);

public sealed record AdxListenPacket(string Id, string Title, int? Baud, byte[] Header, int PacketSize, int ListenTimeout);

public sealed record AdxMacro(string Id, string Title, IReadOnlyList<AdxMacroStep> Steps);

public sealed record AdxMacroStep(string CommandId, int RepeatCount);

public sealed record AdxExchange(AdxSendCommand Request, AdxListenPacket Response);

public sealed record DecodedValue(AdxValueDefinition Definition, double Value);
