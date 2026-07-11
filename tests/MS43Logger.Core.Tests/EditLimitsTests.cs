using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class EditLimitsTests
{
    private static readonly XdfVariable RawX = new("X", XdfVariableKind.Raw, null);

    private static (XdfDefinition Xdf, ValueCodec Codec) Car()
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        return (xdf, new ValueCodec(xdf, BinImage.Load(TestData.Find(TestData.CarBin)!)));
    }

    /// <summary>A codec over an empty definition, for equations of the raw value alone.</summary>
    private static ValueCodec Plain()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER></XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        return new ValueCodec(xdf, BinImage.FromBytes(new byte[BinImage.Size]));
    }

    private static XdfData Data(int bits, bool signed = false) => new(0x70000, bits, signed, true, 1, 1, null);

    private static XdfMath Math(string equation) => new(equation, new[] { RawX });

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void IgnitionMapLimitsComeFromItsByte()
    {
        var (xdf, codec) = Car();
        var z = ((XdfTable)xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!).Z;

        var limits = EditLimits.For(codec, z.Math, z.Data, z.Min, z.Max);

        Assert.Equal(-23.625, limits.StorableMin, 9);
        Assert.Equal(72.0, limits.StorableMax, 9);
        Assert.Equal(0.375, limits.Step!.Value, 9);
        // The XDF's -23.625 to 72 is the storable range itself, so it narrows nothing.
        Assert.Null(limits.XdfLow);
        Assert.Null(limits.XdfHigh);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void XdfEndsThatDoNotNarrowAreDropped()
    {
        var (xdf, codec) = Car();
        var constant = (XdfConstant)xdf.FindByTitle("c_lc_n_max")!;

        var limits = EditLimits.For(codec, constant.Math, constant.Data, constant.RangeLow, constant.RangeHigh);

        Assert.Equal(0, limits.StorableMin, 9);
        Assert.Equal(8160, limits.StorableMax, 9);
        Assert.Equal(32, limits.Step!.Value, 9);
        Assert.Null(limits.XdfLow);
        Assert.Null(limits.XdfHigh);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void XdfEndsThatNarrowAreKept()
    {
        var (xdf, codec) = Car();
        var constant = (XdfConstant)xdf.FindByTitle("c_n_max_er")!;

        var limits = EditLimits.For(codec, constant.Math, constant.Data, constant.RangeLow, constant.RangeHigh);

        Assert.Equal(0, limits.StorableMin, 9);
        Assert.Equal(65535, limits.StorableMax, 9);
        Assert.Equal(1, limits.Step!.Value, 9);
        Assert.Equal(480, limits.XdfLow);
        Assert.Equal(8160, limits.XdfHigh);
    }

    [Fact]
    public void FallingEquationSwapsTheEnds()
    {
        var limits = EditLimits.For(Plain(), Math("100-X"), Data(8), null, null);

        Assert.Equal(-155, limits.StorableMin, 9);
        Assert.Equal(100, limits.StorableMax, 9);
        Assert.Equal(1, limits.Step!.Value, 9);
    }

    [Fact]
    public void SignedBytesReachBelowZero()
    {
        var limits = EditLimits.For(Plain(), Math("0.5*X"), Data(8, signed: true), null, null);

        Assert.Equal(-64, limits.StorableMin, 9);
        Assert.Equal(63.5, limits.StorableMax, 9);
    }

    [Fact]
    public void CurvedEquationHasNoStepAndIsScannedForItsEnds()
    {
        // (X-100)^2 bottoms out inside the raw domain: its minimum is 0 at X = 100, not at either raw end.
        var limits = EditLimits.For(Plain(), Math("(X-100)*(X-100)"), Data(8), null, null);

        Assert.Equal(0, limits.StorableMin, 9);
        Assert.Equal(24025, limits.StorableMax, 9);
        Assert.Null(limits.Step);
    }
}
