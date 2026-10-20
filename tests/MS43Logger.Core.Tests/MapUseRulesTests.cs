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
        Assert.Equal(12, Rules.Rules.Count);
        Assert.Equal(new[] { "activeVe", "octane", "speed", "state", "status1", "vanos" }, Rules.Selectors.Keys.Order());
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

        static (int, int, long, string) Field(UseSelector s) => (s.PacketOffset, s.Bits, s.Mask, s.Channel);
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
        foreach (var json in new[] { unknownFactor, unknownSelector, twice, guessed, "{}" })
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
          "factors": [{"id": "e85Fuel", "cal": "0x70A10", "offset": 128, "scale": 32, "requires": [{"cal": "0x70DF8", "equals": 0}], "evidence": "e"}],
          "rules": [{
            "id": "afrTarget", "title": "AFR target", "selection": "observed", "evidence": "e", "blend": "weights",
            "cases": [{"when": [{"selector": "state", "mask": "0x06", "in": [6]}], "then": "use"}, {"when": [], "then": "notInUse"}],
            "members": [
              {"data": "0x7D69C", "name": "ip_af_target_ron98__maf__n", "label": "RON98", "weight": ["1-e85Fuel"]},
              {"data": "0x7D75C", "name": "ip_af_target_e85__maf__n", "label": "E85", "weight": ["e85Fuel"]}
            ]
          }],
          "leftOut": []
        }
        """;
}
