using System.Collections.Generic;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>The facts line of an item's hover card, shared by the Tune rail and the Compare list.</summary>
public static class ItemFacts
{
    /// <summary>"Map, 20 × 16, °CRK, at 0x77315": what the item is, how big, in what, and where (the hover card's last line).</summary>
    public static string Line(XdfItem item, ItemKind kind)
    {
        var parts = new List<string> { kind.ToString() };
        if (item is XdfTable table)
        {
            parts.Add(table.Z.Data.Cols > 1 && table.Z.Data.Rows > 1 ? $"{table.Z.Data.Rows} × {table.Z.Data.Cols}" : $"{table.Z.Data.Count} points");
        }

        var units = item switch
        {
            XdfTable table2 => table2.Z.Units,
            XdfConstant constant => constant.Units,
            _ => string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(units) && units != "-")
        {
            parts.Add(units);
        }

        if (XdfCatalog.Address(item) is long address)
        {
            parts.Add($"at 0x{address:X}");
        }

        return string.Join(", ", parts);
    }
}
