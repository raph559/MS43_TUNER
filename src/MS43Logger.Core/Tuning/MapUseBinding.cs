using System.Globalization;

namespace MS43Logger.Core.Tuning;

public sealed record MapShare(long Data, string Label, double Weight);

/// <summary>Whether the ECU uses a map for one sample, the share of each map in its blend, and the label the Tune tab shows.</summary>
/// <param name="Detail">Why the use is unknown, for a tooltip; null otherwise.</param>
public sealed record MapUse(MapUseState State, string Text, IReadOnlyList<MapShare> Shares, string? Detail)
{
    public const string UnknownText = "Selection unknown";

    public static MapUse Unknown(string? detail) => new(MapUseState.Unknown, UnknownText, Array.Empty<MapShare>(), detail);
}

/// <summary>
/// The map-use rule of one table, with its calibration factors and curves read from the open bin. Rebind when the bin changes:
/// they are read once.
/// </summary>
public sealed class MapUseBinding
{
    private const string NoRulesForFirmware = "No rule: there are no map-use rules for this firmware.";
    private const string NoRule = "No rule: how the ECU picks this map has not been confirmed.";

    private readonly MapUseRules? _rules;
    private readonly long _data;
    private readonly string? _noRule;
    private readonly Dictionary<string, double> _calibration;
    private readonly string? _calibrationProblem;
    private readonly Dictionary<string, CurveValues> _curves;

    private MapUseBinding(MapUseRules? rules, MapUseRule? rule, long data, string? noRule, Dictionary<string, double> calibration, string? calibrationProblem,
        Dictionary<string, CurveValues>? curves = null)
    {
        _rules = rules;
        Rule = rule;
        _data = data;
        _noRule = noRule;
        _calibration = calibration;
        _calibrationProblem = calibrationProblem;
        _curves = curves ?? new Dictionary<string, CurveValues>(StringComparer.Ordinal);
    }

    public MapUseRule? Rule { get; }

    public static MapUseBinding Resolve(MapUseRules? rules, BinImage image, long data)
    {
        ArgumentNullException.ThrowIfNull(image);
        var calibration = new Dictionary<string, double>(StringComparer.Ordinal);
        if (rules is null)
        {
            return new MapUseBinding(null, null, data, NoRulesForFirmware, calibration, null);
        }

        var rule = rules.FindRule(data);
        if (rule is null)
        {
            return new MapUseBinding(rules, null, data, rules.LeftOutReason(data) ?? NoRule, calibration, null);
        }

        string? problem = null;
        foreach (var id in rule.Members.SelectMany(member => member.Weight).Select(FactorId).Distinct())
        {
            var factor = rules.Factors[id];
            if (factor.Calibration is not long address)
            {
                continue;
            }

            var unmet = factor.Requires.FirstOrDefault(requirement => ReadByte(image, requirement.Address) != requirement.Value);
            if (unmet is not null)
            {
                problem ??= $"The calibration byte at 0x{unmet.Address:X} is {ReadByte(image, unmet.Address)}, not {unmet.Value}, so {factor.Id} is not known ({factor.Evidence}).";
                continue;
            }

            var value = (ReadByte(image, address) - factor.Offset) / factor.Scale;
            if (value is < 0 or > 1)
            {
                problem ??= $"The blend factor {factor.Id} is {value.ToString("0.###", CultureInfo.InvariantCulture)}, outside the blend ({factor.Evidence}).";
                continue;
            }

            calibration[id] = value;
        }

        var curves = rule.Cases.SelectMany(c => c.When).Select(condition => condition.AboveCurve).OfType<string>().Distinct()
            .ToDictionary(id => id, id => ReadCurve(image, rules.Curves[id]), StringComparer.Ordinal);
        return new MapUseBinding(rules, rule, data, null, calibration, problem, curves);
    }

    /// <summary>A curve's breakpoints and values, or why they cannot be read from the bin.</summary>
    private sealed record CurveValues(UseCurve Curve, IReadOnlyList<long> Breakpoints, IReadOnlyList<long> Values, string? Problem);

