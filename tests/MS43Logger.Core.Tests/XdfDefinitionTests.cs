using System.Text;
using System.Xml;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class XdfDefinitionTests
{
    private const string LabelAxisX = """
        <XDFAXIS id="x" uniqueid="0x0">
          <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
          <indexcount>1</indexcount>
          <LABEL index="0" value="0.00" />
          <MATH equation="X"><VAR id="X" /></MATH>
        </XDFAXIS>
        """;

    private static XdfDefinition Parse(string body, string subtract = "0") =>
        XdfDefinition.Load(
            new MemoryStream(Encoding.UTF8.GetBytes($"""
                <XDFFORMAT version="1.80">
                  <XDFHEADER>
                    <deftitle>TEST</deftitle>
                    <BASEOFFSET offset="458752" subtract="{subtract}" />
                    <DEFAULTS datasizeinbits="8" sigdigits="2" outputtype="1" signed="0" lsbfirst="1" float="0" />
                    <CATEGORY index="0x84" name="Idle Speed" />
                  </XDFHEADER>
                  {body}
                </XDFFORMAT>
                """)),
            "test.xdf");

    private static string Table(string id, string title, string z, string x = LabelAxisX, string y = "", string zMath = "<MATH equation=\"X\"><VAR id=\"X\" /></MATH>") => $"""
        <XDFTABLE uniqueid="{id}" flags="0x30">
          <title>{title}</title>
          <CATEGORYMEM index="0" category="133" />
          {x}
          {(y.Length > 0 ? y : LabelAxisX.Replace("id=\"x\"", "id=\"y\""))}
          <XDFAXIS id="z">
            {z}
            <decimalpl>1</decimalpl>
            <min>0.000000</min>
            <max>22.000000</max>
            <outputtype>1</outputtype>
            {zMath}
          </XDFAXIS>
        </XDFTABLE>
        """;

    [Fact]
    public void BaseOffsetIsAddedToAddresses()
    {
        var xdf = Parse(Table("0x10", "t1", """<EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x7315" mmedelementsizebits="8" mmedrowcount="20" mmedcolcount="16" />"""));

        Assert.Equal("TEST", xdf.Title);
        Assert.Equal(0x70000, xdf.BaseOffset);
        var table = Assert.Single(xdf.Tables);
        Assert.Equal(0x77315, table.Z.Data.Address);
        Assert.Equal(20, table.Z.Data.Rows);
        Assert.Equal(16, table.Z.Data.Cols);
        Assert.Equal(320, table.Z.Data.Count);
        Assert.Equal(1, table.Z.DecimalPlaces);
        Assert.Equal(22.0, table.Z.Max);
    }

    [Fact]
    public void SubtractedBaseOffset()
    {
        var xdf = Parse(Table("0x10", "t1", """<EMBEDDEDDATA mmedaddress="0x77315" mmedelementsizebits="8" />"""), subtract: "1");

        Assert.Equal(-0x70000, xdf.BaseOffset);
        Assert.Equal(0x7315, xdf.Tables[0].Z.Data.Address);
    }

    [Fact]
    public void TypeFlagsGiveSignAndByteOrder()
    {
        var xdf = Parse("""
            <XDFCONSTANT uniqueid="0x1"><title>signed16</title>
              <EMBEDDEDDATA mmedtypeflags="0x03" mmedaddress="0xEAA" mmedelementsizebits="16" />
              <MATH equation="X"><VAR id="X" /></MATH></XDFCONSTANT>
            <XDFCONSTANT uniqueid="0x2"><title>noflags8</title>
              <EMBEDDEDDATA mmedaddress="0x132F" mmedelementsizebits="8" />
              <MATH equation="32*X"><VAR id="X" /></MATH></XDFCONSTANT>
            """);

        var signed = xdf.Constants[0].Data;
        Assert.True(signed.Signed);
        Assert.True(signed.LsbFirst);
        Assert.Equal(16, signed.ElementBits);
        Assert.True(signed.IsSupported);

        var plain = xdf.Constants[1].Data;
        Assert.False(plain.Signed);
        Assert.True(plain.LsbFirst);
    }

    [Fact]
    public void UnknownTypeFlagMakesDataUnsupported()
    {
        var xdf = Parse(Table("0x10", "colmajor", """<EMBEDDEDDATA mmedtypeflags="0x06" mmedaddress="0x7315" mmedelementsizebits="8" />"""));

        Assert.False(xdf.Tables[0].Z.Data.IsSupported);
        Assert.Contains("0x06", xdf.Tables[0].Z.Data.UnsupportedReason);
    }

    [Fact]
    public void NonZeroStrideOnAddressedDataIsUnsupported()
    {
        var xdf = Parse(Table("0x10", "strided", """<EMBEDDEDDATA mmedaddress="0x7315" mmedelementsizebits="8" mmedmajorstridebits="16" mmedminorstridebits="0" />"""));

        Assert.False(xdf.Tables[0].Z.Data.IsSupported);
        Assert.True(xdf.Tables[0].X.Data is null);
    }

    [Fact]
    public void RowsAndColsDefaultToOne()
    {
        var xdf = Parse(Table("0x10", "t1", """<EMBEDDEDDATA mmedaddress="0x7315" mmedelementsizebits="8" />"""));

        Assert.Equal(1, xdf.Tables[0].Z.Data.Rows);
        Assert.Equal(1, xdf.Tables[0].Z.Data.Cols);
    }

    [Fact]
    public void AxisLinkLabelsAndMissingLink()
    {
        const string linkedY = """
            <XDFAXIS id="y" uniqueid="0x0">
              <EMBEDDEDDATA mmedaddress="0x4B6E" mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <units>rpm</units>
              <indexcount>16</indexcount>
              <embedinfo type="3" linkobjid="0xC2F8" />
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
            """;
        const string brokenY = """
            <XDFAXIS id="y" uniqueid="0x0">
              <EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <indexcount>4</indexcount>
              <embedinfo type="3" />
              <MATH equation="X"><VAR id="X" /></MATH>
            </XDFAXIS>
            """;
        var xdf = Parse(
            Table("0x10", "linked", """<EMBEDDEDDATA mmedaddress="0x7315" mmedelementsizebits="8" mmedrowcount="16" />""", y: linkedY) +
            Table("0x11", "ip_ectpwm_i__tco_dif", """<EMBEDDEDDATA mmedaddress="0x7400" mmedelementsizebits="8" mmedrowcount="4" />""", y: brokenY) +
            Table("0xC2F8", "ldpm_n_11", """<EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x4B6E" mmedelementsizebits="16" mmedrowcount="16" />"""));

        var linked = xdf.Tables[0];
        Assert.Equal(0xC2F8, linked.Y.LinkedTableId);
        Assert.Equal("rpm", linked.Y.Units);
        Assert.Equal(16, linked.Y.Count);
        Assert.Equal(0x74B6E, linked.Y.Data!.Address);
        Assert.Equal(new[] { "0.00" }, linked.X.Labels);
        Assert.Null(linked.X.Data);

        Assert.Null(xdf.Tables[1].Y.LinkedTableId);
        Assert.Single(xdf.Warnings, warning => warning.Contains("ip_ectpwm_i__tco_dif", StringComparison.Ordinal));
    }

    [Fact]
    public void VariablesByKind()
    {
        var xdf = Parse("""
            <XDFCONSTANT uniqueid="0x1"><title>c</title>
              <EMBEDDEDDATA mmedaddress="0x850" mmedelementsizebits="16" />
              <MATH equation="0.1*X00+Y-Z"><VAR id="X00" /><VAR id="Y" type="link" linkid="0x3236" /><VAR id="Z" type="address" /></MATH></XDFCONSTANT>
            """);

        var variables = xdf.Constants[0].Math.Variables;
        Assert.Equal("0.1*X00+Y-Z", xdf.Constants[0].Math.Equation);
        Assert.Equal(new XdfVariable("X00", XdfVariableKind.Raw, null), variables[0]);
        Assert.Equal(new XdfVariable("Y", XdfVariableKind.Link, 0x3236), variables[1]);
        Assert.Equal(XdfVariableKind.Unsupported, variables[2].Kind);
    }

    [Fact]
    public void CategoriesUseIndexPlusOne()
    {
        var xdf = Parse(Table("0x10", "t1", """<EMBEDDEDDATA mmedaddress="0x7315" mmedelementsizebits="8" />"""));

        Assert.Equal("Idle Speed", xdf.Categories[133]);
        Assert.Equal(new[] { 133 }, xdf.Tables[0].CategoryIds);
    }

    [Fact]
    public void DuplicateIdKeepsFirstAndWarns()
    {
        var xdf = Parse(
            Table("0x2738", "first", """<EMBEDDEDDATA mmedaddress="0x7C84" mmedelementsizebits="8" />""") +
            Table("0x2738", "second", """<EMBEDDEDDATA mmedaddress="0xBEF4" mmedelementsizebits="8" />"""));

        Assert.Equal(2, xdf.Tables.Count);
        Assert.Equal("first", xdf.FindTableById(0x2738)!.Title);
        Assert.Contains(xdf.Warnings, warning => warning.Contains("0x2738", StringComparison.Ordinal));
        Assert.Equal("second", xdf.FindByTitle("SECOND")!.Title);
    }

    [Fact]
    public void FlagMaskIsParsed()
    {
        var xdf = Parse("""
            <XDFFLAG uniqueid="0xCBC8"><title>lv_mil_ind_knk_lv_2</title>
              <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x7D0" mmedelementsizebits="16" />
              <mask>0x02</mask></XDFFLAG>
            """);

        var flag = Assert.Single(xdf.Flags);
        Assert.Equal(2, flag.Mask);
        Assert.Equal(0x707D0, flag.Data.Address);
        Assert.Same(flag, xdf.FindByTitle("lv_mil_ind_knk_lv_2"));
    }

    [Fact]
    public void ConstantFieldsAreParsed()
    {
        var xdf = Parse("""
            <XDFCONSTANT uniqueid="0x661E" vislevel="10" flags="0xC"><title>c_dtc_isa_2_1</title>
              <description>An OBD code</description>
              <CATEGORYMEM index="0" category="133" />
              <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0x272" mmedelementsizebits="16" />
              <units>OBDII P Code</units><outputtype>3</outputtype><decimalpl>0</decimalpl>
              <rangelow>-5.5</rangelow><rangehigh>65535.000000</rangehigh>
              <MATH equation="1.0*X"><VAR id="X" /></MATH></XDFCONSTANT>
            """);

        var constant = Assert.Single(xdf.Constants);
        Assert.Equal(0x661E, constant.UniqueId);
        Assert.Equal("An OBD code", constant.Description);
        Assert.Equal("OBDII P Code", constant.Units);
        Assert.Equal(3, constant.OutputType);
        Assert.Equal(0, constant.DecimalPlaces);
        Assert.Equal(-5.5, constant.RangeLow);
        Assert.Equal(65535.0, constant.RangeHigh);
        Assert.Same(constant, xdf.FindConstantById(0x661E));
    }

    [Fact]
    public void DoctypeIsRefused()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE XDFFORMAT [<!ENTITY x "boom">]>
            <XDFFORMAT><XDFHEADER><deftitle>&x;</deftitle></XDFHEADER></XDFFORMAT>
            """;

        Assert.Throws<XmlException>(() => XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "evil.xdf"));
    }

    [Fact]
    public void MissingHeaderIsRefused()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes("<XDFFORMAT></XDFFORMAT>")), "empty.xdf"));

        Assert.Contains("XDF header is missing", error.Message);
    }
}
