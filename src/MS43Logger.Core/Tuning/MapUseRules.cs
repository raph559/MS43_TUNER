using System.Globalization;
using System.Text.Json;

namespace MS43Logger.Core.Tuning;

public enum MapUseState
{
    InUse,
    NotInUse,
    Possible,
    Unknown,
}

/// <summary>A value the ECU picks maps with, as the logger frame carries it: <c>(raw &amp; Mask) &gt;&gt; Shift</c> of the channel at <see cref="PacketOffset"/>.</summary>
public sealed record UseSelector(string Id, string Ram, string Channel, int PacketOffset, int Bits, long Mask, string Evidence, int Shift = 0);

/// <summary>
/// A calibration curve the ECU compares a selector with, read from the open bin: unsigned <see cref="Bits"/>-bit values at <see cref="Data"/>,
/// searched without interpolation on the axis whose count sits at <see cref="Axis"/>, with the selector <see cref="Input"/>.
/// </summary>
public sealed record UseCurve(string Id, long Data, string Name, int Bits, long Axis, int AxisBits, string Input, string Evidence);

/// <summary>
/// True when <c>selector &amp; Mask</c> is one of <see cref="Values"/>, or is not one of them when <see cref="Negate"/> is set.
/// With <see cref="AboveCurve"/> set it is instead true when the selector is above that curve at the curve's input.
/// </summary>
public sealed record UseCondition(string Selector, long Mask, IReadOnlyList<long> Values, bool Negate, string? AboveCurve = null);

/// <summary>A case matches when all its conditions hold; an empty list always matches.</summary>
public sealed record UseCase(IReadOnlyList<UseCondition> When, MapUseState Then, string? Why);

public sealed record CalibrationRequirement(long Address, long Value);

/// <summary>A blend factor <c>(raw − Offset) / Scale</c>, taken from a selector or from a calibration byte that holds while <see cref="Requires"/> does.</summary>
public sealed record UseFactor(string Id, string? Selector, long? Calibration, long Offset, double Scale, IReadOnlyList<CalibrationRequirement> Requires, string Evidence);

/// <param name="Weight">Factor ids, or "1-" and a factor id, multiplied together; empty means 1.</param>
public sealed record UseMember(long Data, string Name, string Label, IReadOnlyList<string> Weight);

/// <param name="Selection">"observed" (the selection was read in the code) or "inferred".</param>
/// <param name="Inferred">What the rule's outcome rests on that was inferred rather than read in the code; null when nothing is.</param>
/// <param name="Blend">"weights" (each member's product of factors) or "overlapIndex" (the VE maps' index and fraction).</param>
public sealed record MapUseRule(string Id, string Title, string Selection, string? Inferred, string Evidence, string? Caveat, string Blend, string? IndexSelector,
    IReadOnlyList<UseCase> Cases, IReadOnlyList<UseMember> Members);

