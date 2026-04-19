using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class LiveInputMapTests
{
    private static readonly LiveInputMap Map = LiveInputMap.LoadBundled("43X001")!;

    [Fact]
    public void BundledMapLoadsFor43X001()
    {
        Assert.NotNull(Map);
        Assert.Equal("43X001", Map.SoftwareId);
        Assert.Equal(864, Map.Tables.Count);
        Assert.Contains("8cfe3124", Map.Provenance["bin"]);
        Assert.Null(LiveInputMap.LoadBundled("430069"));
        Assert.Null(LiveInputMap.LoadBundled(null));
    }

    [Fact]
    public void InputsTransformRawChannels()
    {
        Assert.Equal(2480, Map.Inputs["E0E4"].Apply(2480 | 3 << 13));
        Assert.Equal(77, Map.Inputs["807F"].Apply(2480));
        Assert.Equal(255, Map.Inputs["807f"].Apply(8191));
        Assert.Equal(21, Map.Inputs["E33B"].Apply(0x1500));
        Assert.Equal(0x4080, Map.Inputs["E0FE"].Apply(0x40));
        Assert.True(Map.Inputs["E0FE"].Approximate);
        Assert.False(Map.Inputs["E0E4"].Approximate);
        Assert.Equal(("N", 0x04, 16), (Map.Inputs["E0E4"].Channel, Map.Inputs["E0E4"].PacketOffset, Map.Inputs["E0E4"].Bits));
    }

    [Fact]
    public void KnownTablesArePresent()
    {
        var ron98 = Map.FindTable(0x77315)!;
        Assert.True(ron98.Interpolates);
        Assert.Equal("ip_iga_ron98_pl__n__maf", ron98.Name);
        Assert.Equal(new[] { new CatalogAxis(0x7501A, 16, "E0E4"), new CatalogAxis(0x75044, 16, "E32C") }, ron98.Axes);

        var o2 = Map.FindTable(0x7927A)!;
        Assert.False(o2.Interpolates);
        Assert.Equal(new[] { new CatalogAxis(0x740F8, 8, "807F"), new CatalogAxis(0x740FF, 8, "E33B") }, o2.Axes);

        Assert.StartsWith("hand-added", Map.FindTable(0x7EE20)!.Source);
        Assert.Null(Map.FindTable(0x12345));
    }

    [Fact]
    public void AxisInputsAreCollected()
    {
        Assert.Equal(new[] { "E0E4" }, Map.InputsOfAxis(0x7501A));
        Assert.Empty(Map.InputsOfAxis(0x12345));
    }

    [Fact]
    public void BrokenJsonIsRefused()
    {
        const string json = """{ "format": 1, "softwareId": "43X001", "provenance": {}, "tables": [] }""";
        var error = Assert.Throws<InvalidDataException>(() => LiveInputMap.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains("inputs", error.Message);
        Assert.Throws<InvalidDataException>(() => LiveInputMap.Load(new MemoryStream(Encoding.UTF8.GetBytes("{ not json"))));
    }
}
