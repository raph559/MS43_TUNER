using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class MapGridTests
{
    private static async Task<(MainWindow Window, ShellViewModel Shell, MapGrid Grid)> Open(string? binPath = null)
    {
        var (window, shell) = await TuneScreen.OpenAsync(binPath: binPath);
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        return (window, shell, grid);
    }

    private static Point CellCentre(MapGrid grid, MainWindow window, int row, int col) =>
        grid.TranslatePoint(grid.CellBounds(row, col).Center, window)!.Value;

    private static long Raw(ShellViewModel shell, int row, int col) => shell.Tune.Editor!.Values.Raw[row, col];

    [AvaloniaFact]
    public async Task RendersEveryCellAndHitTests()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, grid) = await Open();

        Assert.True(grid.TryHitCell(grid.CellBounds(19, 0).Center, out var row, out var col));
        Assert.Equal((19, 0), (row, col));
        Assert.False(grid.TryHitCell(new Point(1, 1), out _, out _));
        Screens.Save(window, "tune-grid");
    }

    [AvaloniaFact]
    public async Task ClickShiftClickSelects()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();

        window.MouseDown(CellCentre(grid, window, 2, 1), MouseButton.Left);
        window.MouseUp(CellCentre(grid, window, 2, 1), MouseButton.Left);
        window.MouseDown(CellCentre(grid, window, 4, 3), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(CellCentre(grid, window, 4, 3), MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal(new CellRange(2, 1, 4, 3), shell.Tune.Editor!.Selection);
    }

    [AvaloniaFact]
    public async Task PlusKeyStepsSelectionByOneRaw()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        grid.Focus();

        window.KeyTextInput("+");

        Assert.Equal(81, Raw(shell, 0, 0));
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);
        shell.Tune.History!.Undo();
        Assert.Equal(80, Raw(shell, 0, 0));
    }

    [AvaloniaFact]
    public async Task TypingANumberSetsSelection()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 2);
        grid.Focus();

        window.KeyTextInput("1");
        window.KeyTextInput("0");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Equal(new long[] { 90, 90, 90 }, new[] { Raw(shell, 0, 0), Raw(shell, 0, 1), Raw(shell, 0, 2) });
        Screens.Save(window, "tune-grid-edited");
    }

    [AvaloniaFact]
    public async Task CopyPasteRoundTrips()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(18, 0, 19, 2);
        var text = editor.Copy();
        editor.Selection = new CellRange(0, 0, 0, 0);

        Assert.True(editor.Apply(new EditRequest(EditKind.Paste, Text: text)));

        Assert.Equal(31.5, editor.Values.Physical[0, 0]);
        Assert.Equal(-23.625, editor.Values.Physical[1, 2]);
    }

    private static Color PixelAt(TopLevel window, Point point)
    {
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var pixel = Marshal.ReadInt32(buffer.Address, (int)point.Y * buffer.RowBytes + (int)point.X * 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }

    private static bool Near(Color expected, Color actual, int tolerance = 8) =>
        Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;

    private static Point InWindow(MapGrid grid, MainWindow window, Point point) => grid.TranslatePoint(point, window)!.Value;

    // Reported: the selection ring of a cell in the first row or column was cut by the header bands painted over it.
    [AvaloniaFact]
    public async Task TheSelectionRingIsWholeOnTheFirstRowAndColumn()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        Dispatcher.UIThread.RunJobs();
        var cell = grid.CellBounds(0, 0);
        var ink1 = ((Avalonia.Media.ISolidColorBrush)grid.FindResource(grid.ActualThemeVariant, "Ink1Brush")!).Color;

        var above = PixelAt(window, InWindow(grid, window, new Point(cell.Center.X, cell.Top - 1.5)));
        var left = PixelAt(window, InWindow(grid, window, new Point(cell.Left - 1.5, cell.Center.Y)));

        Assert.True(Near(ink1, above, 24), $"above the cell: {above}, ring {ink1}");
        Assert.True(Near(ink1, left, 24), $"left of the cell: {left}, ring {ink1}");
    }

    [AvaloniaFact]
    public async Task TheSelectionGlidesToTheNewCell()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var animations = MotionSettings.AnimationsEnabled;
        MotionSettings.AnimationsEnabled = true;
        try
        {
            var (_, shell, grid) = await Open();
            var editor = shell.Tune.Editor!;
            editor.Selection = new CellRange(0, 0, 0, 0);
            Settle(grid);
            var from = grid.SelectionBox!.Value;

            var since = System.Diagnostics.Stopwatch.StartNew();
            editor.Selection = new CellRange(5, 5, 5, 5);
            grid.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            var target = grid.CellBounds(5, 5).Inflate(1);
            if (since.Elapsed < MapGrid.SelectionGlide / 2)
            {
                Assert.NotEqual(target, grid.SelectionBox);
                Assert.True(Distance(grid.SelectionBox!.Value, from) < Distance(target, from), "it starts from the old cell");
            }

            Settle(grid);
            Assert.Equal(target, grid.SelectionBox);
        }
        finally
        {
            MotionSettings.AnimationsEnabled = animations;
        }
    }

    [AvaloniaFact]
    public async Task WithAnimationsOffTheSelectionJumps()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var animations = MotionSettings.AnimationsEnabled;
        MotionSettings.AnimationsEnabled = false;
        try
        {
            var (_, shell, grid) = await Open();
            shell.Tune.Editor!.Selection = new CellRange(5, 5, 5, 5);

            Assert.Equal(grid.CellBounds(5, 5).Inflate(1), grid.SelectionBox);
        }
        finally
        {
            MotionSettings.AnimationsEnabled = animations;
        }
    }

    private static double Distance(Rect a, Rect b) => Point.Distance(a.Center, b.Center);

    private static void Settle(MapGrid grid)
    {
        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        grid.InvalidateVisual();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task HatchedRowIsOutOfTheColourRange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();

        // Row 19 (6990 rpm) holds raw 0, -23.6 °CRK, in every column: an outlier at the storage limit.
        Assert.Contains((19, 0), shell.Tune.Editor!.ColourRange.Hatched);
        var cell = grid.CellBounds(19, 0);
        var colour = PixelAt(window, InWindow(grid, window, new Point(cell.Left + 3, cell.Center.Y)));
        Assert.False(Near(MapRamp.Color(0, night: true), colour) || Near(MapRamp.Color(1, night: true), colour), $"{colour} is a ramp end");
    }

    [AvaloniaFact]
    public async Task CellTextUsesAsciiMinus()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, _, grid) = await Open();

        Assert.Equal("-23.6", grid.CellText(19, 0));
        Assert.Equal("6.4", grid.CellText(0, 0));
    }

    [AvaloniaFact]
    public void ConstantMapIsDrawnInMap4()
    {
        var editor = TableEditorViewModel.Snapshot(CompareGridTests.Row(5, 5, 5), new HashSet<(int, int)>());
        var grid = new MapGrid { Editor = editor, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 600, Height = 200, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Content = grid };
        window.Show();

        Assert.True(editor.ColourRange.IsConstant);
        for (var col = 0; col < 3; col++)
        {
            var cell = grid.CellBounds(0, col);
            var colour = PixelAt(window, grid.TranslatePoint(new Point(cell.Left + 4, cell.Bottom - 4), window)!.Value);
            Assert.True(Near(Color.Parse("#368EB3"), colour), $"cell {col} is {colour}, not Map4");
        }
    }

    [AvaloniaFact]
    public async Task KeyboardFocusShowsTheRingRoundTheActiveCell()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(9, 5, 9, 5);
        var cell = grid.CellBounds(9, 5);

        // The ring of system 1.3 sits 2 to 4 px outside the active cell, after the 2 px Pane gap.
        var ring = InWindow(grid, window, new Point(cell.Center.X, cell.Top - 3));
        Assert.False(Near(Color.Parse("#F3F5F7"), PixelAt(window, ring)), "a ring before the grid has focus");

        grid.Focus(NavigationMethod.Tab);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(Near(Color.Parse("#F3F5F7"), PixelAt(window, ring)), "no ring once the keyboard focuses the grid");
    }

    [AvaloniaFact]
    public async Task SelectionRectangleIsInk()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(9, 4, 12, 6);

        // 2 px Ink1 outside the range, 1 px Pane keyline just inside it.
        var first = grid.CellBounds(9, 5);
        Assert.True(Near(Color.Parse("#F3F5F7"), PixelAt(window, InWindow(grid, window, new Point(first.Center.X, first.Top - 1)))), "no Ink1 line above the range");
        Assert.True(Near(Color.Parse("#0A0B0D"), PixelAt(window, InWindow(grid, window, new Point(first.Center.X, first.Top + 0.5)))), "no Pane keyline inside the range");
    }

    [AvaloniaFact]
    public async Task ChangedTriangleMarksEditedCell()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "ms43-grid-" + Guid.NewGuid().ToString("N"));
        var (window, shell, grid) = await Open(TuneScreen.CopyCar(folder));
        var animations = MotionSettings.AnimationsEnabled;
        MotionSettings.AnimationsEnabled = false;
        try
        {
            var editor = shell.Tune.Editor!;
            editor.Selection = new CellRange(5, 5, 5, 5);
            var cell = grid.CellBounds(5, 5);
            var corner = InWindow(grid, window, new Point(cell.Right - 4, cell.Top + 3));
            var before = PixelAt(window, corner);

            editor.Apply(new EditRequest(EditKind.Step, 1));
            editor.Selection = new CellRange(0, 0, 0, 0);

            Assert.Equal(CellMark.Changed, editor.Mark);
            var after = PixelAt(window, corner);
            Assert.False(Near(before, after, 2), "the corner did not change");
            var fill = PixelAt(window, InWindow(grid, window, new Point(cell.Left + 4, cell.Center.Y)));
            Assert.True(Near(MapRamp.TextOn(fill), after, 40), $"the mark {after} is not the text colour on {fill}");
        }
        finally
        {
            // Each test starts a new app, which loads the motion layer when this is on: leave it as it was.
            MotionSettings.AnimationsEnabled = animations;
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task TypingShowsTheEnterHint()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        shell.Tune.Editor!.Selection = new CellRange(9, 4, 12, 6);
        grid.Focus();

        window.KeyTextInput("27.8");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("27.8", shell.Tune.Editor.TypedText);
        Assert.Equal("Enter sets 12 cells to 27.8 °CRK. Esc cancels.", shell.Tune.Editor.TypingHint);
        var keyLine = Find.Named<Control>(window, "KeyLine").GetVisualDescendants().OfType<NumberText>()
            .Where(text => text.IsEffectivelyVisible).Select(text => text.Value);
        Assert.Contains("Enter sets 12 cells to 27.8 °CRK. Esc cancels.", keyLine);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(string.Empty, shell.Tune.Editor.TypedText);
        Assert.Null(shell.Tune.Editor.TypingHint);
    }

    [AvaloniaFact] // "-5" Enter on the ignition map set +5
    public async Task MinusStartsANegativeNumberOnAMapThatGoesBelowZero()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(2, 2, 2, 2);
        grid.Focus();
        Assert.True(editor.TakesNegatives);

        window.KeyTextInput("-5");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("-5", editor.TypedText);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.InRange(editor.Values.Physical[2, 2], -5.4, -4.6);
    }

    [AvaloniaFact] // "-" alone then Enter still steps down one
    public async Task MinusThenEnterStepsDown()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(2, 2, 2, 2);
        grid.Focus();
        var before = editor.Values.Raw[2, 2];

        window.KeyTextInput("-");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(before - 1, editor.Values.Raw[2, 2]);
    }

    [AvaloniaFact] // in raw view a typed number is a raw count, not a value in the map's unit
    public async Task InRawViewTheTypedNumberIsRaw()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        var editor = shell.Tune.Editor!;
        editor.ShowRaw = true;
        editor.Selection = new CellRange(2, 2, 2, 2);
        grid.Focus();

        window.KeyTextInput("100");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Enter sets 1 cell to raw 100. Esc cancels.", editor.TypingHint);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(100, editor.Values.Raw[2, 2]);
    }

    [AvaloniaFact]
    public async Task ActiveCellIsWhereTheSelectionStarted()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        window.MouseDown(CellCentre(grid, window, 2, 1), MouseButton.Left);
        window.MouseUp(CellCentre(grid, window, 2, 1), MouseButton.Left);
        window.MouseDown(CellCentre(grid, window, 4, 3), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(CellCentre(grid, window, 4, 3), MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal((2, 1), shell.Tune.Editor!.ActiveCell);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Equal((5, 3), shell.Tune.Editor.ActiveCell);
        Assert.Equal(new CellRange(5, 3, 5, 3), shell.Tune.Editor.Selection);
    }

    [AvaloniaFact]
    public async Task RowsShrinkBeforeScrolling()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, grid) = await Open();
        Assert.Equal(26, grid.CellBounds(0, 0).Height);

        window.Width = 1024;
        window.Height = 680;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.InRange(grid.CellBounds(0, 0).Height, 20, 25.9);
    }

    [AvaloniaFact]
    public async Task NarrowGridScrollsWithItsRowHeaderInPlace()
    {

        // 16 columns of "-23.6" cannot be narrower than the number plus 8 px, so a 420 px viewport scrolls sideways.
        var values = Enumerable.Repeat(-23.6, 16).ToArray();
        var grid = new MapGrid { Editor = TableEditorViewModel.Snapshot(CompareGridTests.Row(values), new HashSet<(int, int)>()), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grid };
        var window = new Window { Width = 420, Height = 200, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Content = scroller };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width + 1, "the grid should scroll sideways");
        var header = grid.TranslatePoint(grid.RowHeaderBounds(0).TopLeft, window)!.Value;
        var cell = grid.TranslatePoint(grid.CellBounds(0, 0).TopLeft, window)!.Value;

        scroller.Offset = new Vector(120, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(header.X, grid.TranslatePoint(grid.RowHeaderBounds(0).TopLeft, window)!.Value.X, 1);
        Assert.Equal(cell.X - 120, grid.TranslatePoint(grid.CellBounds(0, 0).TopLeft, window)!.Value.X, 1);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task OneColumnTableIsCentred()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync("ip_iga_tco_st__tco");
        var grid = Find.Named<MapGrid>(window, "MapGrid");

        Assert.True(shell.Tune.CentreGrid);
        var pane = Find.Named<Control>(window, "TableArea");
        var paneCentre = pane.TranslatePoint(new Point(pane.Bounds.Width / 2, 0), window)!.Value.X;
        var gridCentre = grid.TranslatePoint(new Point(grid.Bounds.Width / 2, 0), window)!.Value.X;
        Assert.Equal(paneCentre, gridCentre, 1);

        // The key line follows the grid right under it, and the value keeps the units the column header shows.
        var gridBottom = grid.TranslatePoint(new Point(0, grid.Bounds.Height), window)!.Value.Y;
        var keyTop = Find.Named<Control>(window, "KeyArea").TranslatePoint(default, window)!.Value.Y;
        Assert.InRange(keyTop - gridBottom, 0, 16);
        Assert.Equal("7.50 °CRK, raw 148 (0x94)", shell.Tune.LimitFacts.Single(fact => fact.Label == "Active cell").Detail);
        Screens.Save(window, "restyle-tune-night-1440-curve");
    }

    [AvaloniaFact]
    public async Task AxisRowsAreNumbered()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync("ldpm_n_14");

        Assert.True(shell.Tune.Editor!.NumberRows);
        Assert.Equal("1", shell.Tune.Editor.RowLabel(0));
        Assert.Equal("20", shell.Tune.Editor.RowLabel(19));
    }

    [AvaloniaFact]
    public void FlashStartsAtTextColourAndEndsAtFill()
    {
        var fill = Color.Parse("#368EB3");
        var text = MapRamp.TextOn(fill);

        Assert.Equal(text, MapGrid.FlashColour(text, fill, 0));
        Assert.Equal(fill, MapGrid.FlashColour(text, fill, 1));

        // Cubic ease-out: seven eighths of the way there at half time.
        var half = MapGrid.FlashColour(text, fill, 0.5);
        Assert.Equal(Math.Round(text.R + 0.875 * (fill.R - text.R)), half.R, 1);
    }

    [AvaloniaFact]
    public async Task NoFlashWithAnimationsOff()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "ms43-grid-" + Guid.NewGuid().ToString("N"));
        var (_, shell, grid) = await Open(TuneScreen.CopyCar(folder));
        var animations = MotionSettings.AnimationsEnabled;
        try
        {
            MotionSettings.AnimationsEnabled = false;
            shell.Tune.Editor!.Apply(new EditRequest(EditKind.Step, 1));
            Assert.False(grid.IsFlashing);

            MotionSettings.AnimationsEnabled = true;
            shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
            Assert.True(grid.IsFlashing);
        }
        finally
        {
            // Each test starts a new app, which loads the motion layer when this is on: leave it as it was.
            MotionSettings.AnimationsEnabled = animations;
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ReadOnlyTableShowsMessage()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFTABLE uniqueid="0x10"><title>colmajor</title>
                <XDFAXIS id="z"><EMBEDDEDDATA mmedtypeflags="0x06" mmedaddress="0x7500" mmedelementsizebits="8" mmedrowcount="2" />
                <MATH equation="X"><VAR id="X" /></MATH></XDFAXIS></XDFTABLE>
            </XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        var document = CalibrationDocument.FromParts(xdf, BinImage.FromBytes(new byte[BinImage.Size]), null);
        var editor = new TableEditorViewModel(document, new EditHistory(document), xdf.Tables[0]) { Selection = new CellRange(0, 0, 0, 0) };

        Assert.False(editor.Apply(new EditRequest(EditKind.Set, 1)));
        Assert.Contains("colmajor", editor.Message);
        Assert.False(document.IsModified);
    }

    // At 1024 x 680 the 20 rows scroll: the arrow keys must not move the cell being edited out of sight.
    [AvaloniaFact]
    public async Task ArrowKeysKeepTheActiveCellInView()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, grid) = await Open();
        window.Width = 1024;
        window.Height = 680;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var viewport = grid.FindAncestorOfType<ScrollViewer>()!;
        Assert.True(viewport.Extent.Height > viewport.Viewport.Height + 1, "the grid scrolls at 1024 x 680");

        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        grid.Focus(NavigationMethod.Tab);
        for (var i = 0; i < 19; i++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cell = grid.CellBounds(19, 0);
        Assert.InRange(cell.Bottom, viewport.Offset.Y, viewport.Offset.Y + viewport.Viewport.Height + 0.5);

        for (var i = 0; i < 19; i++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewport.Offset.Y <= 0.5, "back at the first row, the grid scrolls back to the top");
    }
}
