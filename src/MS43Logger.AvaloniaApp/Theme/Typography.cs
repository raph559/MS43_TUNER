using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Theme;

public static class Typography
{
    /// <summary>
    /// Tabular figures. Bahnschrift's default figures are proportional ("1" is 681 units wide, "0" 1130), so a column
    /// of numbers would not line up and a changing reading would jitter; every number turns this on (system 3.3).
    /// </summary>
    public static FontFeatureCollection Tabular { get; } = [FontFeature.Parse("+tnum")];
}
