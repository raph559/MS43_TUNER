using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>Token lookups for custom-drawn controls, following the control's own theme variant.</summary>
internal static class ThemeBrushes
{
    public static IBrush Get(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    /// <summary>A Color resource, or the colour of a solid brush resource.</summary>
    public static Color Color(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) switch
        {
            true when value is Color colour => colour,
            true when value is ISolidColorBrush solid => solid.Color,
            _ => Colors.Gray,
        };

    /// <summary>Orange means "now": the live cursor is needle orange while samples arrive, grey once they stop.</summary>
    public static Color Cursor(Control control, bool stale) => Color(control, stale ? "Ink3Brush" : "NowBrush");

    public static FontFamily Font(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is FontFamily family ? family : FontFamily.Default;

    public static bool IsNight(Control control) => control.ActualThemeVariant == ThemeVariant.Dark;
}
