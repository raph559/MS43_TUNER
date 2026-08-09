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

/// <summary>
/// Which way Interpolate and Smooth work, as in TunerPro: X along each row (across the columns), Y along each column (across
/// the rows), XY both at once.
/// </summary>
public enum EditAxis
{
    XY,
    X,
    Y,
}

public sealed record CellChange(int Row, int Col, long OldRaw, long NewRaw);

/// <summary>Raw changes for one item, computed but not yet applied.</summary>
public sealed record EditPlan(XdfItem Item, IReadOnlyList<CellChange> Changes, IReadOnlyList<string> Warnings);

public sealed class EditRefusedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Edits in physical units, snapped to the raw values the ECU can store.</summary>
public static class TableEdits
{
    private static readonly ConditionalWeakTable<XdfDefinition, HashSet<int>> AxisIds = new();

    public static EditPlan Set(CalibrationDocument d, XdfTable t, CellRange r, double value) =>
        Map(d, t, r, (_, _, _) => value);

    public static EditPlan Add(CalibrationDocument d, XdfTable t, CellRange r, double delta) =>
        Map(d, t, r, (v, _, _) => v + delta);

    public static EditPlan Scale(CalibrationDocument d, XdfTable t, CellRange r, double percent) =>
        Map(d, t, r, (v, _, _) => v * (1 + percent / 100));

    public static EditPlan Interpolate(CalibrationDocument d, XdfTable t, CellRange r)
    {
        var values = Read(d, t);
        double P(int row, int col) => values.Physical[row, col];
        return Map(d, t, r, (_, row, col) =>
        {
            var u = r.Cols > 1 ? (double)(col - r.Col0) / (r.Cols - 1) : 0;
            var w = r.Rows > 1 ? (double)(row - r.Row0) / (r.Rows - 1) : 0;
            return (P(r.Row0, r.Col0) * (1 - u) + P(r.Row0, r.Col1) * u) * (1 - w) + (P(r.Row1, r.Col0) * (1 - u) + P(r.Row1, r.Col1) * u) * w;
        }, keepCorners: true);
    }

    public static EditPlan Smooth(CalibrationDocument d, XdfTable t, CellRange r)
    {
        var values = Read(d, t);
        return Map(d, t, r, (_, row, col) =>
        {
            double sum = 0;
            var count = 0;
            for (var i = row - 1; i <= row + 1; i++)
            {
                for (var j = col - 1; j <= col + 1; j++)
                {
                    if (i >= 0 && i < values.Rows && j >= 0 && j < values.Cols && double.IsFinite(values.Physical[i, j]))
                    {
                        sum += values.Physical[i, j];
                        count++;
                    }
                }
            }

            return sum / count;
        });
    }

    public static EditPlan Paste(CalibrationDocument d, XdfTable t, int row, int col, double[,] pasted)
    {
        ArgumentNullException.ThrowIfNull(pasted);
        var values = Read(d, t);
        if (row < 0 || col < 0 || row + pasted.GetLength(0) > values.Rows || col + pasted.GetLength(1) > values.Cols)
        {
            throw new EditRefusedException($"The pasted {pasted.GetLength(0)} × {pasted.GetLength(1)} block does not fit '{t.Title}' at row {row + 1}, column {col + 1}.");
        }

        var range = new CellRange(row, col, row + pasted.GetLength(0) - 1, col + pasted.GetLength(1) - 1);
        return Map(d, t, range, (_, r, c) => pasted[r - row, c - col]);
    }