/// <summary>Firmware-specific map-selection rules and their evidence from definitions/*_map_use.json.</summary>
public sealed class MapUseRules
{
    private static readonly Dictionary<string, string> BundledFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["43X001"] = "ms43x001_map_use.json",
    };

    private readonly Dictionary<long, MapUseRule> _rules;
    private readonly Dictionary<long, string> _leftOut;

    private MapUseRules(string softwareId, IReadOnlyDictionary<string, string> provenance, IReadOnlyDictionary<string, UseSelector> selectors,
        IReadOnlyDictionary<string, UseCurve> curves, IReadOnlyDictionary<string, UseFactor> factors, IReadOnlyList<MapUseRule> rules, Dictionary<long, string> leftOut)
    {
        SoftwareId = softwareId;
        Provenance = provenance;
        Selectors = selectors;
        Curves = curves;
        Factors = factors;
        Rules = rules;
        _leftOut = leftOut;
        _rules = new Dictionary<long, MapUseRule>();
        foreach (var rule in rules)
        {
            foreach (var member in rule.Members)
            {
                if (!_rules.TryAdd(member.Data, rule) || leftOut.ContainsKey(member.Data))
                {
                    throw new InvalidOperationException($"the table 0x{member.Data:X} is listed twice");
                }
            }
        }
    }

    public string SoftwareId { get; }

    public IReadOnlyDictionary<string, string> Provenance { get; }

    public IReadOnlyDictionary<string, UseSelector> Selectors { get; }

    public IReadOnlyDictionary<string, UseCurve> Curves { get; }

    public IReadOnlyDictionary<string, UseFactor> Factors { get; }

    public IReadOnlyList<MapUseRule> Rules { get; }

    public MapUseRule? FindRule(long data) => _rules.GetValueOrDefault(data);

    /// <summary>Why a table was looked at and left without a rule, or null.</summary>
    public string? LeftOutReason(long data) => _leftOut.GetValueOrDefault(data);

    public static MapUseRules? LoadBundled(string? softwareId)
    {
        if (softwareId is null || !BundledFiles.TryGetValue(softwareId, out var fileName))
        {
            return null;
        }

        var assembly = typeof(MapUseRules).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded map-use rules '{fileName}' were not found.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return Load(stream);
    }

    public static MapUseRules Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var provenance = Required(root, "provenance").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
            var selectors = Required(root, "selectors").EnumerateArray()
                .Select(s => new UseSelector(Text(s, "id"), Text(s, "ram"), Text(s, "channel"), (int)Hex(Text(s, "offset")), Required(s, "bits").GetInt32(),
                    Hex(Text(s, "mask")), Text(s, "evidence"), s.TryGetProperty("shift", out var shift) ? shift.GetInt32() : 0))
                .ToDictionary(s => s.Id, StringComparer.Ordinal);
            var curves = Required(root, "curves").EnumerateArray()
                .Select(c => ReadCurve(c, selectors))
                .ToDictionary(c => c.Id, StringComparer.Ordinal);
            var factors = Required(root, "factors").EnumerateArray()
                .Select(f => new UseFactor(
                    Text(f, "id"),
                    Optional(f, "selector") is { } selector ? Known(selectors, selector, "selector") : null,
                    Optional(f, "cal") is { } cal ? Hex(cal) : null,
                    Required(f, "offset").GetInt64(),
                    Required(f, "scale").GetDouble(),
                    Required(f, "requires").EnumerateArray().Select(r => new CalibrationRequirement(Hex(Text(r, "cal")), Required(r, "equals").GetInt64())).ToList(),
                    Text(f, "evidence")))
                .ToDictionary(f => f.Id, StringComparer.Ordinal);
            var rules = Required(root, "rules").EnumerateArray().Select(r => ReadRule(r, selectors, curves, factors)).ToList();
            var leftOut = new Dictionary<long, string>();
            foreach (var entry in Required(root, "leftOut").EnumerateArray())
            {
                foreach (var data in Required(entry, "data").EnumerateArray())
                {
                    leftOut.Add(Hex(data.GetString()!), Text(entry, "why"));
                }
            }

            return new MapUseRules(Text(root, "softwareId"), provenance, selectors, curves, factors, rules, leftOut);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException($"The map-use rules cannot be read: {error.Message}", error);
        }
    }

    private static UseCurve ReadCurve(JsonElement c, Dictionary<string, UseSelector> selectors)
    {
        if (Required(c, "interpolate").GetBoolean())
        {
            throw new InvalidOperationException($"the curve \"{Text(c, "id")}\" interpolates, which the rules do not model");
        }

        return new UseCurve(Text(c, "id"), Hex(Text(c, "data")), Text(c, "name"), Required(c, "bits").GetInt32(), Hex(Text(c, "axis")), Required(c, "axisBits").GetInt32(),
            Known(selectors, Text(c, "input"), "selector"), Text(c, "evidence"));
    }

    private static MapUseRule ReadRule(JsonElement r, Dictionary<string, UseSelector> selectors, Dictionary<string, UseCurve> curves, Dictionary<string, UseFactor> factors)
    {
        var selection = Text(r, "selection");
        if (selection is not ("observed" or "inferred"))
        {
            throw new InvalidOperationException($"unknown selection \"{selection}\" (observed or inferred)");
        }

        var blend = Text(r, "blend");
        if (blend is not ("weights" or "overlapIndex"))
        {
            throw new InvalidOperationException($"unknown blend \"{blend}\"");
        }

        var cases = Required(r, "cases").EnumerateArray()
            .Select(c => new UseCase(
                Required(c, "when").EnumerateArray().Select(w => ReadCondition(w, selectors, curves)).ToList(),
                Text(c, "then") switch
                {
                    "use" => MapUseState.InUse,
                    "notInUse" => MapUseState.NotInUse,
                    "possible" => MapUseState.Possible,
                    "unknown" => MapUseState.Unknown,
                    var other => throw new InvalidOperationException($"unknown case result \"{other}\""),
                },
                Optional(c, "why")))
            .ToList();
        var members = Required(r, "members").EnumerateArray()
            .Select(m => new UseMember(
                Hex(Text(m, "data")),
                Text(m, "name"),
                Text(m, "label"),
                Required(m, "weight").EnumerateArray().Select(w => WeightTerm(factors, w.GetString()!)).ToList()))
            .ToList();
        var indexSelector = Optional(r, "indexSelector") is { } index ? Known(selectors, index, "selector") : null;
        if (blend == "overlapIndex" && indexSelector is null)
        {
            throw new InvalidOperationException("an overlapIndex rule needs an indexSelector");
        }

        return new MapUseRule(Text(r, "id"), Text(r, "title"), selection, Optional(r, "inferred"), Text(r, "evidence"), Optional(r, "caveat"), blend, indexSelector, cases, members);
    }

    private static UseCondition ReadCondition(JsonElement w, Dictionary<string, UseSelector> selectors, Dictionary<string, UseCurve> curves)
    {
        if (Optional(w, "above") is { } curve)
        {
            return new UseCondition(Known(selectors, Text(w, "selector"), "selector"), -1, Array.Empty<long>(), false, Known(curves, curve, "curve"));
        }

        var negate = w.TryGetProperty("notIn", out var values);
        if (!negate)
        {
            values = Required(w, "in");
        }

        return new UseCondition(Known(selectors, Text(w, "selector"), "selector"), Hex(Text(w, "mask")), values.EnumerateArray().Select(v => v.GetInt64()).ToList(), negate);
    }

    private static string WeightTerm(Dictionary<string, UseFactor> factors, string term)
    {
        Known(factors, term.StartsWith("1-", StringComparison.Ordinal) ? term[2..] : term, "factor");
        return term;
    }

    private static string Known<T>(Dictionary<string, T> items, string id, string kind) =>
        items.ContainsKey(id) ? id : throw new KeyNotFoundException($"the {kind} \"{id}\" is not defined");

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new KeyNotFoundException($"the field \"{name}\" is missing");

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Text(JsonElement element, string name) =>
        Required(element, name).GetString() ?? throw new KeyNotFoundException($"the field \"{name}\" is empty");

    private static long Hex(string text) =>
        long.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
