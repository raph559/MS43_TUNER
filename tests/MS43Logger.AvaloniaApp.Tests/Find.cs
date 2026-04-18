using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Tests;

internal static class Find
{
    /// <summary>A named control anywhere in the window, across user-control name scopes.</summary>
    public static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);
}
