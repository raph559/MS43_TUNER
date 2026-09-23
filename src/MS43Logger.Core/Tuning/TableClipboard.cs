using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <summary>Tab-separated text for copying cells to and from TunerPro and spreadsheets.</summary>
public static class TableClipboard
{
    private static readonly char[] CellSeparators = ['\t', ';', ' '];

    // Spaces inside a number on a tab-separated line: French thousands groups, often non-breaking.
    private static readonly char[] Spaces = [' ', ' ', ' '];

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
                cells.Add(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "n/a");
            }

            lines.Add(string.Join('\t', cells));
        }

        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Reads rows of numbers separated by tabs, semicolons or spaces; a line with a tab is split on tabs only, so a space
    /// inside a number ("1 234,5") is a thousands group. A single comma is a decimal separator, so French spreadsheet text
    /// such as "12,5" reads as 12.5; with both marks ("1,234.5", "1.234,5") the first is the thousands group, and
    /// several commas ("1,234,567") are thousands groups. "n/a" (a cell this app could not show) reads as NaN: pasting
    /// leaves that cell as it is.
    /// </summary>
    public static double[,] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => (line.Contains('\t') ? line.Split('\t').Select(cell => string.Concat(cell.Split(Spaces))).Where(cell => cell.Length > 0)
                    : line.Split(CellSeparators, StringSplitOptions.RemoveEmptyEntries)).Select(ParseNumber).ToArray())
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
        if (token.Equals("n/a", StringComparison.OrdinalIgnoreCase))
        {
            return double.NaN;
        }

        var normalized = token.Replace('−', '-');
        var comma = normalized.IndexOf(',');
        var point = normalized.IndexOf('.');
        normalized = comma >= 0 && point >= 0
            ? comma < point ? normalized.Replace(",", string.Empty) : normalized.Replace(".", string.Empty).Replace(',', '.')
            : normalized.Count(ch => ch == ',') > 1 ? normalized.Replace(",", string.Empty) : normalized.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{token}' is not a number.");
    }
}
