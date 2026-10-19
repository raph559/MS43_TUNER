using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class MapGridTests
{
    private static async Task<(MainWindow Window, ShellViewModel Shell, MapGrid Grid)> Open()
    {
        var (window, shell) = await TuneScreen.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        return (window, shell, grid);
    }

    private static Point CellCentre(MapGrid grid, MainWindow window, int row, int col) =>
        grid.TranslatePoint(grid.CellBounds(row, col).Center, window)!.Value;

    private static long Raw(ShellViewModel shell, int row, int col) => shell.Tune.Editor!.Values.Raw[row, col];

    [AvaloniaFact]
    public void RampEndpointsMatchTokens()
    {
        Assert.Equal(Color.Parse("#0B1E33"), HeatRamp.Color(0));
        Assert.Equal(Color.Parse("#F2F7EC"), HeatRamp.Color(1));
        Assert.Equal(HeatRamp.Color(0), HeatRamp.Color(-3));
    }

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
        Assert.Equal("1 unsaved change", shell.Tune.DirtyText);
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
}
