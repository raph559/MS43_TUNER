using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class CalibrationReaderTests
{
    private const string SyntheticXdf = """
        <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
          <XDFTABLE uniqueid="0x10"><title>direct</title>
            <XDFAXIS id="x" uniqueid="0x0"><EMBEDDEDDATA mmedelementsizebits="8" mmedmajorstridebits="-32" mmedminorstridebits="0" />
              <indexcount>1</indexcount><LABEL index="0" value="a" /><MATH equation="X"><VAR id="X" /></MATH></XDFAXIS>
            <XDFAXIS id="y" uniqueid="0x0"><EMBEDDEDDATA mmedaddress="0x7600" mmedelementsizebits="8" />
              <units>rpm</units><indexcount>2</indexcount><MATH equation="32*X"><VAR id="X" /></MATH></XDFAXIS>
            <XDFAXIS id="z"><EMBEDDEDDATA mmedaddress="0x7500" mmedelementsizebits="8" mmedrowcount="2" />
              <decimalpl>1</decimalpl><MATH equation="0.5*X"><VAR id="X" /></MATH></XDFAXIS>
          </XDFTABLE>
          <XDFTABLE uniqueid="0x11"><title>edge</title>
            <XDFAXIS id="z"><EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0xFFFF" mmedelementsizebits="16" />
              <MATH equation="X"><VAR id="X" /></MATH></XDFAXIS>
          </XDFTABLE>
        </XDFFORMAT>
        """;

    private static (XdfDefinition Xdf, CalibrationReader Reader) Synthetic()
    {
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(SyntheticXdf)), "test.xdf");
        var bytes = new byte[BinImage.Size];
        bytes[0x77600] = 25;
        bytes[0x77601] = 26;
        bytes[0x77500] = 3;
        bytes[0x77501] = 4;
        return (xdf, new CalibrationReader(xdf, BinImage.FromBytes(bytes)));
    }

    private static readonly Lazy<(XdfDefinition Xdf, CalibrationReader Reader)> Car = new(() =>
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        return (xdf, new CalibrationReader(xdf, BinImage.Load(TestData.Find(TestData.CarBin)!)));
    });

    private static TableValues CarTable(string title) => Car.Value.Reader.ReadTable((XdfTable)Car.Value.Xdf.FindByTitle(title)!);

    private static ConstantValue CarConstant(string title) => Car.Value.Reader.ReadConstant((XdfConstant)Car.Value.Xdf.FindByTitle(title)!);

    [Fact]
    public void AxisWithItsOwnAddressAndLabelAxis()
    {
        var (xdf, reader) = Synthetic();
        var values = reader.ReadTable(xdf.Tables[0]);

        Assert.Equal(2, values.Rows);
        Assert.Equal(1, values.Cols);
        Assert.Equal(new[] { 800.0, 832.0 }, values.Y.Values);
        Assert.Equal("rpm", values.Y.Units);
        Assert.Null(values.Y.Name);
        Assert.Equal(new[] { "a" }, values.X.Labels);
        Assert.True(double.IsNaN(values.X.Values[0]));
        Assert.Equal(1.5, values.Physical[0, 0]);
        Assert.Equal(4, values.Raw[1, 0]);
    }

    [Fact]
    public void DataPastTheEndIsListedAndNotRead()
    {
        var (xdf, reader) = Synthetic();

        Assert.Equal(new[] { "edge" }, reader.FindOutOfRangeItems());
        var error = Assert.Throws<InvalidDataException>(() => reader.ReadTable(xdf.Tables[1]));
        Assert.Contains("edge", error.Message);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void PartLoadIgnitionMap()
    {
        var map = CarTable("ip_iga_ron98_pl__n__maf");

        Assert.Equal(20, map.Rows);
        Assert.Equal(16, map.Cols);
        Assert.Equal(new[] { 6.375, 6.375, 6.375, 6.375, 6.75, 8.25 }, Enumerable.Range(0, 6).Select(c => map.Physical[0, c]));
        Assert.All(Enumerable.Range(0, 16), c => Assert.Equal(-23.625, map.Physical[19, c]));
        Assert.Equal("ldpm_n_14", map.Y.Name);
        Assert.Equal(
            new[] { 800.0, 1000, 1250, 1500, 1750, 2200, 2500, 3000, 3250, 3750, 4000, 4250, 4500, 4750, 5000, 5500, 5900, 6480, 6980, 6990 },
            map.Y.Values);
        Assert.Equal("ldpm_maf_iga_2", map.X.Name);
        Assert.Equal(32.979, map.X.Values[0], 3);
        Assert.Equal(1, map.X.DecimalPlaces);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void RpmAxisLinkedTargetAndRevLimit()
    {
        var axis = CarTable("ldpm_n_11");
        Assert.Equal(
            new[] { 800.0, 1250, 1750, 2200, 2500, 3000, 3250, 3750, 4096, 4250, 4500, 5000, 5900, 6480, 6980, 6990 },
            Enumerable.Range(0, axis.Rows).Select(r => axis.Physical[r, 0]));

        Assert.Equal(14.7007, CarTable("ip_af_target_ron98__maf__n").Physical[0, 0], 4);

        var limit = CarTable("id_n_max_max_mt__gear");
        Assert.Equal(222, limit.Raw[0, 0]);
        Assert.Equal(7104, limit.Physical[0, 0]);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CarConstants()
    {
        Assert.Equal(1, CarConstant("c_conf_n_max").Physical);
        Assert.Equal(1, CarConstant("c_conf_lc").Physical);
        Assert.Equal(1, CarConstant("c_conf_nls").Physical);
        Assert.Equal(1, CarConstant("c_conf_load").Physical);
        Assert.Equal(0, CarConstant("c_conf_bc").Physical);
        Assert.Equal(225, CarConstant("c_inj_flow").Raw);
        Assert.Equal(new ConstantValue(141, 4512, "4512"), CarConstant("c_lc_n_max"));
        Assert.Equal(10, CarConstant("c_icl_sup_frq").Raw);
        Assert.Equal(5.0, CarConstant("c_icl_sup_frq").Physical);
        Assert.Equal("320.0", CarConstant("c_t_cp_1").Display);
        Assert.Equal("160", CarConstant("c_sum_a_ftp_min").Display);
        Assert.Equal("0x0014", CarConstant("c_dtc_ad_mec_ref_ivvt_ex_0").Display);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void KnockMilFlags()
    {
        var (xdf, reader) = Car.Value;
        bool Flag(string title) => reader.ReadFlag((XdfFlag)xdf.FindByTitle(title)!);

        Assert.False(Flag("lv_mil_ind_knk_lv_1"));
        Assert.True(Flag("lv_mil_ind_knk_lv_2"));
        Assert.False(Flag("lv_mil_ind_ff_err"));
        Assert.False(Flag("lv_mil_ind_ob_prot"));
        Assert.Empty(reader.FindOutOfRangeItems());
    }
}
