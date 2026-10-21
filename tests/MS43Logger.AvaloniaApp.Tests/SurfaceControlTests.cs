using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The surface is handled like an object in the hand: dragging moves its near side with the pointer, the wheel and + / -
/// zoom (towards the pointer), Shift-drag moves it, Home puts it back, and it opens large in a dialog.
/// </summary>
public class SurfaceControlTests
{
    private static (Point Point, double Depth) Floor(Surface3D surface, double u, double w) =>
        SurfaceMath.Project(u, w, 0, surface.Yaw, surface.Pitch, surface.Bounds.Size, surface.Zoom, surface.Pan);

    // The floor corner nearest the viewer.
    private static (double U, double W) NearCorner(Surface3D surface) =>
        new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }.MaxBy(corner => Floor(surface, corner.Item1, corner.Item2).Depth);

    private static Point Centre(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Drag(Window window, Point from, Vector by, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(from, MouseButton.Left, modifiers);
        window.MouseMove(from + by, modifiers);
        window.MouseUp(from + by, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<(MainWindow Window, Surface3D Surface)> Open()
    {
        var (window, _) = await TuneScreen.OpenAsync();
        return (window, Find.Named<Surface3D>(window, "Surface"));
    }

    [AvaloniaFact]
    public async Task DraggingRightMovesTheNearSideRight()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var near = NearCorner(surface);
        var before = Floor(surface, near.U, near.W).Point;

        Drag(window, Centre(surface, window), new Vector(30, 0));

        Assert.True(Floor(surface, near.U, near.W).Point.X > before.X + 5, $"{before.X} -> {Floor(surface, near.U, near.W).Point.X}");
    }

    [AvaloniaFact]
    public async Task DraggingDownMovesTheNearSideDown()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var near = NearCorner(surface);
        var before = Floor(surface, near.U, near.W).Point;

        Drag(window, Centre(surface, window), new Vector(0, 20));

        Assert.True(Floor(surface, near.U, near.W).Point.Y > before.Y + 2, $"{before.Y} -> {Floor(surface, near.U, near.W).Point.Y}");
    }

    [AvaloniaFact]
    public async Task ArrowRightAlsoMovesTheNearSideRight()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var near = NearCorner(surface);
        var before = Floor(surface, near.U, near.W).Point;
        surface.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.True(Floor(surface, near.U, near.W).Point.X > before.X);
    }

    [AvaloniaFact]
    public async Task TheWheelZoomsTowardsThePointer()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var centre = Floor(surface, 0.5, 0.5).Point;
        var local = new Point(surface.Bounds.Width * 0.3, surface.Bounds.Height * 0.4);
        var at = surface.TranslatePoint(local, window)!.Value;

        window.MouseWheel(at, new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.True(surface.Zoom > 1.05, $"zoom {surface.Zoom}");
        var expected = local + (centre - local) * surface.Zoom;
        var now = Floor(surface, 0.5, 0.5).Point;
        Assert.Equal(expected.X, now.X, 3);
        Assert.Equal(expected.Y, now.Y, 3);
    }

    [AvaloniaFact]
    public async Task PlusMinusZoomWithinLimitsAndHomeResetsEverything()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var (yaw, pitch) = (surface.Yaw, surface.Pitch);
        surface.Focus();

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);
        Assert.True(surface.Zoom > 1);
        for (var i = 0; i < 30; i++)
        {
            window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);
        }

        Assert.Equal(Surface3D.MaxZoom, surface.Zoom, 6);
        for (var i = 0; i < 40; i++)
        {
            window.KeyPressQwerty(PhysicalKey.NumPadSubtract, RawInputModifiers.None);
        }

        Assert.Equal(Surface3D.MinZoom, surface.Zoom, 6);
        Drag(window, Centre(surface, window), new Vector(20, 10));
        Drag(window, Centre(surface, window), new Vector(15, 5), RawInputModifiers.Shift);

        surface.Focus();
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);

        Assert.Equal((yaw, pitch, 1.0, default(Vector)), (surface.Yaw, surface.Pitch, surface.Zoom, surface.Pan));
    }

    [AvaloniaFact]
    public async Task ShiftDragMovesWithoutTurning()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        var (yaw, pitch) = (surface.Yaw, surface.Pitch);

        Drag(window, Centre(surface, window), new Vector(25, -10), RawInputModifiers.Shift);

        Assert.Equal((yaw, pitch), (surface.Yaw, surface.Pitch));
        Assert.Equal(new Vector(25, -10), surface.Pan);
    }

    [AvaloniaFact]
    public async Task ZoomButtonsZoom()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();

        Find.Named<Button>(window, "SurfaceZoomInButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(surface.Zoom > 1);
        Find.Named<Button>(window, "SurfaceZoomOutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find.Named<Button>(window, "SurfaceZoomOutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(surface.Zoom < 1);
    }

    [AvaloniaFact]
    public async Task EnlargeOpensTheSurfaceLargeFromTheSameView()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        surface.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);

        Find.Named<Button>(window, "SurfaceEnlargeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var dialogs = Find.Named<Shell.DialogHost>(window, "Dialogs");
        Assert.True(dialogs.IsOpen);
        var large = Find.Named<Surface3D>(window, "LargeSurface");
        Assert.Same(surface.Editor, large.Editor);
        Assert.True(large.Bounds.Width >= 800 && large.Bounds.Height >= 500, $"{large.Bounds.Size}");
        Assert.Equal((surface.Yaw, surface.Pitch, surface.Zoom), (large.Yaw, large.Pitch, large.Zoom));
        Assert.Equal(304, large.IncludedCellCount);
        Screens.Save(window, "surface-large");

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(dialogs.IsOpen);
    }
}