    public static EditPlan Step(CalibrationDocument d, XdfTable t, CellRange r, int rawSteps)
    {
        var values = Read(d, t);
        var (min, max) = RawRange(t.Z.Data);
        var changes = new List<CellChange>();
        var codec = d.Reader.Codec;

        // A step moves the value as it is shown: + raises it. Where the equation falls as the raw count rises (the AFR
        // target's (1 - (k X - 0.5)) Y), that is a step down in raw.
        bool Rises(long raw) => raw < max
            ? codec.ToPhysical(t.Z.Math, raw + 1) >= codec.ToPhysical(t.Z.Math, raw)
            : codec.ToPhysical(t.Z.Math, raw - 1) <= codec.ToPhysical(t.Z.Math, raw);

        ForEach(values, r, (row, col) =>
        {
            var old = values.Raw[row, col];
            var raw = Math.Clamp(old + (Rises(old) ? rawSteps : -rawSteps), min, max);
            if (raw != old)
            {
                changes.Add(new CellChange(row, col, old, raw));
            }
        });

        return Finish(d, t, values, changes);
    }

    public static EditPlan SetConstant(CalibrationDocument d, XdfConstant c, double value)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(c);
        EnsureEditable(c.Title, c.Data);
        var old = d.Reader.ReadConstant(c).Raw;
        var raw = ToRaw(d, c.Title, c.Math, c.Data, value);
        var warnings = new List<string>();
        Check(d, c.Title, c.Math, c.Data, value, raw, c.RangeLow, c.RangeHigh, warnings);
        return new EditPlan(c, raw == old ? Array.Empty<CellChange>() : new[] { new CellChange(0, 0, old, raw) }, warnings);
    }

    /// <summary>Stores a raw value as it is (switch choices are raw values).</summary>
    public static EditPlan SetConstantRaw(CalibrationDocument d, XdfConstant c, long raw)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(c);
        EnsureEditable(c.Title, c.Data);
        var (min, max) = RawRange(c.Data);
        if (raw < min || raw > max)
        {
            throw new EditRefusedException($"'{c.Title}' cannot hold the raw value {raw} ({min}…{max}).");
        }

        var old = d.Image.ReadElement(c.Data, 0);
        return new EditPlan(c, raw == old ? Array.Empty<CellChange>() : new[] { new CellChange(0, 0, old, raw) }, Array.Empty<string>());
    }

    public static EditPlan SetFlag(CalibrationDocument d, XdfFlag f, bool on)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(f);
        EnsureEditable(f.Title, f.Data);
        var old = d.Image.ReadElement(f.Data, 0);
        var raw = on ? old | f.Mask : old & ~f.Mask;
        return new EditPlan(f, raw == old ? Array.Empty<CellChange>() : new[] { new CellChange(0, 0, old, raw) }, Array.Empty<string>());
    }

    /// <summary>
    /// Writes <paramref name="source"/>'s raw values for <paramref name="item"/> into the document: every differing cell of a
    /// table (an axis copy that would make the axis go down is refused), a value, or a flag's own bit.
    /// </summary>
    /// <summary>Writes <paramref name="source"/>'s raw values into the cells of <paramref name="range"/> only (Tune's Copy from B).</summary>
    public static EditPlan CopyFrom(CalibrationDocument d, XdfTable table, CellRange range, BinImage source)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(source);
        var values = Read(d, table);
        var changes = new List<CellChange>();
        ForEach(values, range, (row, col) =>
        {
            var raw = source.ReadElement(table.Z.Data, row * values.Cols + col);
            if (raw != values.Raw[row, col])
            {
                changes.Add(new CellChange(row, col, values.Raw[row, col], raw));
            }
        });
        var plan = Finish(d, table, values, changes);
        if (changes.Count == 0)
        {
            return plan;
        }

        // Cells copied onto other breakpoints mean something else: say so.
        var moved = BinComparer.CompareTable(d.Xdf, d.Image, source, table).Axes
            .Select(axis => $"'{table.Title}': its {axis.Direction} axis{(axis.A.Name is { } name ? $" ({name})" : string.Empty)} still differs from the source, so the copied cells now sit on A's breakpoints.");
        return plan with { Warnings = plan.Warnings.Concat(moved).ToList() };
    }

    public static EditPlan CopyFrom(CalibrationDocument d, XdfItem item, BinImage source)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        switch (item)
        {
            case XdfTable table:
                var size = Read(d, table);
                return CopyFrom(d, table, new CellRange(0, 0, size.Rows - 1, size.Cols - 1), source);
            case XdfConstant constant:
                EnsureEditable(constant.Title, constant.Data);
                return SetConstantRaw(d, constant, source.ReadElement(constant.Data, 0));
            case XdfFlag flag:
                EnsureEditable(flag.Title, flag.Data);
                return SetFlag(d, flag, (source.ReadElement(flag.Data, 0) & flag.Mask) != 0);
            default:
                throw new ArgumentException($"Unknown item type {item.GetType().Name}.", nameof(item));
        }
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

    private static EditPlan Map(CalibrationDocument d, XdfTable t, CellRange r, Func<double, int, int, double> target, bool keepCorners = false)
    {
        var values = Read(d, t);
        if (r.Row0 < 0 || r.Col0 < 0 || r.Row1 >= values.Rows || r.Col1 >= values.Cols)
        {
            throw new EditRefusedException($"The selection lies outside '{t.Title}'.");
        }

        var changes = new List<CellChange>();
        var limits = new List<string>();
        ForEach(values, r, (row, col) =>
        {
            if (keepCorners && (row == r.Row0 || row == r.Row1) && (col == r.Col0 || col == r.Col1))
            {
                return;
            }

            var old = values.Raw[row, col];
            var wanted = target(values.Physical[row, col], row, col);
            var raw = ToRaw(d, t.Title, t.Z.Math, t.Z.Data, wanted);
            Check(d, t.Title, t.Z.Math, t.Z.Data, wanted, raw, t.Z.Min, t.Z.Max, limits);
            if (raw != old)
            {
                changes.Add(new CellChange(row, col, old, raw));
            }
        });

        return Finish(d, t, values, changes, limits);
    }

    /// <summary>Warns when a value was limited to what the element can hold, or lies outside the XDF's range (a soft limit).</summary>
    private static void Check(CalibrationDocument d, string title, XdfMath math, XdfData data, double wanted, long raw, double? low, double? high, List<string> warnings)
    {
        var codec = d.Reader.Codec;
        var stored = codec.ToPhysical(math, raw);
        var (min, max) = RawRange(data);
        if (double.IsFinite(stored) && (raw == min || raw == max))
        {
            var neighbour = codec.ToPhysical(math, raw == max ? raw - 1 : raw + 1);
            var halfStep = double.IsFinite(neighbour) ? Math.Abs(stored - neighbour) / 2 : 0;
            if (Math.Abs(wanted - stored) > halfStep + 1e-9)
            {
                warnings.Add($"'{title}' cannot hold {Show(wanted)}; it was set to its limit, {Show(stored)}.");
            }
        }

        if ((low is double lo && wanted < lo) || (high is double hi && wanted > hi))
        {
            warnings.Add($"'{title}': {Show(wanted)} is outside the XDF's range {Show(low)} to {Show(high)}.");
        }
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

    private static long ToRaw(CalibrationDocument d, string title, XdfMath math, XdfData data, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new EditRefusedException($"'{title}': the new value is not a number.");
        }

        try
        {
            return d.Reader.Codec.ToRaw(math, data, value);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            throw new EditRefusedException($"'{title}' cannot store {value}: {error.Message}", error);
        }
    }

    private static (long Min, long Max) RawRange(XdfData data) =>
        data.Signed ? (-(1L << (data.ElementBits - 1)), (1L << (data.ElementBits - 1)) - 1) : (0, (1L << data.ElementBits) - 1);

    private static void ForEach(TableValues values, CellRange r, Action<int, int> action)
    {
        for (var row = Math.Max(0, r.Row0); row <= Math.Min(values.Rows - 1, r.Row1); row++)
        {
            for (var col = Math.Max(0, r.Col0); col <= Math.Min(values.Cols - 1, r.Col1); col++)
            {
                action(row, col);
            }
        }
    }
}
