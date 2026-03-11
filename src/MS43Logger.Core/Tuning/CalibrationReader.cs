using System.Globalization;

namespace MS43Logger.Core.Tuning;

/// <param name="Name">The linked axis table's title, or null for an axis with its own data or labels.</param>
public sealed record AxisValues(string? Name, string Units, IReadOnlyList<double> Values, IReadOnlyList<string> Labels, int? DecimalPlaces);

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

    private void EnsureReadable(string title, XdfData data)
    {
        if (data.Address is null || data.ElementBits is not (8 or 16 or 32) || !_image.Contains(data))
        {
            throw new InvalidDataException($"'{title}' cannot be read: its data at 0x{data.Address ?? 0:X} ({data.Count} × {data.ElementBits} bit) lies outside the image or has no usable storage.");
        }
    }

    private static List<string> Format(IEnumerable<double> values, int? decimals) =>
        values.Select(value => ValueCodec.Format(value, 0, decimals, null, 16)).ToList();
}
