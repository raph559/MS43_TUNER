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

/// <summary>How the rail orders items: by title, by address in the bin, by kind (maps first), or by element count.</summary>
public enum ItemSort
{
    Name,
    Address,
    Kind,
    Size,
}

/// <summary>Browsing helpers over an XDF: item kinds, switch choices, categories and search.</summary>
public sealed partial class XdfCatalog
{
    public const string OtherCategory = "Other";

    private readonly XdfDefinition _xdf;
    private readonly List<XdfItem> _all;
    private readonly Dictionary<XdfConstant, IReadOnlyList<(long Value, string Label)>?> _choices = new();
    private readonly Dictionary<XdfItem, string> _haystacks = new();

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
    /// <summary>
    /// The choices as a bin reads them: with the current overlay (<paramref name="currentOverlay"/>), the activation bytes of
    /// launch control, no-lift shift and rolling anti-lag also take triggers, listed by name; otherwise the description's lines.
    /// </summary>
    public IReadOnlyList<(long Value, string Label)>? Choices(XdfConstant constant, bool currentOverlay)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (currentOverlay && constant.Title switch
            {
                "c_conf_lc" => LiveTuning.OverlaySettings.LaunchControl, "c_conf_nls" => LiveTuning.OverlaySettings.NoLiftShift,
                "c_conf_ral" => LiveTuning.OverlaySettings.RollingAntiLag, _ => 0,
            } is var table and > 0)
            return LiveTuning.OverlayTriggers.For(table).Select(t => ((long)t.Value, t.Label)).ToList();
        return Choices(constant);
    }

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

    public IReadOnlyList<(string Category, int Count)> Categories() => Categories(ItemSort.Name, descending: false);

    /// <summary>Categories by name, or by how many items they hold for <see cref="ItemSort.Size"/>; address and kind keep them by name.</summary>
    public IReadOnlyList<(string Category, int Count)> Categories(ItemSort sort, bool descending)
    {
        var all = _all.SelectMany(CategoryNames).GroupBy(name => name).Select(group => (Category: group.Key, Count: group.Count()));
        var ordered = sort == ItemSort.Size
            ? Order(all, pair => pair.Count, descending).ThenBy(pair => pair.Category, StringComparer.OrdinalIgnoreCase)
            : descending
                ? all.OrderByDescending(pair => pair.Category, StringComparer.OrdinalIgnoreCase)
                : all.OrderBy(pair => pair.Category, StringComparer.OrdinalIgnoreCase);
        return ordered.Select(pair => (pair.Category, pair.Count)).ToList();
    }

    /// <summary>Orders items by <paramref name="sort"/>; ties, and items without an address, fall back to the title.</summary>
    public IReadOnlyList<XdfItem> Sort(IEnumerable<XdfItem> items, ItemSort sort, bool descending)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ordered = sort switch
        {
            ItemSort.Address => Order(items, item => Address(item) ?? (descending ? long.MinValue : long.MaxValue), descending),
            ItemSort.Kind => Order(items, item => (int)Kind(item), descending),
            ItemSort.Size => Order(items, Size, descending),
            _ => descending
                ? items.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
        };
        return (sort == ItemSort.Name ? ordered : ordered.ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Where the item's data starts in the bin (base offset included), or null when the XDF gives none.</summary>
    public static long? Address(XdfItem item) => item switch
    {
        XdfTable table => table.Z.Data.Address,
        XdfConstant constant => constant.Data.Address,
        XdfFlag flag => flag.Data.Address,
        _ => null,
    };

    /// <summary>How many values the item holds: rows × columns for a table, 1 for a value or flag.</summary>
    public static int Size(XdfItem item) => item is XdfTable table ? table.Z.Data.Count : 1;

    /// <summary>What a category holds, for its tooltip: "11 items: 6 maps, 3 curves, 2 values" (XDF categories have no description).</summary>
    public string Summary(string category)
    {
        var items = InCategory(category);
        var kinds = items.GroupBy(Kind).OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {group.Key.ToString().ToLowerInvariant()}{(group.Count() == 1 ? "" : "s")}");
        return $"{items.Count} item{(items.Count == 1 ? "" : "s")}: {string.Join(", ", kinds)}";
    }

    private static IOrderedEnumerable<T> Order<T, TKey>(IEnumerable<T> items, Func<T, TKey> key, bool descending) =>
        descending ? items.OrderByDescending(key) : items.OrderBy(key);

    public IReadOnlyList<XdfItem> InCategory(string category) =>
        _all.Where(item => CategoryNames(item).Contains(category)).OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<XdfTable> AxisUsers(XdfTable axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        return _xdf.Tables.Where(table => table.X.LinkedTableId == axis.UniqueId || table.Y.LinkedTableId == axis.UniqueId).ToList();
    }

    /// <summary>
    /// Every word of the query must appear somewhere in the item: its title, description, categories, units or address
    /// ("0x77315" or "77315"). Case-insensitive. Best matches first: the title starting with the query, then the title
    /// containing it, then every word in the title, then the rest.
    /// </summary>
    public IReadOnlyList<XdfItem> Search(string text, int max = 200)
    {
        var query = (text ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return Array.Empty<XdfItem>();
        }

        var words = Words(query);
        bool HasAll(string where) => words.All(word => where.Contains(word, StringComparison.OrdinalIgnoreCase));

        int Rank(XdfItem item) =>
            item.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 :
            item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 :
            HasAll(item.Title) ? 2 :
            HasAll(Haystack(item)) ? 3 : 4;

        return _all.Select(item => (Item: item, Rank: Rank(item)))
            .Where(pair => pair.Rank < 4)
            .OrderBy(pair => pair.Rank)
            .ThenBy(pair => pair.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(pair => pair.Item)
            .ToList();
    }

    /// <summary>True when every word of <paramref name="query"/> is somewhere in the item, as <see cref="Search"/> finds it; an empty query matches all.</summary>
    public bool Matches(XdfItem item, string query)
    {
        ArgumentNullException.ThrowIfNull(item);
        var text = Haystack(item);
        return Words(query).All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The words of a search, split on white space.</summary>
    public static string[] Words(string? query) => (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // Everything a search can find an item by, joined; built once per item.
    private string Haystack(XdfItem item)
    {
        if (!_haystacks.TryGetValue(item, out var text))
        {
            var units = item switch
            {
                XdfTable table => new[] { table.Z.Units, table.X.Units, table.Y.Units },
                XdfConstant constant => new[] { constant.Units },
                _ => Array.Empty<string>(),
            };
            var address = Address(item) is long at ? $"0x{at:X} {at:X}" : string.Empty;
            text = string.Join(" ", new[] { item.Title, item.Description, address }.Concat(units).Concat(CategoryNames(item)));
            _haystacks[item] = text;
        }

        return text;
    }

    /// <summary>The item's categories, or <see cref="OtherCategory"/> when the XDF gives it none.</summary>
    public IReadOnlyList<string> CategoriesOf(XdfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return CategoryNames(item).ToList();
    }

    private IEnumerable<string> CategoryNames(XdfItem item)
    {
        var names = item.CategoryIds.Select(id => _xdf.Categories.TryGetValue(id, out var name) ? name : null).OfType<string>().Distinct().ToList();
        return names.Count > 0 ? names : new[] { OtherCategory };
    }

    [GeneratedRegex(@"^\s*(\d+)\s*:\s*(\S.*)$", RegexOptions.Multiline)]
    private static partial Regex ChoiceLine();
}
