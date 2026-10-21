using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Tuning from the surface: pick cells on it, step them with + and -, or Ctrl-drag them up and down.</summary>
public sealed class SurfaceEditTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-surf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell, Surface3D Surface)> Open()
    {
        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var surface = Find.Named<Surface3D>(window, "Surface");
        surface.ZoomBy(2.2);
        Screens.Capture(window)?.Dispose();
        return (window, shell, surface);
    }

    private static Point InWindow(Surface3D surface, Window window, Point local) => surface.TranslatePoint(local, window)!.Value;

    private static void Click(Window window, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task ClickingAPointSelectsItsCellAndShiftClickARange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;

        Click(window, InWindow(surface, window, surface.PointOf(5, 3)!.Value));
        Assert.Equal(new CellRange(5, 3, 5, 3), editor.Selection);

        Click(window, InWindow(surface, window, surface.PointOf(7, 6)!.Value), RawInputModifiers.Shift);
        Assert.Equal(new CellRange(5, 3, 7, 6), editor.Selection);
    }

    [AvaloniaFact]
    public async Task ClickingEmptySpaceKeepsTheSelectionAndDraggingStillTurns()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(2, 2, 2, 2);
        var yaw = surface.Yaw;

        Click(window, InWindow(surface, window, new Point(3, 3)));
        Assert.Equal(new CellRange(2, 2, 2, 2), editor.Selection);

        var from = InWindow(surface, window, surface.PointOf(5, 3)!.Value);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(30, 0));
        window.MouseUp(from + new Vector(30, 0), MouseButton.Left);
        Assert.NotEqual(yaw, surface.Yaw);
        Assert.Equal(new CellRange(2, 2, 2, 2), editor.Selection);
    }

    [AvaloniaFact]
    public async Task PlusAndMinusStepTheSelectionAndCtrlPlusZooms()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(5, 3, 5, 3);
        var before = editor.Values.Physical[5, 3];
        var zoom = surface.Zoom;
        surface.Focus();

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);
        Assert.True(editor.Values.Physical[5, 3] > before);
        Assert.Equal(zoom, surface.Zoom);

        window.KeyPressQwerty(PhysicalKey.NumPadSubtract, RawInputModifiers.None);
        Assert.Equal(before, editor.Values.Physical[5, 3]);

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Assert.True(surface.Zoom > zoom);
        Assert.Equal(before, editor.Values.Physical[5, 3]);
    }

    [AvaloniaFact]
    public async Task CtrlDragRaisesTheSelectionInOneUndoStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(5, 3, 5, 4);
        var raw = (editor.Values.Raw[5, 3], editor.Values.Raw[5, 4]);
        var from = InWindow(surface, window, new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2));

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(from - new Vector(0, 5 * Surface3D.PixelsPerStep), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, surface.PendingSteps);
        Assert.Equal(raw, (editor.Values.Raw[5, 3], editor.Values.Raw[5, 4]));

        window.MouseUp(from - new Vector(0, 5 * Surface3D.PixelsPerStep), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, surface.PendingSteps);
        Assert.Equal((raw.Item1 + 5, raw.Item2 + 5), (editor.Values.Raw[5, 3], editor.Values.Raw[5, 4]));
        Assert.Equal("Undo: Step 2 cells of ip_iga_ron98_pl__n__maf up", shell.Tune.UndoTip);
        shell.Tune.Undo();
        Assert.Equal(raw, (editor.Values.Raw[5, 3], editor.Values.Raw[5, 4]));
    }

    [AvaloniaFact]
    public async Task EscapeCancelsACtrlDrag()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(5, 3, 5, 3);
        var raw = editor.Values.Raw[5, 3];
        var from = InWindow(surface, window, new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2));
        surface.Focus();

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(from - new Vector(0, 40), RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.MouseUp(from - new Vector(0, 40), MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(raw, editor.Values.Raw[5, 3]);
        Assert.Equal(0, surface.PendingSteps);
    }

    [AvaloniaFact]
    public async Task TheHeightAxisStandsOnTheLeftCorner()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, _, surface) = await Open();
        foreach (var yaw in new[] { -0.72, 0.4, 1.8, -2.4 })
        {
            surface.ResetView();
            surface.TurnBy(yaw - surface.Yaw);
            var size = surface.Bounds.Size;
            (double U, double W)[] corners = [(0, 0), (1, 0), (0, 1), (1, 1)];
            var left = corners.MinBy(corner => SurfaceMath.Project(corner.U, corner.W, 0, surface.Yaw, surface.Pitch, size).Point.X);

            Assert.Equal(left, surface.HeightAxisCorner);
        }
    }

    private static Surface3D Enlarge(MainWindow window)
    {
        Find.Named<Button>(window, "SurfaceEnlargeButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var large = Find.Named<Surface3D>(window, "LargeSurface");
        Screens.Capture(window)?.Dispose();
        return large;
    }

    [AvaloniaFact]
    public async Task UndoAndRedoWorkInTheEnlargedSurface()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(5, 3, 5, 3);
        var raw = editor.Values.Raw[5, 3];
        var large = Enlarge(window);
        large.Focus();

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);
        Assert.NotEqual(raw, editor.Values.Raw[5, 3]);

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Assert.Equal(raw, editor.Values.Raw[5, 3]);
        Assert.True(Find.Named<Shell.DialogHost>(window, "Dialogs").IsOpen);

        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        Assert.NotEqual(raw, editor.Values.Raw[5, 3]);

        Find.Named<Button>(window, "SurfaceUndoButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(raw, editor.Values.Raw[5, 3]);

        // Other shortcuts still wait for the dialog to close.
        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Assert.False(shell.Tune.RailCollapsed);
    }

    [AvaloniaFact]
    public async Task TheSurfaceSaysWhatIsSelected()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;

        editor.Selection = new CellRange(5, 3, 5, 3);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(editor.SelectionText, surface.InfoText);

        editor.Selection = new CellRange(5, 3, 7, 6);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith(editor.SelectionText, surface.InfoText);
        var values = Enumerable.Range(5, 3).SelectMany(r => Enumerable.Range(3, 4).Select(c => editor.Values.Physical[r, c])).ToList();
        Assert.Contains($"values {editor.CellTextOf(values.Min())} to {editor.CellTextOf(values.Max())}", surface.InfoText);
    }

    [AvaloniaFact]
    public async Task ACtrlDragSaysHowMuchItChangesAndWhereItEnds()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, surface) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(5, 3, 5, 3);
        var before = editor.Values.Physical[5, 3];
        var from = InWindow(surface, window, new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2));

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(from - new Vector(0, 5 * Surface3D.PixelsPerStep), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        // Ignition runs 0.375 °CRK a step: five steps up add 1.875.
        Assert.Equal($"+5 steps: +1.9 °CRK, {editor.CellTextOf(before)} → {editor.CellTextOf(before + 1.875)} °CRK", surface.InfoText);
        window.MouseUp(from - new Vector(0, 5 * Surface3D.PixelsPerStep), MouseButton.Left, RawInputModifiers.Control);
    }
}
