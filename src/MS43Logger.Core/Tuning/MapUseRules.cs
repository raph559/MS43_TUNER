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
}
