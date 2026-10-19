using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <summary>Tab-separated text for copying cells to and from TunerPro and spreadsheets.</summary>
public static class TableClipboard
{
    private static readonly char[] CellSeparators = ['\t', ';', ' '];

    /// <summary>
    /// The values of <paramref name="range"/>, one line per row, at full precision so that pasting them back gives the
    /// same raw values (rounded display digits would land on a neighbouring raw step).
    /// </summary>
    public static string Format(TableValues values, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(values);
        var lines = new List<string>();
        for (var row = range.Row0; row <= range.Row1; row++)
        {
            var cells = new List<string>();
            for (var col = range.Col0; col <= range.Col1; col++)
            {
                var value = values.Physical[row, col];
                cells.Add(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "—");
            }

            lines.Add(string.Join('\t', cells));
        }

        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Reads rows of numbers separated by tabs, semicolons or spaces. A comma is always a decimal
    /// separator, so French spreadsheet text such as "12,5" reads as 12.5.
    /// </summary>
    public static double[,] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.Split(CellSeparators, StringSplitOptions.RemoveEmptyEntries).Select(ParseNumber).ToArray())
            .ToList();

        if (rows.Count == 0)
        {
            throw new FormatException("The clipboard holds no numbers.");
        }

        var cols = rows[0].Length;
        if (rows.Any(row => row.Length != cols))
        {
            throw new FormatException("The rows on the clipboard have different lengths.");
        }

        var result = new double[rows.Count, cols];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                result[r, c] = rows[r][c];
            }
        }

        return result;
    }

    private static double ParseNumber(string token)
    {
        var normalized = token.Replace('−', '-').Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{token}' is not a number.");
    }
}
