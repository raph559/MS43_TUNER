using System.Runtime.CompilerServices;

namespace MS43Logger.Core.Tuning;

/// <summary>A rectangle of cells; corners may be given in any order.</summary>
public readonly record struct CellRange
{
    public CellRange(int row0, int col0, int row1, int col1)
    {
        Row0 = Math.Min(row0, row1);
        Row1 = Math.Max(row0, row1);
        Col0 = Math.Min(col0, col1);
        Col1 = Math.Max(col0, col1);
    }

    public int Row0 { get; }

    public int Col0 { get; }

    public int Row1 { get; }

    public int Col1 { get; }

    public int Rows => Row1 - Row0 + 1;

    public int Cols => Col1 - Col0 + 1;

    public bool Contains(int row, int col) => row >= Row0 && row <= Row1 && col >= Col0 && col <= Col1;
}

public sealed record CellChange(int Row, int Col, long OldRaw, long NewRaw);

/// <summary>Raw changes for one item, computed but not yet applied.</summary>
public sealed record EditPlan(XdfItem Item, IReadOnlyList<CellChange> Changes, IReadOnlyList<string> Warnings);

public sealed class EditRefusedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Edits in physical units, snapped to the raw values the ECU can store.</summary>
public static class TableEdits
{
    private static readonly ConditionalWeakTable<XdfDefinition, HashSet<int>> AxisIds = new();

    public static EditPlan SetFlag(CalibrationDocument d, XdfFlag f, bool on)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(f);
        EnsureEditable(f.Title, f.Data);
        var old = d.Image.ReadElement(f.Data, 0);
        var raw = on ? old | f.Mask : old & ~f.Mask;
        return new EditPlan(f, raw == old ? Array.Empty<CellChange>() : new[] { new CellChange(0, 0, old, raw) }, Array.Empty<string>());
    }

    /// <summary>True for tables in the "Axis" category or linked as an axis by another table.</summary>
    public static bool IsAxis(XdfDefinition xdf, XdfTable table)
    {
        var ids = AxisIds.GetValue(xdf, definition => definition.Tables
            .SelectMany(t => new[] { t.X.LinkedTableId, t.Y.LinkedTableId })
            .OfType<int>()
            .ToHashSet());
        return ids.Contains(table.UniqueId) ||
               table.CategoryIds.Any(id => xdf.Categories.TryGetValue(id, out var name) && name == "Axis");
    }

    private static string Show(double? value) =>
        value is double v ? v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "…";

    private static EditPlan Finish(CalibrationDocument d, XdfTable t, TableValues values, List<CellChange> changes, List<string>? limits = null)
    {
        var warnings = new List<string>(limits ?? new List<string>());
        if (changes.Count > 0 && IsAxis(d.Xdf, t))
        {
            var raw = values.Raw.Cast<long>().ToArray();
            foreach (var change in changes)
            {
                raw[change.Row * values.Cols + change.Col] = change.NewRaw;
            }

            foreach (var index in changes.Select(change => change.Row * values.Cols + change.Col).Distinct())
            {
                foreach (var (a, b) in new[] { (index - 1, index), (index, index + 1) })
                {
                    if (a < 0 || b >= raw.Length)
                    {
                        continue;
                    }

                    if (raw[b] < raw[a])
                    {
                        throw new EditRefusedException($"'{t.Title}' is an axis: breakpoint {b + 1} would fall below breakpoint {a + 1}. Axis values must not go down.");
                    }

                    if (raw[b] == raw[a])
                    {
                        warnings.Add($"'{t.Title}': breakpoints {a + 1} and {b + 1} become equal.");
                    }
                }
            }
        }

        return new EditPlan(t, changes, warnings.Distinct().ToList());
    }

    private static TableValues Read(CalibrationDocument d, XdfTable t)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(t);
        EnsureEditable(t.Title, t.Z.Data);
        return d.Reader.ReadTable(t);
    }

    private static void EnsureEditable(string title, XdfData data)
    {
        if (!data.IsSupported)
        {
            throw new EditRefusedException($"'{title}' is read-only: {data.UnsupportedReason}.");
        }
    }

    private static (long Min, long Max) RawRange(XdfData data) =>
        data.Signed ? (-(1L << (data.ElementBits - 1)), (1L << (data.ElementBits - 1)) - 1) : (0, (1L << data.ElementBits) - 1);
}
