using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

/// <summary>The cursor from a V9 watch record: the library's indices and fractions, or the lookup's raw inputs through the axes.</summary>
public class LiveTableBindingWatchTests
{
    private static readonly LiveInputMap Map = LiveInputMap.LoadBundled("43X001")!;

    private static CalibrationDocument Car() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static (LiveTableBinding Binding, double[,] Physical) Bind(string title)
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, (XdfTable)document.Xdf.FindByTitle(title)!);
        return (binding, document.Reader.ReadTable(binding.Table).Physical);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void IndicesGiveTheCellAndItsFraction()
    {
        var (binding, physical) = Bind("ip_iga_ron98_pl__n__maf");
        var fix = binding.LocateWatched(new WatchRecord(WatchKind.MapIndices, 1, 3, 0x8000, 5, 0x4000, 16), physical);
        Assert.Equal(3.5, fix.Row!.Value.Position);
        Assert.Equal(5.25, fix.Col!.Value.Position);
        Assert.Equal(4, fix.Cells.Count);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CurveIndexUsesTheRowOnly()
    {
        var (binding, physical) = Bind("id_t_tco_puc_deacc__tco");
        var fix = binding.LocateWatched(new WatchRecord(WatchKind.CurveIndex, 1, 2, 0, 0, 0, 0), physical);
        var position = binding.Table.Z.Data.Rows > 1 ? fix.Row : fix.Col;
        Assert.Equal(2, position!.Value.Index);
        Assert.Single(fix.Cells);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void InputsGoThroughTheAxes()
    {
        var (binding, physical) = Bind("ip_iga_ron98_is__n__maf");
        Assert.NotNull(binding.RowBreakpoints);
        Assert.NotNull(binding.ColBreakpoints);
        var record = new WatchRecord(WatchKind.MapInputs, 1, (ushort)binding.RowBreakpoints![2], 0, (ushort)binding.ColBreakpoints![2], 0, 0);
        var fix = binding.LocateWatched(record, physical);
        Assert.Equal(new AxisPosition(2, 0, 0), fix.Row);
        Assert.Equal(new AxisPosition(2, 0, 0), fix.Col);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void U8AxisUsesTheLowByteOfTheInput()
    {
        var (binding, physical) = Bind("id_ectpwm_add__n__tco_sp");                // both axes are u8
        var (breaks, bits, row) = binding.RowBits == 8 ? (binding.RowBreakpoints!, 8, true) : (binding.ColBreakpoints!, binding.ColBits, false);
        Assert.Equal(8, bits);
        var input = (ushort)(0xAB00 | breaks[1]);
        var record = row
            ? new WatchRecord(WatchKind.MapInputs, 1, input, 0, (ushort)binding.ColBreakpoints![0], 0, 0)
            : new WatchRecord(WatchKind.MapInputs, 1, (ushort)binding.RowBreakpoints![0], 0, input, 0, 0);
        var fix = binding.LocateWatched(record, physical);
        Assert.Equal(1, (row ? fix.Row : fix.Col)!.Value.Index);
    }
}
