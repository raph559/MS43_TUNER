using System.Globalization;
using System.Text;

namespace MS43Logger.Core.Tuning;

/// <summary>Plain-text rendering of a bin comparison, for the CLI <c>diff</c> command.</summary>
public static class ComparisonText
{
    public static string Format(BinComparison comparison, string nameA, string nameB, string xdfName)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var text = new StringBuilder();
        text.Append("A: ").Append(nameA).Append('\n');
        text.Append("B: ").Append(nameB).Append('\n');
        text.Append("XDF: ").Append(xdfName).Append('\n');
        if (comparison.IsEmpty)
        {
            text.Append("No differences.\n");
            return text.ToString();
        }

        var items = comparison.Items.Count;
        var ranges = comparison.Unmapped.Count;
        var bytes = comparison.UnmappedBytes;
        text.Append($"{items} {(items == 1 ? "item differs" : "items differ")}, ")
            .Append($"{ranges} byte {(ranges == 1 ? "range" : "ranges")} ({Count(bytes)} {(bytes == 1 ? "byte" : "bytes")}) outside the XDF.\n");

        var width = Math.Min(44, comparison.Items.Select(item => item.Item.Title.Length).DefaultIfEmpty(0).Max());
        string? category = null;
        foreach (var item in comparison.Items)
        {
            if (item.Category != category)
            {
                category = item.Category;
                text.Append('\n').Append(category).Append('\n');
            }

            text.Append("  ").Append(item.Item.Title.PadRight(width)).Append("  ").Append(item.Kind.ToString().ToLowerInvariant().PadRight(6)).Append("  ");
            if (item.Item is XdfTable table)
            {
                text.Append($"{item.ChangedCells} of {item.Cells} {(item.Cells == 1 ? "cell" : "cells")}");
                if (item.LargestChange is double largest)
                {
                    text.Append("  largest A - B ").Append(BinComparer.FormatDelta(largest, 0, table.Z, raw: false));
                    if (table.Z.Units.Length > 0)
                    {
                        text.Append(' ').Append(table.Z.Units);
                    }
                }

                text.Append('\n');
                foreach (var axis in item.Axes)
                {
                    text.Append("      ").Append(axis.Direction).Append(" axis differs");
                    if (axis.A.Name is { } name)
                    {
                        text.Append(" (").Append(name).Append(')');
                    }

                    text.Append('\n');
                }
            }
            else
            {
                var units = item.Item is XdfConstant { Units.Length: > 0 } constant && item.Kind == ItemKind.Value ? " " + constant.Units : string.Empty;
                // "Was, now": B (the bin compared with) then A (the bin tuned), the way A - B reads.
                text.Append(item.ValueB).Append(" → ").Append(item.ValueA).Append(units).Append('\n');
            }
        }

        if (comparison.Unmapped.Count > 0)
        {
            text.Append("\nBytes outside the XDF\n");
            foreach (var range in comparison.Unmapped)
            {
                var size = $"{Count(range.Length)} {(range.Length == 1 ? "byte" : "bytes")}";
                var differing = range.Differing < range.Length ? $" ({Count(range.Differing)} differ)" : string.Empty;
                text.Append($"  0x{range.Offset:X5} to 0x{range.End:X5}  {size,12}  {range.Area}{differing}\n");
            }
        }

        return text.ToString();
    }

    // No digit grouping (system 3.3): "11907 bytes".
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
