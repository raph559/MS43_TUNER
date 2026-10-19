using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class TableEditsTests
{
    private static readonly Lazy<CalibrationDocument> Car = new(Open);

    private static CalibrationDocument Open() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static XdfTable Ignition(CalibrationDocument d) => (XdfTable)d.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;

    private static CellChange Only(EditPlan plan) => Assert.Single(plan.Changes);

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AddSnapsToRawSteps()
    {
        var d = Car.Value;

        var change = Only(TableEdits.Add(d, Ignition(d), new CellRange(12, 0, 12, 0), -10));

        Assert.Equal(new CellChange(12, 0, 147, 120), change);
        Assert.Equal(21.375, d.Reader.Codec.ToPhysical(Ignition(d).Z.Math, change.NewRaw));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SetFillsTheRangeAndSkipsUnchangedCells()
    {
        var d = Car.Value;

        var plan = TableEdits.Set(d, Ignition(d), new CellRange(1, 1, 0, 0), 10.0);

        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes, change => Assert.Equal(90, change.NewRaw));
        Assert.All(plan.Changes, change => Assert.Equal(0, change.Row));
        Assert.Empty(plan.Warnings);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ScaleByPercent()
    {
        var d = Car.Value;

        Assert.Equal(new CellChange(6, 0, 120, 126), Only(TableEdits.Scale(d, Ignition(d), new CellRange(6, 0, 6, 0), 10)));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void InterpolateBetweenCorners()
    {
        var d = Car.Value;
        var map = Ignition(d);
        var values = d.Reader.ReadTable(map);
        var range = new CellRange(0, 0, 2, 2);

        var plan = TableEdits.Interpolate(d, map, range);

        double P(int r, int c) => values.Physical[r, c];
        Assert.DoesNotContain(plan.Changes, change => (change.Row, change.Col) is (0, 0) or (0, 2) or (2, 0) or (2, 2));
        for (var r = 0; r <= 2; r++)
        {
            for (var c = 0; c <= 2; c++)
            {
                if (r is 0 or 2 && c is 0 or 2)
                {
                    continue;
                }

                var u = c / 2.0;
                var w = r / 2.0;
                var expected = (P(0, 0) * (1 - u) + P(0, 2) * u) * (1 - w) + (P(2, 0) * (1 - u) + P(2, 2) * u) * w;
                var raw = d.Reader.Codec.ToRaw(map.Z.Math, map.Z.Data, expected);
                var change = plan.Changes.SingleOrDefault(x => x.Row == r && x.Col == c);
                Assert.Equal(raw, change?.NewRaw ?? values.Raw[r, c]);
            }
        }
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SmoothIsThreeByThreeMean()
    {
        var d = Car.Value;
        var map = Ignition(d);
        var values = d.Reader.ReadTable(map);

        var plan = TableEdits.Smooth(d, map, new CellRange(0, 0, 4, 4));

        long Expected(int r, int c)
        {
            var neighbours = new List<double>();
            for (var i = r - 1; i <= r + 1; i++)
            {
                for (var j = c - 1; j <= c + 1; j++)
                {
                    if (i >= 0 && i < values.Rows && j >= 0 && j < values.Cols)
                    {
                        neighbours.Add(values.Physical[i, j]);
                    }
                }
            }

            return d.Reader.Codec.ToRaw(map.Z.Math, map.Z.Data, neighbours.Average());
        }

        long After(int r, int c) => plan.Changes.SingleOrDefault(x => x.Row == r && x.Col == c)?.NewRaw ?? values.Raw[r, c];
        Assert.Equal(Expected(2, 2), After(2, 2));
        Assert.Equal(Expected(0, 0), After(0, 0));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void StepMovesRawCounts()
    {
        var d = Car.Value;

        Assert.Equal(new CellChange(0, 0, 80, 81), Only(TableEdits.Step(d, Ignition(d), new CellRange(0, 0, 0, 0), 1)));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void PasteOutsideTableIsRefused()
    {
        var d = Car.Value;

        Assert.Throws<EditRefusedException>(() => TableEdits.Paste(d, Ignition(d), 19, 15, new double[2, 2]));
        Assert.Equal(4, TableEdits.Paste(d, Ignition(d), 0, 0, new double[,] { { 0, 0 }, { 0, 0 } }).Changes.Count(c => c.Row < 2 && c.Col < 2));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AxisMustNotDecrease()
    {
        var d = Car.Value;
        var axis = (XdfTable)d.Xdf.FindByTitle("ldpm_n_14")!;

        var error = Assert.Throws<EditRefusedException>(() => TableEdits.Set(d, axis, new CellRange(3, 0, 3, 0), 1100));
        Assert.Contains("ldpm_n_14", error.Message);

        var equal = TableEdits.Set(d, axis, new CellRange(3, 0, 3, 0), 1250);
        Assert.Single(equal.Changes);
        Assert.Single(equal.Warnings);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void SetConstantAndFlag()
    {
        var d = Car.Value;

        Assert.Equal(new CellChange(0, 0, 141, 125), Only(TableEdits.SetConstant(d, (XdfConstant)d.Xdf.FindByTitle("c_lc_n_max")!, 4000)));
        Assert.Equal(new CellChange(0, 0, 2, 3), Only(TableEdits.SetFlag(d, (XdfFlag)d.Xdf.FindByTitle("lv_mil_ind_knk_lv_1")!, true)));
        Assert.Empty(TableEdits.SetFlag(d, (XdfFlag)d.Xdf.FindByTitle("lv_mil_ind_knk_lv_2")!, true).Changes);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void ClampedValuesWarn()
    {
        var d = Car.Value;

        var constant = TableEdits.SetConstant(d, (XdfConstant)d.Xdf.FindByTitle("c_lc_n_max")!, 99999);
        Assert.Equal(255, Assert.Single(constant.Changes).NewRaw);
        Assert.Contains(constant.Warnings, w => w.Contains("8160", StringComparison.Ordinal));

        var map = TableEdits.Set(d, Ignition(d), new CellRange(0, 0, 0, 0), 200);
        Assert.Contains(map.Warnings, w => w.Contains("72", StringComparison.Ordinal));

        Assert.Empty(TableEdits.Set(d, Ignition(d), new CellRange(0, 0, 0, 0), 10).Warnings);
    }

    [Fact]
    public void OutsideTheXdfRangeWarns()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFCONSTANT uniqueid="0x20"><title>limited</title>
                <EMBEDDEDDATA mmedaddress="0x7600" mmedelementsizebits="8" /><rangelow>10</rangelow><rangehigh>100</rangehigh>
                <MATH equation="X"><VAR id="X" /></MATH></XDFCONSTANT>
            </XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        var d = CalibrationDocument.FromParts(xdf, BinImage.FromBytes(new byte[BinImage.Size]), null);

        Assert.Contains(TableEdits.SetConstant(d, xdf.Constants[0], 150).Warnings, w => w.Contains("100", StringComparison.Ordinal));
        Assert.Empty(TableEdits.SetConstant(d, xdf.Constants[0], 50).Warnings);
    }

    [Fact]
    public void SetConstantRawWritesTheRawValue()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFCONSTANT uniqueid="0x20"><title>scaled_switch</title>
                <EMBEDDEDDATA mmedaddress="0x7600" mmedelementsizebits="8" />
                <MATH equation="2*X"><VAR id="X" /></MATH></XDFCONSTANT>
            </XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        var d = CalibrationDocument.FromParts(xdf, BinImage.FromBytes(new byte[BinImage.Size]), null);

        Assert.Equal(1, Assert.Single(TableEdits.SetConstantRaw(d, xdf.Constants[0], 1).Changes).NewRaw);
    }

    [Fact]
    public void ReadOnlyAndNonFiniteItemsAreRefused()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFTABLE uniqueid="0x10"><title>colmajor</title>
                <XDFAXIS id="z"><EMBEDDEDDATA mmedtypeflags="0x06" mmedaddress="0x7500" mmedelementsizebits="8" mmedrowcount="2" />
                <MATH equation="X"><VAR id="X" /></MATH></XDFAXIS></XDFTABLE>
              <XDFCONSTANT uniqueid="0x20"><title>odd</title>
                <EMBEDDEDDATA mmedaddress="0x7600" mmedelementsizebits="8" />
                <MATH equation="X+Z"><VAR id="X" /><VAR id="Z" type="address" /></MATH></XDFCONSTANT>
            </XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        var d = CalibrationDocument.FromParts(xdf, BinImage.FromBytes(new byte[BinImage.Size]), null);

        Assert.Contains("colmajor", Assert.Throws<EditRefusedException>(() => TableEdits.Set(d, xdf.Tables[0], new CellRange(0, 0, 0, 0), 1)).Message);
        Assert.Contains("odd", Assert.Throws<EditRefusedException>(() => TableEdits.SetConstant(d, xdf.Constants[0], 1)).Message);
    }
}
