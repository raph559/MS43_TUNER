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
