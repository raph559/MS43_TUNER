using System.ComponentModel;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// A screen's left list (the Tune rail, the Compare list) that the user can drag wider or narrower and fold to a strip.
/// Each screen keeps its own width and state in the settings; <see cref="Views.RailSizer"/> does the drawing side.
/// </summary>
public interface ISidePanel : INotifyPropertyChanged
{
    /// <summary>The width as the user dragged it, or null for the layout's own.</summary>
    double? RailWidth { get; }

    bool RailCollapsed { get; }

    void SetRailWidth(double? width, bool save);

    void SetRailCollapsed(bool collapsed);
}

public static class SidePanel
{
    public const double MinWidth = 200;

    public const double MaxWidth = 480;

    public const double CollapsedWidth = 48;
}
