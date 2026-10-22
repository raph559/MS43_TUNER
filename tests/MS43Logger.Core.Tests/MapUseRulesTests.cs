using System.Text;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

public class MapUseRulesTests
{
    private static readonly MapUseRules Rules = MapUseRules.LoadBundled("43X001")!;

    [Fact]
    public void BundledRulesLoad()
    {
        Assert.Equal("43X001", Rules.SoftwareId);
        Assert.Equal(15, Rules.Rules.Count);
        Assert.Equal(new[] { "activeVe", "load", "octane", "rpm", "speed", "state", "status1", "vanos" }, Rules.Selectors.Keys.Order());
        Assert.Equal(new[] { "e85Cam", "e85Fuel", "e85Ignition", "octane" }, Rules.Factors.Keys.Order());
        Assert.Equal("ignitionPartLoad", Rules.FindRule(0x77315)!.Id);
        Assert.Equal("ve", Rules.FindRule(0x7FC20)!.Id);
        Assert.Null(Rules.FindRule(0x76CDD));
        Assert.Contains("FD18.15", Rules.LeftOutReason(0x76CDD));
        Assert.Null(Rules.LeftOutReason(0x77315));
        Assert.Contains("car bin", Rules.Provenance.Keys);
        Assert.All(Rules.Rules, rule => Assert.Equal("observed", rule.Selection));
        Assert.Contains("FD2C.4", Rules.FindRule(0x77315)!.Inferred);
        Assert.Null(Rules.FindRule(0x77470)!.Inferred);
        Assert.Null(MapUseRules.LoadBundled("430069"));
        Assert.Null(MapUseRules.LoadBundled(null));
    }

    [Fact]
    public void SelectorsSitWhereTheFrameCarriesThem()
    {
        Assert.Equal(new UseSelector("activeVe", "E31C", "ACTIVEVE", 0x10, 16, 0xFFFF, Rules.Selectors["activeVe"].Evidence), Rules.Selectors["activeVe"]);
        Assert.Equal((0x00, 8, 0xFFL, "LUTPL"), Field(Rules.Selectors["state"]));
        Assert.Equal((0x01, 8, 0xFFL, "CHSTATE"), Field(Rules.Selectors["status1"]));
        Assert.Equal((0x02, 8, 0x03L, "VANOSACTIVE"), Field(Rules.Selectors["vanos"]));
        Assert.Equal((0x06, 8, 0xFFL, "VS"), Field(Rules.Selectors["speed"]));
        Assert.Equal((0x40, 8, 0xFFL, "RONADFAC"), Field(Rules.Selectors["octane"]));
        Assert.Equal((0x12, 16, 0xFF00L, "MAFTI"), Field(Rules.Selectors["load"]));
        Assert.Equal(8, Rules.Selectors["load"].Shift);
        Assert.Equal((0x04, 16, 0x1FFFL, "N"), Field(Rules.Selectors["rpm"]));
        Assert.Equal(0, Rules.Selectors["rpm"].Shift);

        static (int, int, long, string) Field(UseSelector s) => (s.PacketOffset, s.Bits, s.Mask, s.Channel);
    }

