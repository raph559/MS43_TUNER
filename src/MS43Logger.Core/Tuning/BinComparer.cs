using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <summary>A table axis whose breakpoints differ between A and B.</summary>
/// <param name="Direction">"row" or "column".</param>
public sealed record AxisDifference(string Direction, AxisValues A, AxisValues B)
{
    public string Describe()
    {
        var name = string.Join(", ", new[] { A.Name, A.Units }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return $"The {Direction} axis differs{(name.Length > 0 ? $" ({name})" : string.Empty)}: " +
               $"A runs {Ends(A)}, B {Ends(B)}, so cells are compared position by position.";
    }

    private static string Ends(AxisValues axis) =>
        axis.Labels.Count == 0 ? "nothing" : $"{axis.Labels[0]} to {axis.Labels[^1]}";
}

/// <summary>One table in A and B; <see cref="Delta"/> holds B − A in raw and physical units on A's axes.</summary>
public sealed record TableComparison(
    TableValues A,
    TableValues B,
    TableValues Delta,
    IReadOnlySet<(int Row, int Col)> Changed,
    IReadOnlyList<AxisDifference> Axes,
    double? LargestChange);

/// <summary>An XDF item whose bytes differ. <see cref="LargestChange"/> is the signed B − A with the largest magnitude.</summary>
public sealed record ItemDifference(
    XdfItem Item,
    ItemKind Kind,
    string Category,
    int ChangedCells,
    int Cells,
    double? LargestChange,
    string? ValueA,
    string? ValueB,
    IReadOnlyList<AxisDifference> Axes);

/// <summary>
/// Bytes that differ outside every XDF item, within one flash area. Runs separated by at most
/// <see cref="BinComparer.MergeGap"/> equal bytes form one range: <see cref="Length"/> spans them,
/// <see cref="Differing"/> counts the bytes that actually differ.
/// </summary>
public sealed record UnmappedRange(int Offset, int Length, int Differing, string Area)
{
    /// <summary>The last offset of the range (inclusive).</summary>
    public int End => Offset + Length - 1;
}

public sealed record BinComparison(IReadOnlyList<ItemDifference> Items, IReadOnlyList<UnmappedRange> Unmapped)
{
    /// <summary>Bytes the unmapped ranges span.</summary>
    public int UnmappedBytes => Unmapped.Sum(range => range.Length);

    /// <summary>Bytes inside the unmapped ranges that actually differ.</summary>
    public int DifferingBytes => Unmapped.Sum(range => range.Differing);

    public bool IsEmpty => Items.Count == 0 && Unmapped.Count == 0;
}

/// <summary>Compares two bins item by item through one XDF, and lists the bytes no item covers.</summary>
public static class BinComparer
{
    // File offsets where each flash area starts (MS43LAB REPORT.md §2).
    private static readonly (int Start, string Name)[] Areas =
    [
        (0x00000, "Boot block"),
        (0x04000, "Unused"),
        (0x08000, "Service sector"),
        (0x0C000, "Unused"),
        (0x10000, "Program"),
        (0x6C000, "Program constants"),
        (0x70000, "Calibration"),
    ];

    /// <summary>Unmapped runs this many equal bytes apart or closer are listed as one range.</summary>
    public const int MergeGap = 8;

    public static string AreaName(int offset) => Areas.Last(area => area.Start <= offset).Name;

    public static BinComparison Compare(XdfDefinition xdf, BinImage a, BinImage b)
    {
        ArgumentNullException.ThrowIfNull(xdf);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var catalog = new XdfCatalog(xdf);
        var readerA = new CalibrationReader(xdf, a);
        var readerB = new CalibrationReader(xdf, b);
        var items = new List<ItemDifference>();

        foreach (var table in xdf.Tables)
        {
            if (Readable(a, table.Z.Data) && !SameBytes(a, b, table.Z.Data))
            {
                var compared = CompareTable(readerA, readerB, table);
                items.Add(new ItemDifference(table, catalog.Kind(table), Category(xdf, table), compared.Changed.Count, table.Z.Data.Count,
                    compared.LargestChange, null, null, compared.Axes));
            }
        }

        foreach (var constant in xdf.Constants)
        {
            if (Readable(a, constant.Data) && !SameBytes(a, b, constant.Data))
            {
                var valueA = readerA.ReadConstant(constant);
                var valueB = readerB.ReadConstant(constant);
                var delta = valueB.Physical - valueA.Physical;
                items.Add(new ItemDifference(constant, catalog.Kind(constant), Category(xdf, constant), 1, 1,
                    double.IsFinite(delta) ? delta : null, Show(catalog, constant, valueA), Show(catalog, constant, valueB), Array.Empty<AxisDifference>()));
            }
        }

        foreach (var flag in xdf.Flags)
        {
            if (Readable(a, flag.Data) && readerA.ReadFlag(flag) is var onA && readerB.ReadFlag(flag) is var onB && onA != onB)
            {
                items.Add(new ItemDifference(flag, ItemKind.Flag, Category(xdf, flag), 1, 1, null, onA ? "on" : "off", onB ? "on" : "off", Array.Empty<AxisDifference>()));
            }
        }

        var ordered = items
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new BinComparison(ordered, Unmapped(xdf, a, b));
    }

    public static TableComparison CompareTable(XdfDefinition xdf, BinImage a, BinImage b, XdfTable table) =>
        CompareTable(new CalibrationReader(xdf, a), new CalibrationReader(xdf, b), table);

