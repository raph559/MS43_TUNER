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

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Assert.True(surface.Zoom > 1);
        for (var i = 0; i < 30; i++)
        {
            window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        }

        Assert.Equal(Surface3D.MaxZoom, surface.Zoom, 6);
        for (var i = 0; i < 40; i++)
        {
            window.KeyPressQwerty(PhysicalKey.NumPadSubtract, RawInputModifiers.Control);
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
        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);

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

    // The volume (floor and full height) stays centred at every turn and tilt, and fits inside the plot.
    [AvaloniaFact]
    public void TheSurfaceIsCentredAndFitsAtEveryTurn()
    {
        var size = new Size(280, 176);
        foreach (var yaw in new[] { -2.5, -1.2, -0.72, 0, 0.6, 1.9 })
        {
            foreach (var pitch in new[] { 0.15, 0.6, 1.0, 1.35 })
            {
                var corners = (from u in new[] { 0.0, 1 } from w in new[] { 0.0, 1 } from h in new[] { 0.0, 1 }
                               select SurfaceMath.Project(u, w, h, yaw, pitch, size).Point).ToList();
                var left = corners.Min(p => p.X);
                var right = corners.Max(p => p.X);
                var top = corners.Min(p => p.Y);
                var bottom = corners.Max(p => p.Y);

                Assert.Equal(size.Width / 2, (left + right) / 2, 3);
                Assert.Equal(size.Height / 2, (top + bottom) / 2, 3);
                Assert.True(left >= 8 && top >= 8 && right <= size.Width - 8 && bottom <= size.Height - 8,
                    $"yaw {yaw}, pitch {pitch}: {left:0}..{right:0} x {top:0}..{bottom:0}");
            }
        }
    }

    [AvaloniaFact]
    public async Task TheAxesAndTheHeightHaveScales()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, surface) = await Open();
        Find.Named<Button>(window, "SurfaceEnlargeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var large = Find.Named<Surface3D>(window, "LargeSurface");
        Screens.Capture(window)?.Dispose();

        // Columns (MAFIGA, mg/stk), rows (rpm) and height (°CRK over the colour range 3.0 to 39.0), first and last.
        Assert.Contains("33.0", large.ScaleLabels);
        Assert.Contains("600.0", large.ScaleLabels);
        Assert.Contains("800", large.ScaleLabels);
        Assert.Contains("6990", large.ScaleLabels);
        Assert.Contains("3.0", large.ScaleLabels);
        Assert.Contains("39.0 °CRK", large.ScaleLabels);
        Assert.Contains("rpm", large.ScaleLabels);
        Assert.Contains("mg/stk", large.ScaleLabels);
        Assert.True(large.ScaleLabels.Count > surface.ScaleLabels.Count, "the large plot has room for more of the scale");
        Screens.Save(window, "surface-scales");
    }

    [AvaloniaFact]
    public void ScaleLabelsAreEvenlySpacedAndEndOnTheLast()
    {
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12, 14, 16, 19 }, Surface3D.Ticks(20, 400));
        Assert.Equal(new[] { 0, 19 }, Surface3D.Ticks(20, 30));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Surface3D.Ticks(4, 400));
        Assert.Equal(new[] { 0 }, Surface3D.Ticks(1, 400));

        // Every gap but the last is the same step.
        var ticks = Surface3D.Ticks(16, 300);
        var gaps = ticks.Zip(ticks.Skip(1), (a, b) => b - a).ToList();
        Assert.Single(gaps.Take(gaps.Count - 1).Distinct());
        Assert.Equal(15, ticks[^1]);
    }
}
