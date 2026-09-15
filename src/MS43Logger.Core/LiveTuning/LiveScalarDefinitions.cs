using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.LiveTuning;

/// <summary>The XDF constants (or, for pops, the app's own definitions) behind each field of a settings group.</summary>
public static class LiveScalarDefinitions
{
    /// <summary>One constant per field, in field order; null when the XDF lacks any of them.</summary>
    public static IReadOnlyList<XdfConstant>? ConstantsFor(OverlayMap map, XdfDefinition xdf)
    {
        ArgumentNullException.ThrowIfNull(xdf);
        if (map.ScalarGroup is not { } group) return null;
        var constants = new List<XdfConstant>();
        foreach (var field in group.Fields)
        {
            if (field.Definition is { } own)
            {
                constants.Add(new XdfConstant(-(map.Index * 16 + field.Offset), own.Title, own.Description, Array.Empty<int>(),
                    new XdfData(map.Address + field.Offset, 8, false, true, 1, 1, null),
                    new XdfMath(own.Equation, new[] { new XdfVariable("X", XdfVariableKind.Raw, null) }),
                    own.Units, own.Decimals, null, own.RangeLow, own.RangeHigh));
                continue;
            }
            var matches = xdf.Constants.Where(c => c.Title == field.Key && c.Data.Address is { } address
                && BinImage.ToOffset(address) == map.Address + field.Offset
                && c.Data is { ElementBits: 8, Signed: false, IsSupported: true, Count: 1 }).ToArray();
            if (matches.Length != 1) return null;
            constants.Add(matches[0]);
        }
        return constants;
    }
}
