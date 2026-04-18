using System.Globalization;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class EditHistoryTests
{
    private static CalibrationDocument Open() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static XdfTable Ignition(CalibrationDocument d) => (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ApplyUndoRedoRestoresBytes()
    {
        var d = Open();
        var history = new EditHistory(d);
        var original = d.Image.ToArray();
        var changed = 0;
        d.Changed += (_, _) => changed++;

        history.Apply(TableEdits.Add(d, Ignition(d), new CellRange(0, 0, 3, 3), 2), "Add 2 °CRK");
        var edited = d.Image.ToArray();

        Assert.True(d.IsModified);
        Assert.Equal("Add 2 °CRK", history.NextUndoLabel);
        Assert.True(history.Undo());
        Assert.Equal(original, d.Image.ToArray());
        Assert.False(d.IsModified);
        Assert.True(history.CanRedo);
        Assert.True(history.Redo());
        Assert.Equal(edited, d.Image.ToArray());
        Assert.Equal(3, changed);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ConstantsAndFlagsUndoToo()
    {
        var d = Open();
        var history = new EditHistory(d);
        var original = d.Image.ToArray();

        history.Apply(TableEdits.SetConstant(d, (XdfConstant)d.Xdf.FindByTitle("c_lc_n_max")!, 4000), "Launch limit");
        history.Apply(TableEdits.SetFlag(d, (XdfFlag)d.Xdf.FindByTitle("lv_mil_ind_knk_lv_1")!, true), "Knock MIL");

        Assert.Equal(2, history.UndoCount);
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.Equal(original, d.Image.ToArray());
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void NewEditClearsRedoAndEmptyPlansAreSkipped()
    {
        var d = Open();
        var history = new EditHistory(d);

        history.Apply(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1), "Step");
        history.Undo();
        history.Apply(TableEdits.Step(d, Ignition(d), new CellRange(1, 1, 1, 1), 1), "Step");
        history.Apply(TableEdits.SetFlag(d, (XdfFlag)d.Xdf.FindByTitle("lv_mil_ind_knk_lv_2")!, true), "No change");

        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void UndoOnEmptyReturnsFalse()
    {
        var history = new EditHistory(CalibrationDocument.FromParts(
            XdfDefinition.Load(new MemoryStream("<XDFFORMAT><XDFHEADER><deftitle>T</deftitle></XDFHEADER></XDFFORMAT>"u8.ToArray()), "t.xdf"),
            BinImage.FromBytes(new byte[BinImage.Size]),
            null));

        Assert.False(history.Undo());
        Assert.False(history.Redo());
        Assert.Null(history.NextUndoLabel);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void FormatThenParseRoundTrips()
    {
        var d = Open();
        var values = d.Reader.ReadTable(Ignition(d));

        var text = TableClipboard.Format(values, new CellRange(18, 0, 19, 2));

        Assert.Equal("31.5\t31.5\t34.5\r\n-23.625\t-23.625\t-23.625", text);
        Assert.Equal(new double[,] { { 31.5, 31.5, 34.5 }, { -23.625, -23.625, -23.625 } }, TableClipboard.Parse(text));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CopyPasteInPlaceKeepsEveryTableIdentical()
    {
        var d = Open();
        var changedTables = new List<string>();
        foreach (var table in d.Xdf.Tables.Where(t => t.Z.Data.IsSupported && d.Image.Contains(t.Z.Data)))
        {
            var values = d.Reader.ReadTable(table);
            if (values.Physical.Cast<double>().Any(v => !double.IsFinite(v)))
            {
                continue;
            }

            var text = TableClipboard.Format(values, new CellRange(0, 0, values.Rows - 1, values.Cols - 1));
            if (TableEdits.Paste(d, table, 0, 0, TableClipboard.Parse(text)).Changes.Count > 0)
            {
                changedTables.Add(table.Title);
            }
        }

        Assert.Empty(changedTables);
    }

    [Fact]
    public void ParseFrenchSpreadsheetText()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Equal(new double[,] { { 12.5, 13 }, { 14.5, 15 } }, TableClipboard.Parse("12,5\t13,0\n14,5\t15,0\n"));
            Assert.Equal(new double[,] { { 1.5, 2 }, { -3, 4.25 } }, TableClipboard.Parse("1.5;2\r\n−3 4.25"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void RaggedPasteIsRefused()
    {
        Assert.Throws<FormatException>(() => TableClipboard.Parse("1\t2\n3"));
        Assert.Throws<FormatException>(() => TableClipboard.Parse("1\tabc"));
        Assert.Throws<FormatException>(() => TableClipboard.Parse("   "));
    }
}
