using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class TableTextFormatterTests
{
    private static readonly XdfMath RawMath = new("X", new[] { new XdfVariable("X", XdfVariableKind.Raw, null) });

    private static TableValues Sample()
    {
        var data = new XdfData(0x77315, 8, false, true, 2, 3, null);
        var noAxis = new XdfAxis("x", string.Empty, 3, null, null, Array.Empty<string>(), RawMath, null);
        var table = new XdfTable(1, "t1", string.Empty, Array.Empty<int>(), noAxis, noAxis with { Id = "y", Count = 2 }, new XdfZ(data, RawMath, "°CRK", 1, null, null, null));
        var raw = new long[,] { { 3, 5, 7 }, { 9, 11, 13 } };
        var physical = new double[,] { { 1.5, 2.5, 3.5 }, { 4.5, 5.5, 6.5 } };
        return new TableValues(
            table,
            2,
            3,
            raw,
            physical,
            new AxisValues(null, "mg/stk", new[] { 10.0, 20, 30 }, new[] { "10.0", "20.0", "30.0" }, 1),
            new AxisValues(null, "rpm", new[] { 800.0, 1000 }, new[] { "800", "1000" }, 0));
    }

    [Fact]
    public void FormatsTableWithAxes()
    {
        var expected = string.Join(
            "\n",
            "t1  0x77315  2 × 3  °CRK",
            "rpm \\ mg/stk   10.0   20.0   30.0",
            "800             1.5    2.5    3.5",
            "1000            4.5    5.5    6.5");

        Assert.Equal(expected, TableTextFormatter.Format(Sample(), raw: false));
    }

    [Fact]
    public void RawModePrintsRawValues()
    {
        var lines = TableTextFormatter.Format(Sample(), raw: true).Split('\n');

        // Label column 12 wide ("rpm \ mg/stk"), value columns 7 wide (widest "10.0" + 3).
        Assert.Equal("800".PadRight(12) + "3".PadLeft(7) + "5".PadLeft(7) + "7".PadLeft(7), lines[2]);
        Assert.Equal("1000".PadRight(12) + "9".PadLeft(7) + "11".PadLeft(7) + "13".PadLeft(7), lines[3]);
    }

    [Fact]
    public void ConstantLine()
    {
        var constant = new XdfConstant(2, "c_lc_n_max", string.Empty, Array.Empty<int>(), new XdfData(0x7132F, 8, false, true, 1, 1, null), RawMath, string.Empty, 0, null, null, null);

        Assert.Equal("c_lc_n_max = 4512 (raw 141)", TableTextFormatter.FormatConstant(constant, new ConstantValue(141, 4512, "4512")));
        Assert.Equal(
            "c_lc_n_max = 4512 rpm (raw 141)",
            TableTextFormatter.FormatConstant(constant with { Units = "rpm" }, new ConstantValue(141, 4512, "4512")));
    }
}
