using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>The map's surface, large, shown in the window's <see cref="DialogHost"/> from the inspector's Enlarge button.</summary>
public partial class SurfaceDialog : UserControl
{
    private const double ZoomStep = 1.2;

    public SurfaceDialog()
    {
        InitializeComponent();
    }

    /// <param name="editor">The map shown (its live cursor comes along).</param>
    /// <param name="title">The map's id, under the dialog title.</param>
    /// <param name="from">The inspector's plot: the large one opens on its turn, zoom and position.</param>
    /// <param name="window">The window's client size; the plot takes most of it.</param>
    public SurfaceDialog(TableEditorViewModel editor, string title, Surface3D from, Size window)
        : this()
    {
        ArgumentNullException.ThrowIfNull(from);
        MapTitle.Text = title;
        Width = Math.Clamp(window.Width - 160, 640, 1280);
        LargeSurface.Height = Math.Clamp(window.Height - 260, 360, 820);
        LargeSurface.Editor = editor;
        LargeSurface.CopyView(from, new Size(Width, LargeSurface.Height));
        AttachedToVisualTree += (_, _) => LargeSurface.Focus();
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => LargeSurface.ZoomBy(ZoomStep);

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => LargeSurface.ZoomBy(1 / ZoomStep);

    private void Reset_Click(object? sender, RoutedEventArgs e) => LargeSurface.ResetView();

    private void Close_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this);
}
