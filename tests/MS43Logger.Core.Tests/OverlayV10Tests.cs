using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// Protocol 10: V9 plus the pops stabs (group 139) and the downshift blip (140, its gears and tap 141, its gear ratios
/// 142), each 8 bytes after pops' flashed defaults. The rules are the firmware's (sim OverlayV10Tests).
/// </summary>
public sealed class OverlayV10Tests
{
    private static readonly byte[] Defaults = V10Groups.Defaults;

    internal static FakeOverlayV4Ecu Ecu(int version = 10)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        Defaults.CopyTo(image, 0x4FF80);
        return new(image, version);
    }

    private static byte[] Group(int table) => Defaults.AsSpan(8 * (table - OverlayV8.Pops), 8).ToArray();

    private static byte[] With(int table, params (int Index, byte Value)[] changes)
    {
        var copy = Group(table);
        foreach (var (index, value) in changes) copy[index] = value;
        return copy;
    }

    [Fact]
    public void DetectionAcceptsVersion10WithMagicC0DA()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(143, detection.Catalogue!.Count);
        Assert.Equal(OverlayV10.Maps, detection.Catalogue);
        Assert.Equal(Group(OverlayV10.Blip), detection.Flash[OverlayV10.Blip]);
        ecu.HeaderMagic = 0xC0D9; Assert.Equal("not understood", client.Detect().Problem);
    }

    [Fact]
    public void Version9EcusAreUnchanged()
    {
        var detection = new OverlayClient(Ecu(9)).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal(OverlayV9.Maps, detection.Catalogue);
        Assert.Equal(OverlayV9.Maps, OverlayV9.MapsFor(9, 0));
        Assert.Equal(908, OverlayV5.Budget(9));
        Assert.Equal(680, OverlayV5.PoolAEnd(9));
    }

    [Fact]
    public void TheCatalogueMatchesTheEcusTableList()
    {
        // V10's rows as tools/live_overlay_v10.py wrote them into defs/live_overlay_v10_catalog.json at fe5054f: id, name,
        // address, fields. V10.1's build writes 5 fields for 139 and 7 for 141 (OverlayV101Tests).
        (int Id, string Key, int Address, int Fields)[] added =
            [(139, "pops_stabs", 0xCFF88, 4), (140, "blip", 0xCFF90, 8), (141, "blip_gears", 0xCFF98, 6), (142, "blip_ratios", 0xCFFA0, 4)];
        Assert.Equal(Enumerable.Range(0, 143), OverlayV10.Maps.Select(map => map.Index));
        foreach (var (id, key, address, fields) in added)
        {
            var map = OverlayV10.Maps[id];
            Assert.Equal((key, address, 8, 2, fields), (map.Key, map.Address, map.Bytes, map.Flags, map.ScalarGroup!.Fields.Count));
        }
        foreach (var (v9, v10) in OverlayV9.Maps.Zip(OverlayV10.Maps))
            if (v9.Index == OverlayV8.Pops) Assert.Equal(v9 with { ScalarGroup = v10.ScalarGroup }, v10);
            else Assert.Equal(v9, v10);
        Assert.Equal(OverlayV10.Maps, OverlayV9.MapsFor(10, 0));
    }

    [Fact]
    public void ThePopsOpeningWarnsUnder4Tps()
    {
        var opening = OverlayV10.Maps[OverlayV8.Pops].ScalarGroup!.Fields.Single(field => field.Offset == 6).Definition!;
        Assert.Contains("Below 4.0 °TPS the stabs' gaps blur: the ECU closes the throttle slowly there.", opening.Description);
        Assert.Equal(20.0, opening.RangeHigh);
        Assert.DoesNotContain("4.0 °TPS", OverlayV9.PopsGroup.Fields.Single(field => field.Offset == 6).Definition!.Description);
    }

    [Fact]
    public void TheGroupsAreTitledAndCategorisedAsTheAppShowsThem()
    {
        Assert.Equal(["Pops stabs", "Downshift blip", "Downshift blip: gears and tap", "Downshift blip: gear ratios"],
            OverlayV10.Maps.Skip(139).Select(map => map.ScalarGroup!.Title));
        Assert.All(OverlayV10.Maps[139].ScalarGroup!.Fields, field => Assert.Equal("Pops & Bangs", field.Definition!.Category));
        Assert.All(OverlayV10.Maps.Skip(140).SelectMany(map => map.ScalarGroup!.Fields),
            field => Assert.Equal("Downshift blip", field.Definition!.Category));
    }

    public static TheoryData<int, int, string, double, double> Ranges => new()
    {
        // table, byte, units, lowest and highest physical value: the firmware's limits through each equation
        { 139, 0, "s", 0, 5.0 }, { 139, 1, "°TPS", 0, 30.0 }, { 139, 2, "ms", 50, 300 }, { 139, 3, "ms", 100, 1000 },
        { 140, 2, "km/h", 10, 255 }, { 140, 3, "rpm", 2016, 5984 }, { 140, 4, "s", 0.3, 3.0 }, { 140, 5, "°TPS", 0, 30.0 },
        { 140, 6, "°TPS", 0, 30.0 }, { 140, 7, "°BTDC", 0.0, 30.0 },
        { 141, 4, "°PVS", 5.15625, 39.84375 }, { 141, 5, "ms", 100, 600 },
        { 142, 0, "rpm/(km/h)", 90, 127.5 }, { 142, 1, "rpm/(km/h)", 50, 80 }, { 142, 2, "rpm/(km/h)", 35, 55 },
        { 142, 3, "rpm/(km/h)", 25, 42.5 },
    };

    [Theory, MemberData(nameof(Ranges))]
    public void EachSettingShowsTheFirmwaresLimitsInItsUnits(int table, int offset, string units, double low, double high)
    {
        var definition = OverlayV10.Maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset).Definition!;
        Assert.Equal((units, low, high), (definition.Units, definition.RangeLow, definition.RangeHigh));
    }

    [Fact]
    public void TheBlipsChoicesAreNamed()
    {
        static string Description(int table, int offset) =>
            OverlayV10.Maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset).Definition!.Description;
        foreach (var (value, label, _) in OverlayV8.Triggers) Assert.Contains($"\n{value}: {label}", Description(140, 0));
        Assert.EndsWith("1: While braking\n2: On a pedal tap\n3: Braking or tap", Description(140, 1));
        for (var gear = 0; gear < 4; gear++) Assert.EndsWith("\n0: Off\n1: On", Description(141, gear));
        Assert.Contains("tap only", Description(141, 3));
        Assert.Equal(["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st"], OverlayV10.Maps[141].ScalarGroup!.Fields.Take(4).Select(f => f.Label));
    }

    public static TheoryData<int, int, byte> OutOfRange => new()
    {
        { 139, 0, 51 }, { 139, 1, 151 }, { 139, 2, 4 }, { 139, 2, 31 }, { 139, 3, 9 }, { 139, 3, 101 },
        { 140, 0, 3 }, { 140, 0, 0x7F }, { 140, 0, 0xA0 }, { 140, 0, 0xFF }, { 140, 1, 0 }, { 140, 1, 4 },
        { 140, 2, 9 }, { 140, 3, 62 }, { 140, 3, 188 }, { 140, 4, 2 }, { 140, 4, 31 }, { 140, 5, 151 },
        { 140, 6, 151 }, { 140, 7, 62 }, { 140, 7, 144 },
        { 141, 0, 2 }, { 141, 1, 2 }, { 141, 2, 2 }, { 141, 3, 2 }, { 141, 4, 10 }, { 141, 4, 86 }, { 141, 5, 9 },
        { 141, 5, 61 },
        { 142, 0, 179 }, { 142, 1, 99 }, { 142, 1, 161 }, { 142, 2, 69 }, { 142, 2, 111 }, { 142, 3, 49 }, { 142, 3, 86 },
    };

    [Theory, MemberData(nameof(OutOfRange))]
    public void ValidateRefusesEveryOutOfRangeByte(int table, int index, byte value) =>
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(OverlayV10.Version, table, With(table, (index, value)), Group(table)));

    public static TheoryData<int, int, byte> AtTheLimits => new()
    {
        { 139, 0, 0 }, { 139, 0, 50 }, { 139, 1, 0 }, { 139, 1, 150 }, { 139, 2, 5 }, { 139, 2, 30 }, { 139, 3, 10 },
        { 139, 3, 100 },
        { 140, 0, 0 }, { 140, 0, 2 }, { 140, 0, 0x80 }, { 140, 0, 0x97 }, { 140, 0, 0x9F }, { 140, 1, 1 }, { 140, 1, 3 },
        { 140, 2, 10 }, { 140, 2, 255 }, { 140, 3, 63 }, { 140, 3, 187 }, { 140, 4, 3 }, { 140, 4, 30 }, { 140, 5, 150 },
        { 140, 6, 0 }, { 140, 7, 63 }, { 140, 7, 143 },
        { 141, 0, 0 }, { 141, 3, 1 }, { 141, 4, 11 }, { 141, 4, 85 }, { 141, 5, 10 }, { 141, 5, 60 },
        { 142, 0, 180 }, { 142, 0, 255 }, { 142, 1, 100 }, { 142, 1, 160 }, { 142, 2, 70 }, { 142, 2, 110 },
        { 142, 3, 50 }, { 142, 3, 85 },
    };

    [Theory, MemberData(nameof(AtTheLimits))]
    public void ValidateAcceptsEveryByteAtItsLimits(int table, int index, byte value) =>
        Assert.Null(OverlayV9.ValidateFor(OverlayV10.Version, table, With(table, (index, value)), Group(table)));

    [Fact]
    public void AHoldOpeningAboveTheBlipOpeningIsRefused()
    {
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.Blip, With(140, (5, 50), (6, 51)), Group(140)));
        Assert.Null(OverlayV10.ValidateCopy(OverlayV10.Blip, With(140, (5, 50), (6, 50)), Group(140)));
    }

    public static TheoryData<int, int> Reserved => new()
    {
        { 139, 4 }, { 139, 5 }, { 139, 6 }, { 139, 7 }, { 141, 6 }, { 141, 7 }, { 142, 4 }, { 142, 5 }, { 142, 6 }, { 142, 7 },
    };

    [Theory, MemberData(nameof(Reserved))]
    public void ReservedBytesMustBeZero(int table, int index) =>
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(table, With(table, (index, 1)), Group(table)));

    [Fact]
    public void TheRefusalSaysWhichRuleAGroupBroke()
    {
        Assert.Equal("A pops setting is outside its limits", OverlayV10.Refusal(OverlayV8.Pops, With(138, (6, 201))));
        Assert.Equal("A stab setting is outside its limits", OverlayV10.Refusal(OverlayV10.Stabs, With(139, (1, 151))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.Blip, With(140, (5, 151))));
        Assert.Equal("A blip setting is outside its limits: the hold opening is above the blip opening",
            OverlayV10.Refusal(OverlayV10.Blip, With(140, (5, 40), (6, 41))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.BlipGears, With(141, (3, 2))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.BlipRatios, With(142, (0, 179))));
        Assert.Null(OverlayV10.Refusal(OverlayV6.LaunchControl, new byte[8]));
    }

    [Fact]
    public void ACopyOfAnotherLengthIsRefused() =>
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.Stabs, Group(139).AsSpan(0, 4), Group(139)));

    [Fact]
    public void V10RulesOnlyFromVersion10()
    {
        foreach (var table in new[] { 139, 140, 141, 142 })
        {
            Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(OverlayV9.Version, table, Group(table), Group(table)));
            Assert.Null(OverlayV9.ValidateFor(OverlayV10.Version, table, Group(table), Group(table)));
        }
        // V10 keeps V9's other rules: pops' opening up to 20.0 °TPS, and the triggers of LC, NLS and RAL
        Assert.Null(OverlayV9.ValidateFor(OverlayV10.Version, OverlayV8.Pops, With(138, (6, 200)), Group(138)));
        Assert.Equal(OverlayReason.Range, OverlayV9.ValidateFor(OverlayV10.Version, OverlayV8.Pops, With(138, (6, 201)), Group(138)));
        var flash = FakeOverlayEcu.Image().AsSpan(0x7132C, 8).ToArray(); var trigger = flash.ToArray(); trigger[0] = OverlayV8.AirConOn;
        Assert.Null(OverlayV9.ValidateFor(OverlayV10.Version, OverlayV6.LaunchControl, trigger, flash));
    }

    [Fact]
    public void PlannerKeepsPoolAWithin668ForVersion10()
    {
        Assert.Equal(896, OverlayV5.Budget(10));
        Assert.Equal(668, OverlayV5.PoolAEnd(10));
        Assert.True(OverlayV5.Fits(660, 8, 10));
        Assert.False(OverlayV5.Fits(664, 8, 10));
        Assert.Equal(OverlayV10.Maps, OverlayV5.Catalogue(10));
    }

    public static TheoryData<int[]> FittingSets => new() { new[] { 1, 2, 3, 4 }, new[] { 0, 1, 3, 4 }, new[] { 0, 1, 2, 4 }, new[] { 0, 1, 2, 3 } };

    [Theory, MemberData(nameof(FittingSets))] // RF5: V8's fitting sets still fit in 668 bytes of pool A plus pool B
    public void ThePlannerFitsV8sCombinations(int[] tables)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var table in tables)
        {
            var copy = detection.Flash[table].ToArray(); copy[0] = (byte)(copy[0] == 255 ? 254 : copy[0] + 1);
            Assert.Null(client.Send(table, copy, detection.Flash[table], client.Status()).Refused);
        }
    }

    [Fact]
    public void TheBlipGoesLiveAndReadsBack()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var copy = With(140, (0, 1), (5, 100));
        Assert.Null(client.Send(OverlayV10.Blip, copy, detection.Flash[OverlayV10.Blip], client.Status()).Refused);
        var entry = client.Status().Tables[OverlayV10.Blip];
        Assert.Equal(OverlayState.On, entry.State);
        Assert.Equal(copy, client.ReadCopy(OverlayV10.Blip, entry));
    }

    [Fact]
    public void AHoldAboveTheBlipOpeningIsRefusedBeforeSending()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var requests = ecu.Requests.Count;
        var result = client.Send(OverlayV10.Blip, With(140, (5, 40), (6, 41)), detection.Flash[OverlayV10.Blip], client.Status());
        Assert.Equal(OverlayReason.Range, result.Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3 or 9);
    }
}