    [Fact]
    public void TheFullLoadCurveIsTheOneTheCatalogueLists()
    {
        var curve = Rules.Curves["vanosFullLoad"];
        Assert.Equal(new UseCurve("vanosFullLoad", 0x75D7F, "id_maf_fl_ivvt__n", 8, 0x74B6C, 16, "rpm", curve.Evidence), curve);
        var catalogue = LiveInputMap.LoadBundled("43X001")!.FindTable(0x75D7F)!;
        Assert.False(catalogue.Interpolates);
        Assert.Equal(new CatalogAxis(0x74B6C, 16, "E0E4"), catalogue.Axes.Single());
        Assert.Equal("E0E4", Rules.Selectors["rpm"].Ram);
        Assert.Equal("E33B", Rules.Selectors["load"].Ram);

        foreach (var id in new[] { "vanosFullLoadIntake", "vanosFullLoadExhaust", "vanosPartLoadIntake", "vanosPartLoadExhaust" })
        {
            var rule = Rules.Rules.Single(r => r.Id == id);
            Assert.Contains(rule.Cases, c => c.When.Any(w => w.AboveCurve == "vanosFullLoad" && w.Selector == "load"));
        }

        Assert.Equal("vanosFullLoadIntake", Rules.FindRule(0x7635C)!.Id);
        Assert.Equal("vanosFullLoadIntake", Rules.FindRule(0x76174)!.Id);
        Assert.Equal("vanosPartLoadExhaust", Rules.FindRule(0x7629C)!.Id);
        Assert.Equal("vanosPartLoadExhaust", Rules.FindRule(0x760B4)!.Id);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void EveryRuleMapHasACursorOnTheCar()
    {
        var document = CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);
        var map = LiveInputMap.LoadBundled("43X001");
        var members = Rules.Rules.SelectMany(rule => rule.Members).ToList();
        Assert.Equal(35, members.Count);
        foreach (var member in members)
        {
            var table = document.Xdf.Tables.SingleOrDefault(t => t.Z.Data.Address == member.Data);
            Assert.True(table is not null, $"no XDF table at 0x{member.Data:X}");
            Assert.Equal(member.Name, table!.Title);
            var binding = LiveTableBinding.Resolve(map, document, table);
            Assert.True(binding.HasInput, $"{member.Name}: {binding.Reason}");
        }
    }

    [Fact]
    public void BrokenRulesAreRejected()
    {
        var unknownFactor = Valid.Replace("\"weight\": [\"e85Fuel\"]", "\"weight\": [\"e85Diesel\"]");
        var unknownSelector = Valid.Replace("\"selector\": \"state\"", "\"selector\": \"gearbox\"");
        var twice = Valid.Replace("\"data\": \"0x7D75C\"", "\"data\": \"0x7D69C\"");
        var guessed = Valid.Replace("\"selection\": \"observed\"", "\"selection\": \"guessed\"");
        var unknownCurve = Valid.Replace("\"above\": \"fullLoad\"", "\"above\": \"halfLoad\"");
        var curveInput = Valid.Replace("\"input\": \"state\"", "\"input\": \"rpm\"");
        var interpolated = Valid.Replace("\"interpolate\": false", "\"interpolate\": true");
        foreach (var json in new[] { unknownFactor, unknownSelector, twice, guessed, unknownCurve, curveInput, interpolated, "{}" })
        {
            var error = Assert.Throws<InvalidDataException>(() => MapUseRules.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
            Assert.StartsWith("The map-use rules cannot be read:", error.Message);
        }

        Assert.Single(MapUseRules.Load(new MemoryStream(Encoding.UTF8.GetBytes(Valid))).Rules);
    }

    private const string Valid = """
        {
          "format": 1,
          "softwareId": "43X001",
          "provenance": {"date": "2026-10-21"},
          "selectors": [{"id": "state", "ram": "FD2D", "channel": "LUTPL", "offset": "0x00", "bits": 8, "mask": "0xFF", "evidence": "e"}],
          "curves": [{"id": "fullLoad", "data": "0x75D7F", "name": "id_maf_fl_ivvt__n", "bits": 8, "axis": "0x74B6C", "axisBits": 16, "input": "state", "interpolate": false, "evidence": "e"}],
          "factors": [{"id": "e85Fuel", "cal": "0x70A10", "offset": 128, "scale": 32, "requires": [{"cal": "0x70DF8", "equals": 0}], "evidence": "e"}],
          "rules": [{
            "id": "afrTarget", "title": "AFR target", "selection": "observed", "evidence": "e", "blend": "weights",
            "cases": [{"when": [{"selector": "state", "mask": "0x06", "in": [6]}, {"selector": "state", "above": "fullLoad"}], "then": "use"}, {"when": [], "then": "notInUse"}],
            "members": [
              {"data": "0x7D69C", "name": "ip_af_target_ron98__maf__n", "label": "RON98", "weight": ["1-e85Fuel"]},
              {"data": "0x7D75C", "name": "ip_af_target_e85__maf__n", "label": "E85", "weight": ["e85Fuel"]}
            ]
          }],
          "leftOut": []
        }
        """;
}
