using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class CompareGridTests
{
    private static readonly XdfMath Identity = new("X", [new XdfVariable("X", XdfVariableKind.Raw, null)]);

    /// <summary>A 1 × n table of the given physical values (raw = value), with two decimals and units "ms".</summary>
    internal static TableValues Row(params double[] values)
    {
        var data = new XdfData(0x70100, 8, true, true, 1, values.Length, null);
        var axis = new XdfAxis("x", string.Empty, values.Length, null, null, [], Identity, 0);
        var table = new XdfTable(1, "t", string.Empty, [], axis, axis with { Id = "y", Count = 1 }, new XdfZ(data, Identity, "ms", 2, 1, null, null));
        var raw = new long[1, values.Length];
        var physical = new double[1, values.Length];
        for (var c = 0; c < values.Length; c++)
        {
            raw[0, c] = (long)values[c];
            physical[0, c] = values[c];
        }

        var labels = values.Select((_, i) => i.ToString()).ToList();
        var x = new AxisValues(null, string.Empty, values.Select((_, i) => (double)i).ToList(), labels, 0);
        return new TableValues(table, 1, values.Length, raw, physical, x, new AxisValues(null, string.Empty, [0.0], ["0"], 0));
    }

    private static (Window Window, MapGrid Grid) Show(TableEditorViewModel editor, ThemeVariant theme)
    {
        var grid = new MapGrid { Editor = editor, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 600, Height = 200, RequestedThemeVariant = theme, Content = grid };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, grid);
    }

    private static Color PixelAt(TopLevel window, Point point)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame()!;
        using var buffer = bitmap.Lock();
        var pixel = Marshal.ReadInt32(buffer.Address, (int)point.Y * buffer.RowBytes + (int)point.X * 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }

    private static void Near(Color expected, Color actual) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= 8 && Math.Abs(expected.G - actual.G) <= 8 && Math.Abs(expected.B - actual.B) <= 8,
            $"expected {expected}, got {actual}");

    [AvaloniaFact]
    public void DivergingEndsAndMiddle()
    {
        var pane = Color.Parse("#0A0B0D");
        var below = Color.Parse("#6CA7F5");
        var above = Color.Parse("#F87F99");

        Assert.Equal(pane, MapRamp.Diverging(0, pane, below, above));
        Assert.Equal(above, MapRamp.Diverging(1, pane, below, above));
        Assert.Equal(below, MapRamp.Diverging(-1, pane, below, above));
        Assert.Equal(above, MapRamp.Diverging(2, pane, below, above));
        Assert.Equal(pane, MapRamp.Diverging(double.NaN, pane, below, above));

        // The mid mixes of system 2.5, within 1 per channel.
        Close(Color.Parse("#9D5263"), MapRamp.Diverging(0.5, pane, below, above));
        Close(Color.Parse("#7595D3"), MapRamp.Diverging(-0.5, Colors.White, Color.Parse("#1F53B8"), Color.Parse("#CE2854")));
    }

    private static void Close(Color expected, Color actual) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1,
            $"expected {expected}, got {actual}");

    [AvaloniaFact]
    public void ReadOnlyGridRefusesTyping()
    {
        var editor = TableEditorViewModel.Snapshot(Row(1, 2, 3), new HashSet<(int, int)>());
        var (window, grid) = Show(editor, ThemeVariant.Dark);
        editor.Selection = new CellRange(0, 0, 0, 0);
        grid.Focus();

        window.KeyTextInput("5");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyTextInput("+");

        Assert.True(editor.IsReadOnly);
        Assert.Equal(1, editor.Values.Physical[0, 0]);
        Assert.Equal(TableEditorViewModel.ReadOnlyMessage, editor.Message);
        Assert.False(editor.Apply(new EditRequest(EditKind.Set, 9)));
        Assert.Equal("1", editor.Copy());
    }

    [AvaloniaFact]
    public void DifferingCellsWearTheRing()
    {
        var editor = TableEditorViewModel.Snapshot(Row(1, 2, 3), new HashSet<(int, int)> { (0, 1) }, mark: CellMark.Differs);
        var (window, grid) = Show(editor, ThemeVariant.Dark);
        var cell = grid.CellBounds(0, 1);
        Point At(double x, double y) => grid.TranslatePoint(new Point(x, y), window)!.Value;

        // A 6 px ring 2 px inside the top-right corner: its rim takes the text colour, its middle keeps the fill.
        var fill = PixelAt(window, At(cell.Left + 3, cell.Bottom - 3));
        var rim = PixelAt(window, At(cell.Right - 5, cell.Top + 2));
        var middle = PixelAt(window, At(cell.Right - 5, cell.Top + 5));
        Assert.Equal(CellMark.Differs, editor.Mark);
        Assert.True(Math.Abs(rim.R - MapRamp.TextOn(fill).R) < 90, $"rim {rim} is not the text colour");
        Near(fill, middle);
    }

    [AvaloniaFact]
    public void ZeroDeltaCellsArePaneWithInk3Text()
    {
        var editor = TableEditorViewModel.Snapshot(Row(-1, 0, 1), new HashSet<(int, int)> { (0, 0), (0, 2) }, isDelta: true);
        var (window, grid) = Show(editor, ThemeVariant.Dark);
        var cell = grid.CellBounds(0, 1);

        // System 2.5: no difference keeps the Pane fill and prints the item's zero in Ink3, so changed cells carry the colour.
        Assert.Equal("0.00", grid.CellText(0, 1));
        Near(Color.Parse("#0A0B0D"), PixelAt(window, grid.TranslatePoint(new Point(cell.Left + 3, cell.Bottom - 3), window)!.Value));
        // The brightest pixel inside the cell is its text: Ink3 (#858C97), not the white of a coloured cell.
        var origin = grid.TranslatePoint(cell.TopLeft, window)!.Value;
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var brightest = 0;
        for (var y = (int)origin.Y + 2; y < (int)(origin.Y + cell.Height) - 2; y++)
        {
            for (var x = (int)origin.X + 2; x < (int)(origin.X + cell.Width) - 2; x++)
            {
                var pixel = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                brightest = Math.Max(brightest, (pixel & 0xFF) + (pixel >> 8 & 0xFF) + (pixel >> 16 & 0xFF));
            }
        }

        Assert.InRange(brightest, 0x85 + 0x8C + 0x97 - 30, 0x85 + 0x8C + 0x97 + 30);
    }

    [AvaloniaFact]
    public void DeltaGridColoursBySign()
    {
        var editor = TableEditorViewModel.Snapshot(Row(-1, 0, 1), new HashSet<(int, int)> { (0, 0), (0, 2) }, isDelta: true);
        var (window, grid) = Show(editor, ThemeVariant.Dark);

        Point Corner(int col)
        {
            var cell = grid.CellBounds(0, col);
            return grid.TranslatePoint(new Point(cell.Left + 5, cell.Bottom - 5), window)!.Value;
        }

        Assert.True(editor.IsDelta);
        Near(Color.Parse("#6CA7F5"), PixelAt(window, Corner(0)));
        Near(Color.Parse("#0A0B0D"), PixelAt(window, Corner(1)));
        Near(Color.Parse("#F87F99"), PixelAt(window, Corner(2)));
        Screens.Save(window, "compare-delta-row");
    }
}