    private static CurveValues ReadCurve(BinImage image, UseCurve curve)
    {
        var breakpoints = LiveTableBinding.ReadBreakpoints(image, new CatalogAxis(curve.Axis, curve.AxisBits, curve.Input));
        var values = new XdfData(curve.Data, curve.Bits, false, true, Math.Max(1, breakpoints?.Count ?? 0), 1, null);
        if (breakpoints is not { Count: > 0 } || !image.Contains(values))
        {
            return new CurveValues(curve, Array.Empty<long>(), Array.Empty<long>(),
                $"The curve {curve.Name} at 0x{curve.Data:X} cannot be read from the bin (its axis at 0x{curve.Axis:X}), so the choice is not known ({curve.Evidence}).");
        }

        return new CurveValues(curve, breakpoints, Enumerable.Range(0, breakpoints.Count).Select(i => image.ReadElement(values, i)).ToList(), null);
    }

    public MapUse Evaluate(AdxDefinition adx, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(adx);
        if (Rule is not { } rule || _rules is not { } rules)
        {
            return MapUse.Unknown(_noRule);
        }

        var reader = new SelectorReader(rules, adx, payload.ToArray());
        try
        {
            var match = rule.Cases.FirstOrDefault(c => c.When.All(condition => Holds(condition, reader, _curves)));
            switch (match?.Then)
            {
                case null:
                    return MapUse.Unknown($"No case of the rule {rule.Id} matches this sample.");
                case MapUseState.NotInUse:
                    return new MapUse(MapUseState.NotInUse, "Not in use", Array.Empty<MapShare>(), null);
                case MapUseState.Unknown:
                    return MapUse.Unknown(match.Why);
            }

            if (_calibrationProblem is not null)
            {
                return MapUse.Unknown(_calibrationProblem);
            }

            var shares = Weights(rule, reader);
            var own = shares.Single(share => share.Data == _data).Weight;
            if (match.Then == MapUseState.Possible)
            {
                return own > 0
                    ? new MapUse(MapUseState.Possible, "Possibly in use", Array.Empty<MapShare>(), null)
                    : new MapUse(MapUseState.NotInUse, "Not in use", Array.Empty<MapShare>(), null);
            }

            var used = shares.Where(share => share.Weight > 0)
                .OrderBy(share => share.Data == _data ? 0 : 1)
                .ThenByDescending(share => share.Weight)
                .ToList();
            var list = ShareList(used);
            if (own <= 0)
            {
                return new MapUse(MapUseState.NotInUse, used.Count > 0 ? "Not in use · " + list : "Not in use", used, null);
            }

            return new MapUse(MapUseState.InUse, used.Count == 1 ? "In use" : list, used, null);
        }
        catch (CannotTellException missing)
        {
            return MapUse.Unknown(missing.Message);
        }
        catch (InvalidOperationException error)
        {
            return MapUse.Unknown($"The sample could not be read ({error.Message}).");
        }
    }