    private static TableComparison CompareTable(CalibrationReader readerA, CalibrationReader readerB, XdfTable table)
    {
        var a = readerA.ReadTable(table);
        var b = readerB.ReadTable(table);
        var raw = new long[a.Rows, a.Cols];
        var physical = new double[a.Rows, a.Cols];
        var changed = new HashSet<(int Row, int Col)>();
        double? largest = null;
        for (var r = 0; r < a.Rows; r++)
        {
            for (var c = 0; c < a.Cols; c++)
            {
                raw[r, c] = b.Raw[r, c] - a.Raw[r, c];
                physical[r, c] = b.Physical[r, c] - a.Physical[r, c];
                if (raw[r, c] != 0)
                {
                    changed.Add((r, c));
                    if (double.IsFinite(physical[r, c]) && (largest is not double l || Math.Abs(physical[r, c]) > Math.Abs(l)))
                    {
                        largest = physical[r, c];
                    }
                }
            }
        }

        var axes = new List<AxisDifference>();
        if (!SameValues(a.Y.Values, b.Y.Values))
        {
            axes.Add(new AxisDifference("row", a.Y, b.Y));
        }

        if (!SameValues(a.X.Values, b.X.Values))
        {
            axes.Add(new AxisDifference("column", a.X, b.X));
        }

        return new TableComparison(a, b, a with { Raw = raw, Physical = physical }, changed, axes, largest);
    }

    /// <summary>Signed text for one B − A value: "+1.50", "−0.38" (U+2212), "·" for no change. Hex and ASCII items show raw counts.</summary>
    public static string FormatDelta(double delta, long rawDelta, XdfZ z, bool raw)
    {
        ArgumentNullException.ThrowIfNull(z);
        if (raw || z.OutputType is 3 or 4)
        {
            return rawDelta == 0 ? "·" : (rawDelta > 0 ? "+" : "−") + Math.Abs(rawDelta).ToString(CultureInfo.InvariantCulture);
        }

        if (double.IsNaN(delta))
        {
            return "—";
        }

        if (delta == 0)
        {
            return "·";
        }

        return (delta > 0 ? "+" : "−") + ValueCodec.Format(Math.Abs(delta), 0, z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
    }

    private static bool Readable(BinImage image, XdfData data) =>
        data.Address is not null && data.ElementBits is 8 or 16 or 32 && image.Contains(data);

    private static bool SameBytes(BinImage a, BinImage b, XdfData data)
    {
        var start = BinImage.ToOffset(data.Address!.Value);
        var length = data.Count * data.ElementBytes;
        return a.Bytes.Slice(start, length).SequenceEqual(b.Bytes.Slice(start, length));
    }

    private static bool SameValues(IReadOnlyList<double> a, IReadOnlyList<double> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.Equals(pair.Second));

    private static string Category(XdfDefinition xdf, XdfItem item) =>
        item.CategoryIds.Select(id => xdf.Categories.TryGetValue(id, out var name) ? name : null).OfType<string>().FirstOrDefault()
        ?? XdfCatalog.OtherCategory;

    private static string Show(XdfCatalog catalog, XdfConstant constant, ConstantValue value)
    {
        if (catalog.Choices(constant) is { } choices && choices.FirstOrDefault(choice => choice.Value == value.Raw) is { Label: { } label })
        {
            return label;
        }

        return double.IsNaN(value.Physical) ? "raw " + value.Raw.ToString(CultureInfo.InvariantCulture) : value.Display;
    }

    private static List<UnmappedRange> Unmapped(XdfDefinition xdf, BinImage a, BinImage b)
    {
        var differences = a.DiffRanges(b);
        var result = new List<UnmappedRange>();
        if (differences.Count == 0)
        {
            return result;
        }

        var covered = new bool[BinImage.Size];
        void Cover(XdfData? data, int count)
        {
            if (data?.Address is not long address || data.ElementBits is not (8 or 16 or 32))
            {
                return;
            }

            var start = BinImage.ToOffset(address);
            var end = Math.Min(BinImage.Size, start + (long)count * data.ElementBytes);
            for (var i = start; i < end; i++)
            {
                covered[i] = true;
            }
        }

        foreach (var table in xdf.Tables)
        {
            Cover(table.Z.Data, table.Z.Data.Count);
            Cover(table.X.Data, table.X.Count);
            Cover(table.Y.Data, table.Y.Count);
        }

        foreach (var constant in xdf.Constants)
        {
            Cover(constant.Data, constant.Data.Count);
        }

        foreach (var flag in xdf.Flags)
        {
            Cover(flag.Data, flag.Data.Count);
        }

        foreach (var (offset, length) in differences)
        {
            for (var i = offset; i < offset + length; i++)
            {
                if (covered[i])
                {
                    continue;
                }

                var area = AreaName(i);
                if (result.Count > 0 && result[^1] is var last && last.Area == area && i - (last.Offset + last.Length) <= MergeGap &&
                    !covered.AsSpan(last.Offset + last.Length, i - (last.Offset + last.Length)).Contains(true))
                {
                    result[^1] = last with { Length = i - last.Offset + 1, Differing = last.Differing + 1 };
                }
                else
                {
                    result.Add(new UnmappedRange(i, 1, 1, area));
                }
            }
        }

        return result;
    }
}
