namespace MS43Logger.Core.Tuning;

/// <summary>Where an input sits on an axis, as the ECU's search leaves it.</summary>
/// <param name="Clamped">−1 when the input is below the first breakpoint, +1 above the last, else 0.</param>
public readonly record struct AxisPosition(int Index, double Fraction, int Clamped)
{
    public double Position => Index + Fraction;
}

/// <summary>
/// The axis search of the MS43 lookup library (0D:0060 / 0D:00B6 interpolating, 0D:0462 / 0D:039A not):
/// unsigned compares, clamp to the first or last breakpoint with fraction 0, otherwise the highest breakpoint
/// at or below the input and a 16-bit fraction to the next one.
/// </summary>
public static class AxisSearch
{
    public static AxisPosition Find(IReadOnlyList<long> breakpoints, long input, bool interpolate)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        if (breakpoints.Count == 0)
        {
            throw new ArgumentException("An axis needs at least one breakpoint.", nameof(breakpoints));
        }

        var last = breakpoints.Count - 1;
        if (input >= breakpoints[last])
        {
            return new AxisPosition(last, 0, input > breakpoints[last] ? 1 : 0);
        }

        if (input <= breakpoints[0])
        {
            return new AxisPosition(0, 0, input < breakpoints[0] ? -1 : 0);
        }

        var index = last - 1;
        while (input < breakpoints[index])
        {
            index--;
        }

        if (!interpolate || input == breakpoints[index])
        {
            return new AxisPosition(index, 0, 0);
        }

        var fraction = ((input - breakpoints[index]) << 16) / (breakpoints[index + 1] - breakpoints[index]);
        return new AxisPosition(index, fraction / 65536.0, 0);
    }
}

public readonly record struct CellWeight(int Row, int Col, double Weight);

public static class CellWeights
{
    /// <summary>The cells a bilinear lookup reads, row-major, leaving out cells with no weight or outside the table. A null axis is index 0.</summary>
    public static IReadOnlyList<CellWeight> Around(AxisPosition? row, AxisPosition? col, int rows, int cols)
    {
        var r = row ?? default;
        var c = col ?? default;
        var cells = new List<CellWeight>(4);
        for (var dr = 0; dr <= 1; dr++)
        {
            for (var dc = 0; dc <= 1; dc++)
            {
                var weight = (dr == 0 ? 1 - r.Fraction : r.Fraction) * (dc == 0 ? 1 - c.Fraction : c.Fraction);
                int cellRow = r.Index + dr, cellCol = c.Index + dc;
                if (weight > 0 && cellRow < rows && cellCol < cols)
                {
                    cells.Add(new CellWeight(cellRow, cellCol, weight));
                }
            }
        }

        return cells;
    }

    public static double Interpolate(double[,] values, IReadOnlyList<CellWeight> cells)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(cells);
        var sum = 0.0;
        foreach (var cell in cells)
        {
            sum += values[cell.Row, cell.Col] * cell.Weight;
        }

        return sum;
    }
}

public readonly record struct TrailPoint(double Row, double Col, double Opacity);

/// <summary>The last few cursor positions, fading out over <c>life</c>.</summary>
public sealed class CursorTrail
{
    private readonly int _capacity;
    private readonly TimeSpan _life;
    private readonly Queue<(double Row, double Col, TimeSpan Time)> _points = new();

    public CursorTrail(int capacity = 32, TimeSpan? life = null)
    {
        _capacity = Math.Max(1, capacity);
        _life = life ?? TimeSpan.FromSeconds(1.2);
    }

    public void Add(double row, double col, TimeSpan time)
    {
        _points.Enqueue((row, col, time));
        while (_points.Count > _capacity)
        {
            _points.Dequeue();
        }
    }

    public IReadOnlyList<TrailPoint> Points(TimeSpan now)
    {
        while (_points.Count > 0 && now - _points.Peek().Time >= _life)
        {
            _points.Dequeue();
        }

        return _points.Select(p => new TrailPoint(p.Row, p.Col, 1 - (now - p.Time) / _life)).ToList();
    }

    public void Clear() => _points.Clear();
}
