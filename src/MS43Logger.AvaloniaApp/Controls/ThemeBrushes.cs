using Avalonia.Controls;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>Token lookups for custom-drawn controls, following the control's own theme variant.</summary>
internal static class ThemeBrushes
{
    public static IBrush Get(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    public static Color Color(Control control, string key) =>
        Get(control, key) is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    public static FontFamily Font(Control control, string key) =>
        control.TryFindResource(key, out var value) && value is FontFamily family ? family : FontFamily.Default;
}
