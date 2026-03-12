using System.Globalization;
using System.Text;

namespace MS43Logger.Core.Tuning;

/// <summary>Plain-text rendering of XDF items, for the CLI and for checks against TunerPro.</summary>
public static class TableTextFormatter
{
    public static string Format(TableValues values, bool raw)
    {
        ArgumentNullException.ThrowIfNull(values);
        var z = values.Table.Z;
        var lines = new List<string>
        {
            $"{values.Table.Title}  0x{z.Data.Address ?? 0:X5}  {values.Rows} × {values.Cols}  {z.Units}".TrimEnd(),
        };

        if (values.Table.Description.Length > 0)
        {
            lines.Add(values.Table.Description.Replace('\n', ' '));
        }

        var cells = new string[values.Rows, values.Cols];
        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                cells[r, c] = raw
                    ? values.Raw[r, c].ToString(CultureInfo.InvariantCulture)
                    : ValueCodec.Format(values.Physical[r, c], values.Raw[r, c], z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
            }
        }

        var corner = $"{values.Y.Units} \\ {values.X.Units}";
        var labelWidth = values.Y.Labels.Append(corner).Max(label => label.Length);
        var widths = Enumerable.Range(0, values.Cols)
            .Select(c => Enumerable.Range(0, values.Rows).Select(r => cells[r, c].Length).Append(Label(values.X, c).Length).Max() + 3)
            .ToArray();

        var header = new StringBuilder(corner.PadRight(labelWidth));
        for (var c = 0; c < values.Cols; c++)
        {
            header.Append(Label(values.X, c).PadLeft(widths[c]));
        }

        lines.Add(header.ToString());
        for (var r = 0; r < values.Rows; r++)
        {
            var line = new StringBuilder(Label(values.Y, r).PadRight(labelWidth));
            for (var c = 0; c < values.Cols; c++)
            {
                line.Append(cells[r, c].PadLeft(widths[c]));
            }

            lines.Add(line.ToString());
        }

        return string.Join("\n", lines);
    }

    public static string FormatConstant(XdfConstant constant, ConstantValue value)
    {
        ArgumentNullException.ThrowIfNull(constant);
        ArgumentNullException.ThrowIfNull(value);
        var units = constant.Units.Length > 0 ? " " + constant.Units : string.Empty;
        return $"{constant.Title} = {value.Display}{units} (raw {value.Raw.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string Label(AxisValues axis, int index) => index < axis.Labels.Count ? axis.Labels[index] : string.Empty;
}
