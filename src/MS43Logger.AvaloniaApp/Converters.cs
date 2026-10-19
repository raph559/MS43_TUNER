using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp;

public static class Converters
{
    public static readonly IValueConverter Indent = new FuncValueConverter<bool, Thickness>(indented => new Thickness(indented ? 14 : 0, 0, 0, 0));

    public static readonly IValueConverter ShapeTitle = new FuncValueConverter<bool, string>(surface => surface ? "Surface" : "Curve");

    public static readonly IValueConverter Bold = new FuncValueConverter<bool, FontWeight>(bold => bold ? FontWeight.SemiBold : FontWeight.Normal);
}
