using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>
/// Keeps a clicked row where it was on screen while its list is rebuilt under it (Recent grows at the top, a category
/// opens, the current row moves), so the list never jumps under the pointer.
/// </summary>
internal static class ListAnchor
{
    /// <summary>The row's top, in the list's coordinates; null when it is not on screen.</summary>
    public static double? TopOf(ListBox list, int index) =>
        index >= 0 && list.ContainerFromIndex(index) is Control row && row.TranslatePoint(default, list) is { } at ? at.Y : null;

    /// <summary>Scrolls the rebuilt list so the row now at <paramref name="index"/> sits at <paramref name="top"/> again.</summary>
    public static void Keep(ListBox list, int index, double top)
    {
        if (index < 0 || list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller)
        {
            return;
        }

        list.UpdateLayout();
        if (TopOf(list, index) is not { } now)
        {
            // The rebuild left the row off screen: bring it in first, then line it up.
            list.ScrollIntoView(index);
            list.UpdateLayout();
            if (TopOf(list, index) is not { } shown)
            {
                return;
            }

            now = shown;
        }

        if (Math.Abs(now - top) > 0.5)
        {
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y + now - top));
            list.UpdateLayout();
        }
    }
}
