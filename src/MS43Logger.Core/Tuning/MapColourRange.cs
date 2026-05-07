namespace MS43Logger.Core.Tuning;

/// <summary>
/// The value range a map's colours span, and the cells left out of it. A cell at the storable minimum or maximum is
/// hatched instead of coloured only when it is an outlier there: fewer than half of the cells sit at that limit, and
/// its value lies outside the other cells' range by at least a quarter of that range. Ordinary values that happen to
/// sit at a raw limit, such as a zero next to small values, stay normal cells.
/// </summary>
public sealed record MapColourRange(double Min, double Max, IReadOnlySet<(int Row, int Col)> Hatched)
{
    public bool IsConstant => Min == Max;

    public static MapColourRange Compute(TableValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var (rawMin, rawMax) = ValueCodec.RawRange(values.Table.Z.Data);
        return Compute(values.Physical, values.Raw, rawMin, rawMax);
    }

    public static MapColourRange Compute(double[,] physical, long[,] raw, long rawMin, long rawMax)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(raw);
        var all = physical.Length;
        var cells = new List<(int Row, int Col)>();
        for (var row = 0; row < physical.GetLength(0); row++)
        {
            for (var col = 0; col < physical.GetLength(1); col++)
            {
                if (double.IsFinite(physical[row, col]))
                {
                    cells.Add((row, col));
                }
            }
        }

        var hatched = new HashSet<(int Row, int Col)>();
        foreach (var limit in new[] { rawMin, rawMax }.Distinct())
        {
            var group = cells.Where(cell => raw[cell.Row, cell.Col] == limit).ToList();
            var others = cells.Where(cell => raw[cell.Row, cell.Col] != limit).Select(cell => physical[cell.Row, cell.Col]).ToList();
            if (group.Count == 0 || others.Count == 0 || group.Count * 2 >= all)
            {
                continue;
            }

            var value = physical[group[0].Row, group[0].Col];
            var low = others.Min();
            var high = others.Max();
            var distance = value < low ? low - value : value > high ? value - high : 0;
            if (distance > 0 && distance >= (high - low) / 4)
            {
                hatched.UnionWith(group);
            }
        }

        var working = cells.Where(cell => !hatched.Contains(cell)).Select(cell => physical[cell.Row, cell.Col]).ToList();
        return working.Count == 0
            ? new MapColourRange(double.NaN, double.NaN, hatched)
            : new MapColourRange(working.Min(), working.Max(), hatched);
    }
}
