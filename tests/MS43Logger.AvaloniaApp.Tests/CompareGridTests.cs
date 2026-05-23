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
        var middle = Color.Parse("#141920");
        var down = Color.Parse("#4C8DFF");
        var up = Color.Parse("#FF5C7A");

        Assert.Equal(middle, HeatRamp.Diverging(0, middle, down, up));
        Assert.Equal(up, HeatRamp.Diverging(1, middle, down, up));
        Assert.Equal(down, HeatRamp.Diverging(-1, middle, down, up));
        Assert.Equal(up, HeatRamp.Diverging(2, middle, down, up));
        Assert.Equal(middle, HeatRamp.Diverging(double.NaN, middle, down, up));
        var half = HeatRamp.Diverging(0.5, middle, down, up);
        Assert.True(half.R > middle.R && half.R < up.R);
    }

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
        Near(Color.Parse("#4C8DFF"), PixelAt(window, Corner(0)));
        Near(Color.Parse("#141920"), PixelAt(window, Corner(1)));
        Near(Color.Parse("#FF5C7A"), PixelAt(window, Corner(2)));
        Screens.Save(window, "compare-delta-row");
    }
}
