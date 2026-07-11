using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class LiveTableBindingTests
{
    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly LiveInputMap Map = LiveInputMap.LoadBundled("43X001")!;

    private static CalibrationDocument Car() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static XdfTable Table(CalibrationDocument document, string title) => (XdfTable)document.Xdf.FindByTitle(title)!;

    private static CursorFix Locate(CalibrationDocument document, LiveTableBinding binding, params (string, long)[] values) =>
        binding.Locate(Ms43x, LivePayloads.Build(Ms43x, values), document.Reader.ReadTable(binding.Table).Physical);

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ReplayedSamplesMoveTheCursorToTheExpectedCells()
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "ip_iga_ron98_pl__n__maf"));
        Assert.True(binding.HasInput, binding.Reason);
        Assert.Null(binding.CheckLogger(Ms43x));
        var physical = document.Reader.ReadTable(binding.Table).Physical;

        var a = Locate(document, binding, ("N", 800), ("MAFIGA", 778));
        Assert.Equal(new AxisPosition(0, 0, 0), a.Row);
        Assert.Equal(new AxisPosition(0, 0, 0), a.Col);
        Assert.Equal(new[] { new CellWeight(0, 0, 1) }, a.Cells);
        Assert.Equal(6.375, a.Value, 9);

        var b = Locate(document, binding, ("N", 2480 | 3 << 13), ("MAFIGA", 1415));
        double fr = 61166 / 65536.0, fc = 32698 / 65536.0;
        Assert.Equal(new AxisPosition(5, fr, 0), b.Row);
        Assert.Equal(new AxisPosition(1, fc, 0), b.Col);
        Assert.Equal(
            new[] { new CellWeight(5, 1, (1 - fr) * (1 - fc)), new CellWeight(5, 2, (1 - fr) * fc), new CellWeight(6, 1, fr * (1 - fc)), new CellWeight(6, 2, fr * fc) },
            b.Cells);
        Assert.Equal(CellWeights.Interpolate(physical, b.Cells), b.Value, 9);
        Assert.Equal(("N", 2480.0), (b.Inputs[0].Channel, b.Inputs[0].Physical));
        Assert.Equal("MAFIGA", b.Inputs[1].Channel);
        Assert.False(b.Approximate);

        var c = Locate(document, binding, ("N", 7200), ("MAFIGA", 20000));
        Assert.Equal(new AxisPosition(19, 0, 1), c.Row);
        Assert.Equal(new AxisPosition(15, 0, 1), c.Col);
        Assert.Equal(-23.625, c.Value, 9);
        Assert.Equal(7200, c.Inputs[0].Physical, 9);
        Assert.Equal(1, c.Inputs[0].Clamped);

        var d = Locate(document, binding, ("N", 500), ("MAFIGA", 0));
        Assert.Equal(new AxisPosition(0, 0, -1), d.Row);
        Assert.Equal(new AxisPosition(0, 0, -1), d.Col);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void DerivedInputsUseTheEcuVariables()
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "id_o2_int_max_mt__n__maf"));
        Assert.True(binding.HasInput, binding.Reason);
        Assert.False(binding.Interpolates);

        var fix = Locate(document, binding, ("N", 2480), ("MAFTI", 0x1500));
        Assert.Equal(new[] { new CellWeight(3, 2, 1) }, fix.Cells);
        Assert.Equal(77, fix.Inputs[0].Input);
        Assert.Equal(21, fix.Inputs[1].Input);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CoolantCurve()
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "id_t_tco_puc_deacc__tco"));
        var fix = Locate(document, binding, ("TCO", 124));
        Assert.Equal(new AxisPosition(1, 0, 0), fix.Row);
        Assert.Null(fix.Col);
        Assert.Equal(new[] { new CellWeight(1, 0, 1) }, fix.Cells);
        Assert.Single(fix.Inputs);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VeMapUsesPressureAndRpm()
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "ip_map_ve_1__map__n"));
        Assert.True(binding.HasInput, binding.Reason);
        var fix = Locate(document, binding, ("MAP", 3000), ("N", 2000));
        Assert.Equal(new AxisPosition(3, 63905 / 65536.0, 0), fix.Row);
        Assert.Equal(new AxisPosition(4, 63488 / 65536.0, 0), fix.Col);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void GearComesFromTheTopBitsOfTheRpmWord()
    {
        var document = Car();
        var curve = LiveTableBinding.Resolve(Map, document, Table(document, "id_n_max_mt__gear"));
        Assert.True(curve.HasInput, curve.Reason);
        Assert.Null(curve.CheckLogger(Ms43x));
        var fix = Locate(document, curve, ("N", 2480 | 3 << 13));
        Assert.Equal(new AxisPosition(3, 0, 0), fix.Row);
        Assert.Equal(3, fix.Inputs[0].Input);

        var boost = LiveTableBinding.Resolve(Map, document, Table(document, "ip_bc_map_req_ron98__gear__n"));
        Assert.True(boost.HasInput, boost.Reason);
        var both = Locate(document, boost, ("N", 2480 | 3 << 13));
        Assert.Equal(new AxisPosition(3, 0, 0), both.Row);
        Assert.Equal(3, both.Inputs[0].Input);
        Assert.Equal(2480, both.Inputs[1].Input);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AxisTableShowsItsInput()
    {
        var document = Car();
        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "ldpm_n_14"));
        Assert.True(binding.HasInput, binding.Reason);
        Assert.Equal(new AxisPosition(5, 61166 / 65536.0, 0), Locate(document, binding, ("N", 2480)).Row);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void EditedAxisMovesTheCursor()
    {
        var document = Car();
        var axis = Table(document, "ldpm_n_14");
        new EditHistory(document).Apply(TableEdits.Set(document, axis, new CellRange(5, 0, 5, 0), 2300), "test");

        var binding = LiveTableBinding.Resolve(Map, document, Table(document, "ip_iga_ron98_pl__n__maf"));
        Assert.Equal(new AxisPosition(5, 58982 / 65536.0, 0), Locate(document, binding, ("N", 2480), ("MAFIGA", 778)).Row);
    }

    [RealFileFact(TestData.CarBin, TestData.StockBin, TestData.Xdf)]
    public void NoLiveInputReasons()
    {
        var document = Car();
        var unlogged = LiveTableBinding.Resolve(Map, document, Table(document, "id_lam_tco_min__tco_st"));
        Assert.False(unlogged.HasInput);
        Assert.EndsWith("ECU variable 8450, which the logger does not read.", unlogged.Reason);

        var stock = CalibrationDocument.Open(TestData.Find(TestData.StockBin)!, TestData.Find(TestData.Xdf)!);
        var noMap = LiveTableBinding.Resolve(LiveInputMap.LoadBundled(stock.Firmware.SoftwareId), stock, Table(stock, "ip_iga_ron98_pl__n__maf"));
        Assert.Equal("No live input: there is no input map for firmware 430069.", noMap.Reason);

        var ron98 = LiveTableBinding.Resolve(Map, document, Table(document, "ip_iga_ron98_pl__n__maf"));
        Assert.StartsWith("No live input: the logger definition", ron98.CheckLogger(BundledDefinitions.Load("b25")));
        Assert.Equal("Live input: N (rpm) on rows, MAFIGA (mg/stk) on columns.", ron98.Describe());
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CoverageOfTheCarXdf()
    {
        var document = Car();
        var bound = document.Xdf.Tables.Select(table => LiveTableBinding.Resolve(Map, document, table)).Where(b => b.HasInput).Select(b => b.Table.Title).ToHashSet();
        Assert.True(bound.Count >= 380, $"{bound.Count} tables have a live input");
        Assert.Contains("ip_iga_ron98_pl__n__maf", bound);
        Assert.Contains("ip_af_target_ron98__maf__n", bound);
        for (var k = 1; k <= 8; k++)
        {
            Assert.Contains($"ip_map_ve_{k}__map__n", bound);
        }
    }
}
