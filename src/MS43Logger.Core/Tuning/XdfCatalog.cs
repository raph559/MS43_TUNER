using System.Globalization;
using System.Text.RegularExpressions;

namespace MS43Logger.Core.Tuning;

public enum ItemKind
{
    Map,
    Curve,
    Axis,
    Value,
    Switch,
    Flag,
}

/// <summary>Browsing helpers over an XDF: item kinds, switch choices, categories and search.</summary>
public sealed partial class XdfCatalog
{
    public const string OtherCategory = "Other";

    private readonly XdfDefinition _xdf;
    private readonly List<XdfItem> _all;
    private readonly Dictionary<XdfConstant, IReadOnlyList<(long Value, string Label)>?> _choices = new();

    public XdfCatalog(XdfDefinition xdf)
    {
        _xdf = xdf ?? throw new ArgumentNullException(nameof(xdf));
        _all = xdf.Tables.Cast<XdfItem>().Concat(xdf.Constants).Concat(xdf.Flags).ToList();
    }

    public IReadOnlyList<XdfItem> All => _all;

    public ItemKind Kind(XdfItem item) => item switch
    {
        XdfFlag => ItemKind.Flag,
        XdfConstant constant => Choices(constant) is null ? ItemKind.Value : ItemKind.Switch,
        XdfTable table when TableEdits.IsAxis(_xdf, table) => ItemKind.Axis,
        XdfTable table when table.Z.Data.Rows == 1 || table.Z.Data.Cols == 1 => ItemKind.Curve,
        _ => ItemKind.Map,
    };

    /// <summary>The "n: label" lines of a configuration switch's description, or null when it has fewer than two.</summary>
    public IReadOnlyList<(long Value, string Label)>? Choices(XdfConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (!_choices.TryGetValue(constant, out var choices))
        {
            var found = ChoiceLine().Matches(constant.Description)
                .Select(match => (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value.Trim()))
                .ToList();
            choices = found.Count >= 2 ? found : null;
            _choices[constant] = choices;
        }

        return choices;
    }

    public IReadOnlyList<(string Category, int Count)> Categories() =>
        _all.SelectMany(CategoryNames)
            .GroupBy(name => name)
            .Select(group => (group.Key, group.Count()))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<XdfItem> InCategory(string category) =>
        _all.Where(item => CategoryNames(item).Contains(category)).OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<XdfTable> AxisUsers(XdfTable axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        return _xdf.Tables.Where(table => table.X.LinkedTableId == axis.UniqueId || table.Y.LinkedTableId == axis.UniqueId).ToList();
    }

    /// <summary>Title prefix matches first, then title matches, then description matches; case-insensitive.</summary>
    public IReadOnlyList<XdfItem> Search(string text, int max = 200)
    {
        var query = (text ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return Array.Empty<XdfItem>();
        }

        int Rank(XdfItem item) =>
            item.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 :
            item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 :
            item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;

        return _all.Select(item => (Item: item, Rank: Rank(item)))
            .Where(pair => pair.Rank < 3)
            .OrderBy(pair => pair.Rank)
            .ThenBy(pair => pair.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(pair => pair.Item)
            .ToList();
    }

    private IEnumerable<string> CategoryNames(XdfItem item)
    {
        var names = item.CategoryIds.Select(id => _xdf.Categories.TryGetValue(id, out var name) ? name : null).OfType<string>().Distinct().ToList();
        return names.Count > 0 ? names : new[] { OtherCategory };
    }

    [GeneratedRegex(@"^\s*(\d+)\s*:\s*(\S.*)$", RegexOptions.Multiline)]
    private static partial Regex ChoiceLine();
}
