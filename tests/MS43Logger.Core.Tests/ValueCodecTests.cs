using System.Globalization;
using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class ValueCodecTests
{
    private static readonly XdfVariable RawX = new("X", XdfVariableKind.Raw, null);

    private static XdfData Data(int bits, bool signed = false) => new(0x70000, bits, signed, true, 1, 1, null);

    private static XdfMath Math(string equation, params XdfVariable[] variables) =>
        new(equation, variables.Length == 0 ? new[] { RawX } : variables);

    /// <summary>A definition holding one constant <c>0.1 * X</c> (id 0x3236) at 0x70E11, and an image where it reads 147.</summary>
    private static ValueCodec Codec()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFCONSTANT uniqueid="0x3236"><title>c_af_stoich_ron98</title>
                <EMBEDDEDDATA mmedtypeflags="0x02" mmedaddress="0xE11" mmedelementsizebits="8" />
                <MATH equation="0.1 * X"><VAR id="X" /></MATH></XDFCONSTANT>
            </XDFFORMAT>
            """;
        var xdf = XdfDefinition.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test.xdf");
        var bytes = new byte[BinImage.Size];
        bytes[0x70E11] = 147;
        return new ValueCodec(xdf, BinImage.FromBytes(bytes));
    }

    [Fact]
    public void LinearBothWays()
    {
        var codec = Codec();
        var ignition = Math("0.375*X-23.625");

        Assert.Equal(-23.625, codec.ToPhysical(ignition, 0));
        Assert.Equal(36, codec.ToRaw(ignition, Data(8), -10));
        Assert.Equal(255, codec.ToRaw(ignition, Data(8), 1000));
        Assert.Equal(0, codec.ToRaw(ignition, Data(8), -1000));
        Assert.Equal(4000, codec.ToRaw(Math("0.25*X"), Data(16), 1000));
    }

    [Fact]
    public void VariableIdsAreCaseInsensitiveAndAnyRawName()
    {
        var codec = Codec();

        Assert.Equal(7, codec.ToPhysical(Math("x", new XdfVariable("X", XdfVariableKind.Raw, null)), 7));
        Assert.Equal(320.0, codec.ToPhysical(Math("0.1*X00", new XdfVariable("X00", XdfVariableKind.Raw, null)), 3200), 9);
        Assert.Equal(3200, codec.ToRaw(Math("0.1*X00", new XdfVariable("X00", XdfVariableKind.Raw, null)), Data(16), 320.0));
    }

    [Fact]
    public void LinkedConstantFeedsY()
    {
        var codec = Codec();
        var target = Math("(1-(0.0039058823*X-0.5))*Y", RawX, new XdfVariable("Y", XdfVariableKind.Link, 0x3236));

        Assert.Equal(14.7007, codec.ToPhysical(target, 128), 4);
        Assert.Equal(128, codec.ToRaw(target, Data(8), 14.7007));
        Assert.True(double.IsNaN(codec.ToPhysical(Math("X+Y", RawX, new XdfVariable("Y", XdfVariableKind.Link, 0x9999)), 1)));
        Assert.True(double.IsNaN(codec.ToPhysical(Math("X+Z", RawX, new XdfVariable("Z", XdfVariableKind.Unsupported, null)), 1)));
    }

    [Fact]
    public void NonFiniteIsNaNAndNeverChosen()
    {
        var codec = Codec();
        var blink = Math("50/X");

        Assert.True(double.IsNaN(codec.ToPhysical(blink, 0)));
        Assert.Equal(5.0, codec.ToPhysical(blink, 10));
        Assert.Equal(10, codec.ToRaw(blink, Data(8), 5.0));
        Assert.Equal(10, codec.ToRaw(blink, Data(8), 4.9));
        Assert.Equal(255, codec.ToRaw(blink, Data(8), 0));
    }

    [Fact]
    public void HugeTargetsClampToTheNearEnd()
    {
        var codec = Codec();
        var ignition = Math("0.375*X-23.625");
        var delay = Math("0.00000029802322*X-0.0000001509884");

        Assert.Equal(0, codec.ToRaw(ignition, Data(8), -1e20));
        Assert.Equal(255, codec.ToRaw(ignition, Data(8), 1e20));
        Assert.Equal(int.MinValue, codec.ToRaw(delay, Data(32, signed: true), -1e13));
        Assert.Equal(int.MaxValue, codec.ToRaw(delay, Data(32, signed: true), 1e13));
    }

    [Fact]
    public void NoFiniteRawMeansNoConversion()
    {
        var codec = Codec();

        Assert.Throws<InvalidOperationException>(
            () => codec.ToRaw(Math("X+Z", RawX, new XdfVariable("Z", XdfVariableKind.Unsupported, null)), Data(8), 1));
    }

    [Fact]
    public void Signed32BitLinearRoundTrips()
    {
        var codec = Codec();
        var delay = Math("0.00000029802322*X-0.0000001509884");

        foreach (var raw in new long[] { -5, 1000 })
        {
            Assert.Equal(raw, codec.ToRaw(delay, Data(32, signed: true), codec.ToPhysical(delay, raw)));
        }

        Assert.Throws<NotSupportedException>(() => codec.ToRaw(Math("50/X"), Data(32), 2));
    }

    [Fact]
    public void FormatRules()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Equal("320.0", ValueCodec.Format(320.0, 3200, 1, null, 16));
            Assert.Equal("160", ValueCodec.Format(159.99999, 480, 1, 2, 16));
            Assert.Equal("0x0014", ValueCodec.Format(20, 20, 0, 3, 16));
            Assert.Equal("12.35", ValueCodec.Format(12.3456, 0, null, null, 8));
            Assert.Equal("n/a", ValueCodec.Format(double.NaN, 0, 1, null, 8));
            Assert.Equal("0.0", ValueCodec.Format(-0.01, 0, 1, null, 8));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void EveryCellRoundTrips()
    {
        var xdf = XdfDefinition.LoadFromFile(TestData.Find(TestData.Xdf)!);
        var image = BinImage.Load(TestData.Find(TestData.CarBin)!);
        var codec = new ValueCodec(xdf, image);
        var checkedCells = 0;
        var notFinite = new List<string>();

        void Check(string title, XdfMath math, XdfData data)
        {
            if (!data.IsSupported || !image.Contains(data))
            {
                return;
            }

            for (var i = 0; i < data.Count; i++)
            {
                var raw = image.ReadElement(data, i);
                var physical = codec.ToPhysical(math, raw);
                if (double.IsNaN(physical))
                {
                    notFinite.Add($"{title}[{i}] raw {raw}");
                    continue;
                }

                var back = codec.ToRaw(math, data, physical);
                Assert.True(
                    back == raw || codec.ToPhysical(math, back) == physical,
                    $"{title}[{i}]: raw {raw} -> {physical} -> raw {back}");
                checkedCells++;
            }
        }

        foreach (var table in xdf.Tables)
        {
            Check(table.Title, table.Z.Math, table.Z.Data);
        }

        foreach (var constant in xdf.Constants)
        {
            Check(constant.Title, constant.Math, constant.Data);
        }

        Assert.True(checkedCells > 20000, $"only {checkedCells} cells checked");
        Assert.Empty(notFinite);
    }
}
