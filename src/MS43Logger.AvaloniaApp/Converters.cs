using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public static class Converters
{
    public static readonly IValueConverter Indent = new FuncValueConverter<bool, Thickness>(indented => new Thickness(indented ? 14 : 0, 0, 0, 0));

    /// <summary>The glyph kind of a table that uses an axis: a map, or a curve when it has one row or one column.</summary>
    public static readonly IValueConverter TableKind = new FuncValueConverter<XdfItem?, ItemKind>(item =>
        item is XdfTable { Z.Data: { Rows: > 1, Cols: > 1 } } ? ItemKind.Map : ItemKind.Curve);

    /// <summary>A range of bytes outside the XDF, "0x03C43 to 0x03C9B".</summary>
    public static readonly IValueConverter RangeText = new FuncValueConverter<UnmappedRange?, string>(range =>
        range is null ? string.Empty : CompareViewModel.RangeText(range));

    public static readonly IValueConverter Bold = new FuncValueConverter<bool, FontWeight>(bold => bold ? FontWeight.SemiBold : FontWeight.Normal);
    public static readonly IValueConverter ShapeTitle = new FuncValueConverter<bool, string>(surface => surface ? "Surface" : "Curve");
}
