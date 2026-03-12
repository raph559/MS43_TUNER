using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <param name="Name">The linked axis table's title, or null for an axis with its own data or labels.</param>
public sealed record AxisValues(string? Name, string Units, IReadOnlyList<double> Values, IReadOnlyList<string> Labels, int? DecimalPlaces);

public sealed record TableValues(XdfTable Table, int Rows, int Cols, long[,] Raw, double[,] Physical, AxisValues X, AxisValues Y);

public sealed record ConstantValue(long Raw, double Physical, string Display);

/// <summary>Reads XDF tables, constants and flags out of a bin image, in raw and physical units.</summary>
public sealed class CalibrationReader
{
    private readonly XdfDefinition _xdf;
    private readonly BinImage _image;
    private readonly ValueCodec _codec;

    public CalibrationReader(XdfDefinition xdf, BinImage image)
    {
        _xdf = xdf ?? throw new ArgumentNullException(nameof(xdf));
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _codec = new ValueCodec(xdf, image);
    }

    public ValueCodec Codec => _codec;

    public TableValues ReadTable(XdfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var data = table.Z.Data;
        EnsureReadable(table.Title, data);

        var raw = new long[data.Rows, data.Cols];
        var physical = new double[data.Rows, data.Cols];
        for (var r = 0; r < data.Rows; r++)
        {
            for (var c = 0; c < data.Cols; c++)
            {
                raw[r, c] = _image.ReadElement(data, r * data.Cols + c);
                physical[r, c] = data.IsSupported ? _codec.ToPhysical(table.Z.Math, raw[r, c]) : double.NaN;
            }
        }

        return new TableValues(table, data.Rows, data.Cols, raw, physical, ReadAxis(table.X, data.Cols), ReadAxis(table.Y, data.Rows));
    }

    public ConstantValue ReadConstant(XdfConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        EnsureReadable(constant.Title, constant.Data);
        var raw = _image.ReadElement(constant.Data, 0);
        var physical = constant.Data.IsSupported ? _codec.ToPhysical(constant.Math, raw) : double.NaN;
        return new ConstantValue(raw, physical, ValueCodec.Format(physical, raw, constant.DecimalPlaces, constant.OutputType, constant.Data.ElementBits));
    }

    public bool ReadFlag(XdfFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);
        EnsureReadable(flag.Title, flag.Data);
        return (_image.ReadElement(flag.Data, 0) & flag.Mask) != 0;
    }

    /// <summary>Titles of items whose data does not lie entirely inside the image.</summary>
    public IReadOnlyList<string> FindOutOfRangeItems() =>
        _xdf.Tables.Select(table => (table.Title, table.Z.Data))
            .Concat(_xdf.Constants.Select(constant => (constant.Title, constant.Data)))
            .Concat(_xdf.Flags.Select(flag => (flag.Title, flag.Data)))
            .Where(item => item.Data.Address is not null && !_image.Contains(item.Data))
            .Select(item => item.Title)
            .ToList();

    private void EnsureReadable(string title, XdfData data)
    {
        if (data.Address is null || data.ElementBits is not (8 or 16 or 32) || !_image.Contains(data))
        {
            throw new InvalidDataException($"'{title}' cannot be read: its data at 0x{data.Address ?? 0:X} ({data.Count} × {data.ElementBits} bit) lies outside the image or has no usable storage.");
        }
    }

    private AxisValues ReadAxis(XdfAxis axis, int count)
    {
        if (axis.LinkedTableId is int link &&
            _xdf.FindTableById(link) is XdfTable linked &&
            linked.Z.Data.IsSupported &&
            _image.Contains(linked.Z.Data))
        {
            var decimals = axis.DecimalPlaces ?? linked.Z.DecimalPlaces;
            var values = Enumerable.Range(0, count)
                .Select(i => i < linked.Z.Data.Count ? _codec.ToPhysical(linked.Z.Math, _image.ReadElement(linked.Z.Data, i)) : double.NaN)
                .ToList();
            var units = axis.Units.Length > 0 ? axis.Units : linked.Z.Units;
            return new AxisValues(linked.Title, units, values, Format(values, decimals), decimals);
        }

        if (axis.Data is XdfData data && data.IsSupported && _image.Contains(data with { Rows = count, Cols = 1 }))
        {
            var values = Enumerable.Range(0, count)
                .Select(i => _codec.ToPhysical(axis.Math, _image.ReadElement(data, i)))
                .ToList();
            return new AxisValues(null, axis.Units, values, Format(values, axis.DecimalPlaces), axis.DecimalPlaces);
        }

        if (axis.Labels.Count > 0)
        {
            var labels = Enumerable.Range(0, count).Select(i => i < axis.Labels.Count ? axis.Labels[i] : string.Empty).ToList();
            var values = labels
                .Select(label => double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN)
                .ToList();
            return new AxisValues(null, axis.Units, values, labels, axis.DecimalPlaces);
        }

        var indices = Enumerable.Range(0, count).Select(i => (double)i).ToList();
        return new AxisValues(null, axis.Units, indices, indices.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList(), 0);
    }

    private static List<string> Format(IEnumerable<double> values, int? decimals) =>
        values.Select(value => ValueCodec.Format(value, 0, decimals, null, 16)).ToList();
}
