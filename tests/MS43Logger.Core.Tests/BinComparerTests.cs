using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class BinComparerTests
{
    // Addresses are XDF addresses; the base offset puts them at 0x70000 + address in the image.
    private const string Body = """
        <XDFTABLE uniqueid="0x10" flags="0x30">
          <title>fuel_map</title>
          <CATEGORYMEM index="0" category="1" />
          <XDFAXIS id="x" uniqueid="0x0">
            <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
            <indexcount>3</indexcount>
            <LABEL index="0" value="10" /><LABEL index="1" value="20" /><LABEL index="2" value="30" />
            <MATH equation="X"><VAR id="X" /></MATH>
          </XDFAXIS>
          <XDFAXIS id="y" uniqueid="0x0">
            <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
            <units>rpm</units>
            <indexcount>2</indexcount>
            <embedinfo type="3" linkobjid="0x20" />
            <MATH equation="X"><VAR id="X" /></MATH>
          </XDFAXIS>
          <XDFAXIS id="z">
            <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x100" mmedelementsizebits="8" mmedrowcount="2" mmedcolcount="3" />
            <units>ms</units>
            <decimalpl>2</decimalpl>
            <outputtype>1</outputtype>
            <MATH equation="0.5*X"><VAR id="X" /></MATH>
          </XDFAXIS>
        </XDFTABLE>
        <XDFTABLE uniqueid="0x20" flags="0x30">
          <title>axis_rows</title>
          <CATEGORYMEM index="0" category="2" />
          <XDFAXIS id="x" uniqueid="0x0">
            <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
            <indexcount>1</indexcount>
            <LABEL index="0" value="0" />
            <MATH equation="X"><VAR id="X" /></MATH>
          </XDFAXIS>
          <XDFAXIS id="y" uniqueid="0x0">
            <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
            <indexcount>2</indexcount>
            <LABEL index="0" value="0" /><LABEL index="1" value="1" />
            <MATH equation="X"><VAR id="X" /></MATH>
          </XDFAXIS>
          <XDFAXIS id="z">
            <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x200" mmedelementsizebits="8" mmedrowcount="2" mmedcolcount="1" />
            <units>rpm</units>
            <decimalpl>0</decimalpl>
            <MATH equation="10*X"><VAR id="X" /></MATH>
          </XDFAXIS>
        </XDFTABLE>
        <XDFCONSTANT uniqueid="0x30"><title>c_value</title>
          <CATEGORYMEM index="0" category="1" />
          <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x300" mmedelementsizebits="16" />
          <units>rpm</units><decimalpl>0</decimalpl>
          <MATH equation="X"><VAR id="X" /></MATH></XDFCONSTANT>
        <XDFCONSTANT uniqueid="0x31"><title>c_switch</title>
          <description>Mode
        0: Off
        1: On</description>
          <CATEGORYMEM index="0" category="1" />
          <EMBEDDEDDATA mmedaddress="0x310" mmedelementsizebits="8" />
          <MATH equation="X"><VAR id="X" /></MATH></XDFCONSTANT>
        <XDFFLAG uniqueid="0x40"><title>flag_a</title>
          <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x320" mmedelementsizebits="16" />
          <mask>0x01</mask></XDFFLAG>
        <XDFFLAG uniqueid="0x41"><title>flag_b</title>
          <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x320" mmedelementsizebits="16" />
          <mask>0x02</mask></XDFFLAG>
        <XDFCONSTANT uniqueid="0x50"><title>c_odd</title>
          <CATEGORYMEM index="0" category="1" />
          <EMBEDDEDDATA mmedtypeflags="0x04" mmedaddress="0x400" mmedelementsizebits="8" />
          <MATH equation="X"><VAR id="X" /></MATH></XDFCONSTANT>
        """;

    internal static XdfDefinition Synthetic() =>
        XdfDefinition.Load(
            new MemoryStream(Encoding.UTF8.GetBytes($"""
                <XDFFORMAT version="1.80">
                  <XDFHEADER>
                    <deftitle>TEST</deftitle>
                    <BASEOFFSET offset="458752" subtract="0" />
                    <DEFAULTS datasizeinbits="8" sigdigits="2" outputtype="1" signed="0" lsbfirst="1" float="0" />
                    <CATEGORY index="0x0" name="Fuel" />
                    <CATEGORY index="0x1" name="Axis" />
                  </XDFHEADER>
                  {Body}
                </XDFFORMAT>
                """)),
            "test.xdf");

    internal static BinImage Blank() => BinImage.FromBytes(new byte[BinImage.Size]);

    internal static BinImage With(BinImage image, params (int Offset, byte Value)[] bytes)
    {
        var data = image.ToArray();
        foreach (var (offset, value) in bytes)
        {
            data[offset] = value;
        }

        return BinImage.FromBytes(data);
    }

    private static BinImage Base() =>
        With(Blank(), (0x70100, 10), (0x70101, 10), (0x70102, 10), (0x70103, 20), (0x70104, 20), (0x70105, 20), (0x70200, 0), (0x70201, 1));

    [Fact]
    public void IdenticalImagesHaveNoDifferences()
    {
        var comparison = BinComparer.Compare(Synthetic(), Base(), Base());

        Assert.True(comparison.IsEmpty);
        Assert.Empty(comparison.Items);
        Assert.Empty(comparison.Unmapped);
        Assert.Contains("No differences.", ComparisonText.Format(comparison, "a.bin", "b.bin", "test.xdf"));
    }

    [Fact]
    public void TableCellChangeIsCountedWithLargestChange()
    {
        var xdf = Synthetic();
        var a = Base();
        var b = With(a, (0x70101, 14), (0x70105, 10));

        var item = Assert.Single(BinComparer.Compare(xdf, a, b).Items);

        Assert.Equal("fuel_map", item.Item.Title);
        Assert.Equal(ItemKind.Map, item.Kind);
        Assert.Equal("Fuel", item.Category);
        Assert.Equal(2, item.ChangedCells);
        Assert.Equal(6, item.Cells);
        Assert.Equal(-5.0, item.LargestChange);
        Assert.Null(item.ValueA);
        Assert.Empty(item.Axes);

        var table = BinComparer.CompareTable(xdf, a, b, (XdfTable)item.Item);
        Assert.Equal(2.0, table.Delta.Physical[0, 1]);
        Assert.Equal(4, table.Delta.Raw[0, 1]);
        Assert.Equal(0.0, table.Delta.Physical[0, 0]);
        Assert.Equal(new HashSet<(int, int)> { (0, 1), (1, 2) }, table.Changed);
        Assert.Equal(5.0, table.A.Physical[0, 1]);
        Assert.Equal(7.0, table.B.Physical[0, 1]);
    }

    [Fact]
    public void ValuesAndSwitchesShowAToB()
    {
        var a = With(Base(), (0x70300, 0xC0), (0x70301, 0x11), (0x70310, 0));
        var b = With(a, (0x70300, 0xA0), (0x70301, 0x0D), (0x70310, 1));

        var items = BinComparer.Compare(Synthetic(), a, b).Items;

        var value = items.Single(item => item.Item.Title == "c_value");
        Assert.Equal(ItemKind.Value, value.Kind);
        Assert.Equal("4544", value.ValueA);
        Assert.Equal("3488", value.ValueB);
        Assert.Equal(-1056.0, value.LargestChange);
        Assert.Equal(1, value.ChangedCells);

        var choice = items.Single(item => item.Item.Title == "c_switch");
        Assert.Equal(ItemKind.Switch, choice.Kind);
        Assert.Equal("Off", choice.ValueA);
        Assert.Equal("On", choice.ValueB);
    }

    [Fact]
    public void FlagSharingAWordOnlyListsItsOwnBit()
    {
        var a = With(Base(), (0x70320, 0x01));
        var b = With(a, (0x70320, 0x03));

        var item = Assert.Single(BinComparer.Compare(Synthetic(), a, b).Items);

        Assert.Equal("flag_b", item.Item.Title);
        Assert.Equal(ItemKind.Flag, item.Kind);
        Assert.Equal("off", item.ValueA);
        Assert.Equal("on", item.ValueB);
        Assert.Equal(XdfCatalog.OtherCategory, item.Category);
    }

    [Fact]
    public void UnsupportedDataIsComparedOnRawValues()
    {
        var item = Assert.Single(BinComparer.Compare(Synthetic(), Base(), With(Base(), (0x70400, 7))).Items);

        Assert.Equal("c_odd", item.Item.Title);
        Assert.Null(item.LargestChange);
    }

    [Fact]
    public void BytesOutsideItemsAreAreasSplitAtBoundaries()
    {
        var b = With(Base(), (0x3FFE, 1), (0x3FFF, 1), (0x4000, 1), (0x4001, 1), (0x70300, 5));

        var comparison = BinComparer.Compare(Synthetic(), Base(), b);

        Assert.Equal("c_value", Assert.Single(comparison.Items).Item.Title);
        Assert.Equal(new[] { new UnmappedRange(0x3FFE, 2, 2, "Boot block"), new UnmappedRange(0x4000, 2, 2, "Unused") }, comparison.Unmapped);
        Assert.Equal(4, comparison.UnmappedBytes);
        Assert.Equal(4, comparison.DifferingBytes);
        Assert.Equal(0x3FFF, comparison.Unmapped[0].End);
        Assert.False(comparison.IsEmpty);
    }

    [Fact]
    public void NearbyRangesMergeAcrossUpToEightEqualBytes()
    {
        var b = With(Base(), (0x3C00, 1), (0x3C09, 1), (0x3C20, 1), (0x3C2A, 1), (0x702FE, 1), (0x70303, 1));

        var comparison = BinComparer.Compare(Synthetic(), Base(), b);

        Assert.Equal(
            new[]
            {
                new UnmappedRange(0x3C00, 10, 2, "Boot block"),
                new UnmappedRange(0x3C20, 1, 1, "Boot block"),
                new UnmappedRange(0x3C2A, 1, 1, "Boot block"),
                new UnmappedRange(0x702FE, 1, 1, "Calibration"),
                new UnmappedRange(0x70303, 1, 1, "Calibration"),
            },
            comparison.Unmapped);
        Assert.Equal(14, comparison.UnmappedBytes);
        Assert.Equal(6, comparison.DifferingBytes);
        Assert.Contains("10 bytes  Boot block (2 differ)", ComparisonText.Format(comparison, "a", "b", "x"));
    }

    [Theory]
    [InlineData(0x00000, "Boot block")]
    [InlineData(0x04000, "Unused")]
    [InlineData(0x08000, "Service sector")]
    [InlineData(0x0BFFF, "Service sector")]
    [InlineData(0x0C000, "Unused")]
    [InlineData(0x10000, "Program")]
    [InlineData(0x4FFE6, "Program")]
    [InlineData(0x6C000, "Program constants")]
    [InlineData(0x70000, "Calibration")]
    [InlineData(0x7FFFF, "Calibration")]
    public void AreaNames(int offset, string area) => Assert.Equal(area, BinComparer.AreaName(offset));

    [Fact]
    public void AxisDifferenceIsNamed()
    {
        var b = With(Base(), (0x70201, 3), (0x70100, 12));

        var items = BinComparer.Compare(Synthetic(), Base(), b).Items;

        Assert.Equal(new[] { "axis_rows", "fuel_map" }, items.Select(item => item.Item.Title));
        Assert.Equal(ItemKind.Axis, items[0].Kind);
        Assert.Equal("Axis", items[0].Category);
        var axis = Assert.Single(items[1].Axes);
        Assert.Equal("row", axis.Direction);
        Assert.Equal("The row axis differs (axis_rows, rpm): A runs 0 to 10, B 0 to 30, so cells are compared position by position.", axis.Describe());
    }

    [Fact]
    public void FormatDeltaSignsAndZero()
    {
        var z = Synthetic().Tables[0].Z;

        Assert.Equal("+1.50", BinComparer.FormatDelta(1.5, 3, z, raw: false));
        Assert.Equal("-0.38", BinComparer.FormatDelta(-0.375, -1, z, raw: false));
        Assert.Equal("0.00", BinComparer.FormatDelta(0, 0, z, raw: false));
        Assert.Equal("n/a", BinComparer.FormatDelta(double.NaN, 0, z, raw: false));
        Assert.Equal("+3", BinComparer.FormatDelta(2.6, 5, z with { OutputType = 2 }, raw: false));
        Assert.Equal("+4", BinComparer.FormatDelta(2, 4, z, raw: true));
        Assert.Equal("0", BinComparer.FormatDelta(0, 0, z, raw: true));
        Assert.Equal("-4", BinComparer.FormatDelta(-2, -4, z with { OutputType = 3 }, raw: false));
    }

    [Fact]
    public void TextListsCategoriesItemsAndRanges()
    {
        var b = With(Base(), (0x70101, 14), (0x3C43, 9));
        var text = ComparisonText.Format(BinComparer.Compare(Synthetic(), Base(), b), "a.bin", "b.bin", "test.xdf");

        Assert.Contains("A: a.bin", text);
        Assert.Contains("B: b.bin", text);
        Assert.Contains("1 item differs, 1 byte range (1 byte) outside the XDF", text);
        Assert.Contains("\nFuel\n", text);
        Assert.Contains("fuel_map", text);
        Assert.Contains("1 of 6 cells", text);
        Assert.Contains("+2.00 ms", text);
        Assert.Contains("Bytes outside the XDF", text);
        Assert.Contains("0x03C43 to 0x03C43", text);
        Assert.DoesNotContain("differ)", text);
        Assert.Contains("Boot block", text);
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void CarVersusReleaseListsTheKnownDifferences()
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        var comparison = BinComparer.Compare(xdf, BinImage.Load(TestData.Find(TestData.CarBin)!), BinImage.Load(TestData.Find(TestData.ReleaseBin)!));
        var byTitle = comparison.Items.ToDictionary(item => item.Item.Title);

        foreach (var name in new[] { "c_conf_n_max", "c_conf_lc", "c_conf_nls" })
        {
            Assert.Equal(ItemKind.Switch, byTitle[name].Kind);
            Assert.NotEqual(byTitle[name].ValueA, byTitle[name].ValueB);
        }

        Assert.False(byTitle.ContainsKey("c_conf_load"));
        Assert.False(byTitle.ContainsKey("c_conf_bc"));
        Assert.StartsWith("225", byTitle["c_inj_flow"].ValueA);
        Assert.StartsWith("237", byTitle["c_inj_flow"].ValueB);
        Assert.StartsWith("4512", byTitle["c_lc_n_max"].ValueA);

        var ignition = byTitle["ip_iga_ron98_pl__n__maf"];
        var rows = Assert.Single(ignition.Axes, axis => axis.Direction == "row");
        Assert.Equal(ItemKind.Axis, byTitle[rows.A.Name!].Kind);

        var outside = comparison.Unmapped.Where(range => range.Offset < 0x70000).ToList();
        Assert.Equal(18, outside.Count);
        Assert.Equal(11907, outside.Sum(range => range.Length));
        Assert.Contains(outside, range => range.Area == "Boot block");
        Assert.Contains(outside, range => range.Area == "Service sector");
        Assert.Contains(outside, range => range.Area == "Program");

        var covered = xdf.Tables.Select(table => table.Z.Data).Concat(xdf.Constants.Select(c => c.Data)).Concat(xdf.Flags.Select(f => f.Data))
            .Where(data => data.Address is not null)
            .Select(data => (Start: BinImage.ToOffset(data.Address!.Value), End: BinImage.ToOffset(data.Address!.Value) + data.Count * data.ElementBytes))
            .ToList();
        Assert.DoesNotContain(comparison.Unmapped, range => covered.Any(item => range.Offset < item.End && range.Offset + range.Length > item.Start));
    }

    [RealFileFact(TestData.CarBin, TestData.ReleaseBin, TestData.Xdf)]
    public void CarVersusReleaseTextHasEveryListedItem()
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        var comparison = BinComparer.Compare(xdf, BinImage.Load(TestData.Find(TestData.CarBin)!), BinImage.Load(TestData.Find(TestData.ReleaseBin)!));

        var text = ComparisonText.Format(comparison, TestData.CarBin, TestData.ReleaseBin, TestData.Xdf);

        Assert.Contains($"{comparison.Items.Count} items differ", text);
        Assert.Contains("18 byte ranges (11907 bytes) outside the XDF", text);
        Assert.All(comparison.Items, item => Assert.Contains(item.Item.Title, text));
    }
}