    /// <summary>
    /// "Not in use" with the maps that count, when this map's own share of its blend is 0 whatever the rule's case: the ECU
    /// may read the map, but its value is multiplied by 0 (an E85 map with the bin's flex-fuel substitute at 0 %, for
    /// example). Null when the share is above 0 or cannot be told.
    /// </summary>
    public MapUse? ZeroShare(AdxDefinition adx, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(adx);
        if (Rule is not { } rule || _rules is not { } rules || _calibrationProblem is not null)
        {
            return null;
        }

        try
        {
            var shares = Weights(rule, new SelectorReader(rules, adx, payload.ToArray()));
            if (shares.Single(share => share.Data == _data).Weight > 0)
            {
                return null;
            }

            var used = shares.Where(share => share.Weight > 0).OrderByDescending(share => share.Weight).ToList();
            return new MapUse(MapUseState.NotInUse, used.Count > 0 ? "Not in use · " + ShareList(used) : "Not in use", used, null);
        }
        catch (CannotTellException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string ShareList(IEnumerable<MapShare> used) => string.Join(" · ", used.Select(share => $"{share.Label} {Percent(share.Weight)}"));

    private List<MapShare> Weights(MapUseRule rule, SelectorReader reader)
    {
        if (rule.Blend == "overlapIndex")
        {
            // 0x3B9DA: i = E31D − 1 clamped to 7 unsigned; below 7 the maps i and i+1 get (255 − F)/256 and F/256, at 7 the last map alone.
            var selector = reader.Read(rule.IndexSelector!);
            var fraction = selector & 0xFF;
            var index = (int)Math.Min(((selector >> 8) - 1) & 0xFF, 7);
            var weights = new double[rule.Members.Count];
            if (index == 7)
            {
                weights[7] = 1;
            }
            else
            {
                weights[index] = (255 - fraction) / 256.0;
                weights[index + 1] = fraction / 256.0;
            }

            return rule.Members.Select((member, i) => new MapShare(member.Data, member.Label, weights[i])).ToList();
        }

        return rule.Members.Select(member => new MapShare(member.Data, member.Label, member.Weight.Aggregate(1.0, (product, term) => product * Term(term, reader)))).ToList();
    }

    private double Term(string term, SelectorReader reader)
    {
        var id = FactorId(term);
        var value = _calibration.TryGetValue(id, out var fixedValue) ? fixedValue : SelectorFactor(_rules!.Factors[id], reader);
        return term.StartsWith("1-", StringComparison.Ordinal) ? 1 - value : value;
    }

    private static double SelectorFactor(UseFactor factor, SelectorReader reader) => (reader.Read(factor.Selector!) - factor.Offset) / factor.Scale;

    private static bool Holds(UseCondition condition, SelectorReader reader, Dictionary<string, CurveValues> curves)
    {
        if (condition.AboveCurve is not { } id)
        {
            return condition.Values.Contains(reader.Read(condition.Selector) & condition.Mask) != condition.Negate;
        }

        var curve = curves[id];
        if (curve.Problem is not null)
        {
            throw new CannotTellException(curve.Problem);
        }

        // Not interpolated (interp false in the code catalogue): the value at the highest breakpoint at or below the input, clamped to the ends.
        var position = AxisSearch.Find(curve.Breakpoints, reader.Read(curve.Curve.Input), false);
        return reader.Read(condition.Selector) > curve.Values[position.Index];
    }

    private static string FactorId(string term) => term.StartsWith("1-", StringComparison.Ordinal) ? term[2..] : term;

    private static long ReadByte(BinImage image, long address) => image.ReadElement(new XdfData(address, 8, false, true, 1, 1, null), 0);

    /// <summary>"72 %"; a share above 0 that rounds to 0 is "&lt;1 %".</summary>
    private static string Percent(double weight)
    {
        var rounded = Math.Round(weight * 100, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "<1 %" : rounded.ToString("0", CultureInfo.InvariantCulture) + " %";
    }

    private sealed class CannotTellException(string message) : Exception(message);

    /// <summary>Reads each selector from the payload once: a value channel by its own byte order, a bitmask channel as its byte.</summary>
    private sealed class SelectorReader(MapUseRules rules, AdxDefinition adx, byte[] payload)
    {
        private readonly Dictionary<string, long> _values = new(StringComparer.Ordinal);

        public long Read(string id)
        {
            if (_values.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var selector = rules.Selectors[id];
            var value = adx.Values.FirstOrDefault(v => string.Equals(v.Id, selector.Channel, StringComparison.OrdinalIgnoreCase));
            long raw;
            if (value is not null && value.PacketOffset == selector.PacketOffset && value.SizeInBits == selector.Bits && !value.FloatingPoint)
            {
                raw = AdxDefinition.ReadRaw(value, payload);
            }
            else if (selector.Bits == 8 && adx.Bitmasks.Any(b => string.Equals(b.Id, selector.Channel, StringComparison.OrdinalIgnoreCase) && b.PacketOffset == selector.PacketOffset))
            {
                raw = selector.PacketOffset < payload.Length ? payload[selector.PacketOffset]
                    : throw new InvalidOperationException($"the payload is too short for {selector.Channel}");
            }
            else
            {
                throw new CannotTellException($"The logger definition {adx.SourceName} does not carry {selector.Channel} at offset 0x{selector.PacketOffset:X2}.");
            }

            return _values[id] = (raw & selector.Mask) >> selector.Shift;
        }
    }
}
