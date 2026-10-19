using System.Globalization;

namespace MS43Logger.Core;

public sealed record RateStats(double SamplesPerSecond, double AverageMs, double MinMs, double MaxMs, int Intervals)
{
    public static RateStats? FromIntervals(IReadOnlyCollection<double> intervalsMs)
    {
        if (intervalsMs.Count == 0)
        {
            return null;
        }

        var average = intervalsMs.Average();
        var rate = average > 0 ? 1000.0 / average : 0;
        return new RateStats(rate, average, intervalsMs.Min(), intervalsMs.Max(), intervalsMs.Count);
    }
}

/// <summary>Rolling statistics over the intervals between received samples.</summary>
public sealed class SampleRateMeter
{
    private readonly int _windowSize;
    private readonly Queue<double> _intervalsMs = new();
    private TimeSpan? _last;

    public SampleRateMeter(int windowSize = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);
        _windowSize = windowSize;
    }

    public RateStats? Current => RateStats.FromIntervals(_intervalsMs);

    public void Record(TimeSpan timestamp)
    {
        if (_last is TimeSpan last)
        {
            _intervalsMs.Enqueue((timestamp - last).TotalMilliseconds);
            while (_intervalsMs.Count > _windowSize)
            {
                _intervalsMs.Dequeue();
            }
        }

        _last = timestamp;
    }

    public void Reset()
    {
        _intervalsMs.Clear();
        _last = null;
    }
}

/// <summary>Logging rate of an existing CSV log, from its <c>timestamp_utc</c> column.</summary>
public static class LogRateAnalyzer
{
    public static RateStats? FromCsv(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = reader.ReadLine();
        if (header is null)
        {
            return null;
        }

        var column = SplitCsvLine(header).FindIndex(name => name.Equals("timestamp_utc", StringComparison.OrdinalIgnoreCase));
        if (column < 0)
        {
            return null;
        }

        var intervals = new List<double>();
        DateTimeOffset? previous = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = SplitCsvLine(line);
            if (column >= fields.Count ||
                !DateTimeOffset.TryParse(fields[column], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                continue;
            }

            if (previous is DateTimeOffset last)
            {
                intervals.Add((timestamp - last).TotalMilliseconds);
            }

            previous = timestamp;
        }

        return RateStats.FromIntervals(intervals);
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
