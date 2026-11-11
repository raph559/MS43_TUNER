using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// The overlay the app talks to, V10.4: V9 plus the downshift blip (140, its gears and tap 141, its gear ratios 142), each
/// 8 bytes after pops' flashed defaults; 139 is reserved. The pops AFR target is the real AFR × 10; 141/6 is the blip lead
/// and 141/7 the spark above the blip's target. Any other version is refused.
/// </summary>
public sealed class CurrentOverlayTests
{
    private static readonly byte[] Defaults = OverlayV104Settings.Defaults;

    internal static FakeOverlayEcu Ecu() => new(FakeOverlayEcu.CurrentImage());

    private static byte[] Group(int table) => Defaults.AsSpan(8 * (table - OverlaySettings.Pops), 8).ToArray();

    private static byte[] With(int table, params (int Index, byte Value)[] changes)
    {
        var copy = Group(table);
        foreach (var (index, value) in changes) copy[index] = value;
        return copy;
    }

    private static OverlayScalar Field(int table, int offset) =>
        OverlayV10.Maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset);

    private static double Shown(OverlayScalarDefinition definition, byte raw) =>
        ExpressionEvaluator.Evaluate(definition.Equation, new Dictionary<string, double> { ["X"] = raw });

    [Fact]
    public void DetectionAcceptsCurrentOverlayWithValidMagic()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Null(detection.Problem);
        Assert.Equal("10.4", detection.Header!.Label);
        Assert.Equal(143, detection.Catalogue!.Count);
        Assert.Equal(OverlayV10.Maps, detection.Catalogue);
        Assert.Equal(Group(OverlayV10.Blip), detection.Flash[OverlayV10.Blip]);
        ecu.HeaderMagic = 0xC0D9; Assert.Equal("not understood", client.Detect().Problem);
    }

    public static TheoryData<int, int> OlderOverlays => new()
    {
        { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }, { 6, 0 }, { 7, 0 }, { 8, 0 }, { 8, 5 }, { 9, 0 }, { 10, 0 }, { 10, 1 }, { 10, 2 }, { 10, 3 }, { 11, 0 }, { 10, 5 },
    };

    [Theory, MemberData(nameof(OlderOverlays))]
    public void EveryOtherVersionIsRefusedAndSentNothing(int version, int minor)
    {
        var ecu = Ecu(); ecu.HeaderVersion = version; ecu.Minor = minor;
        var client = new OverlayClient(ecu); var detection = client.Detect();
        Assert.Contains($"overlay V{(minor == 0 ? version.ToString() : $"{version}.{minor}")}", detection.Problem);
        Assert.Contains("V10.4 only", detection.Problem);
        Assert.Empty(detection.Flash);
        Assert.Null(detection.Catalogue);
        Assert.Throws<InvalidOperationException>(() => client.Status());
        Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x6D);
    }

    [Fact]
    public void ABinCarriesTheCurrentOverlayOnlyAtV104()
    {
        var image = FakeOverlayEcu.CurrentImage();
        Assert.False(OverlayV10.IsCurrent(image));                               // no header yet
        "LTOV"u8.CopyTo(image.AsSpan(0x4B544)); image[0x4B548] = 10; image[0x4B549] = 4;
        Assert.True(OverlayV10.IsCurrent(image));
        image[0x4B549] = 3; Assert.False(OverlayV10.IsCurrent(image));          // V10.3
        image[0x4B549] = 2; Assert.False(OverlayV10.IsCurrent(image));
        image[0x4B548] = 9; image[0x4B549] = 0; Assert.False(OverlayV10.IsCurrent(image));
    }

    [Fact]
    public void TheCatalogueMatchesTheEcusTableList()
    {
        // Expected V10.4 settings groups: ID, key, flash address, and field count.
        (int Id, string Key, int Address, int Fields)[] added =
            [(140, "blip", 0xCFF90, 8), (141, "blip_gears", 0xCFF98, 8), (142, "blip_ratios", 0xCFFA0, 4)];
        Assert.Equal(Enumerable.Range(0, 143), OverlayV10.Maps.Select(map => map.Index));
        foreach (var (id, key, address, fields) in added)
        {
            var map = OverlayV10.Maps[id];
            Assert.Equal((key, address, 8, 2, fields), (map.Key, map.Address, map.Bytes, map.Flags, map.ScalarGroup!.Fields.Count));
        }
        var reserved = OverlayV10.Maps[OverlayV10.Reserved];
        Assert.Equal(("reserved_139", 0xCFF88, 8, 2, true), (reserved.Key, reserved.Address, reserved.Bytes, reserved.Flags, reserved.Reserved));
        Assert.Null(reserved.ScalarGroup);
        Assert.Equal(1, OverlayV10.Maps.Count(m => m.Reserved));
    }

    [Fact]
    public void NoSettingMentionsTheStabs()
    {
        var fields = OverlayV10.Maps.Where(m => m.ScalarGroup is not null).SelectMany(m => m.ScalarGroup!.Fields);
        Assert.DoesNotContain(fields, f => f.Label.Contains("stab", StringComparison.OrdinalIgnoreCase)
            || f.Definition?.Description.Contains("stab", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ThePopsGroupHasTheRealAfrAndThe20DegreeOpening()
    {
        var afr = Field(OverlaySettings.Pops, 3);
        Assert.Equal("AFR target (petrol)", afr.Label);
        Assert.Equal(("AFR", 5.0, 14.7, 1), (afr.Definition!.Units, afr.Definition.RangeLow, afr.Definition.RangeHigh, afr.Definition.Decimals));
        Assert.Equal(7.0, Shown(afr.Definition, 70), 9);
        Assert.Equal(5.0, Shown(afr.Definition, 50), 9);
        Assert.Equal(14.7, Shown(afr.Definition, 147), 9);
        Assert.Equal(20.0, Field(OverlaySettings.Pops, 6).Definition!.RangeHigh);
    }

    [Fact]
    public void TheGroupsAreTitledAndCategorisedAsTheAppShowsThem()
    {
        Assert.Equal(["Downshift blip", "Downshift blip: gears and tap", "Downshift blip: gear ratios"],
            OverlayV10.Maps.Skip(140).Select(map => map.ScalarGroup!.Title));
        Assert.All(OverlayV10.Maps.Skip(140).SelectMany(map => map.ScalarGroup!.Fields),
            field => Assert.Equal("Downshift blip", field.Definition!.Category));
        Assert.Equal(["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st", "Tap threshold", "Tap length", "Blip lead", "Spark above the target"],
            OverlayV10.Maps[OverlayV10.BlipGears].ScalarGroup!.Fields.Select(f => f.Label));
    }

    public static TheoryData<int, int, string, double, double> Ranges => new()
    {
        // table, byte, units, lowest and highest physical value: the firmware's limits through each equation
        { 138, 3, "AFR", 5.0, 14.7 }, { 138, 6, "°TPS", 0, 20.0 },
        { 140, 2, "km/h", 10, 255 }, { 140, 3, "rpm", 2016, 5984 }, { 140, 4, "s", 0.3, 3.0 }, { 140, 5, "°TPS", 0, 30.0 },
        { 140, 6, "°TPS", 0, 30.0 }, { 140, 7, "°BTDC", 0.0, 30.0 },
        { 141, 4, "°PVS", 5.15625, 39.84375 }, { 141, 5, "ms", 100, 600 }, { 141, 6, "ms", 0, 300 }, { 141, 7, "°BTDC", -23.625, 30.0 },
        { 142, 0, "rpm/(km/h)", 90, 127.5 }, { 142, 1, "rpm/(km/h)", 50, 80 }, { 142, 2, "rpm/(km/h)", 35, 55 },
        { 142, 3, "rpm/(km/h)", 25, 42.5 },
    };

    [Theory, MemberData(nameof(Ranges))]
    public void EachSettingShowsTheFirmwaresLimitsInItsUnits(int table, int offset, string units, double low, double high)
    {
        var definition = Field(table, offset).Definition!;
        Assert.Equal((units, low, high), (definition.Units, definition.RangeLow, definition.RangeHigh));
    }

    [Fact]
    public void TheNewerSettingsSayWhatTheyDo()
    {
        var lead = Field(OverlayV10.BlipGears, 6).Definition!.Description;
        Assert.Contains("192 rpm", lead);
        Assert.Contains("0 waits for the engine speed itself", lead);
        var cap = Field(OverlayV10.BlipGears, 7).Definition!;
        Assert.Equal(-23.625, Shown(cap, 0), 9);
        Assert.Equal(30.0, Shown(cap, 143), 9);
    }

    [Fact]
    public void TheBlipsChoicesAreNamed()
    {
        static string Description(int table, int offset) => Field(table, offset).Definition!.Description;
        foreach (var (value, label, _) in OverlayTriggers.Pops) Assert.Contains($"\n{value}: {label}", Description(140, 0));
        Assert.EndsWith("1: While braking\n2: On a pedal tap\n3: Braking or tap", Description(140, 1));
        for (var gear = 0; gear < 4; gear++) Assert.EndsWith("\n0: Off\n1: On", Description(141, gear));
        Assert.Contains("tap only", Description(141, 3));
    }

    public static TheoryData<int, int, byte> OutOfRange => new()
    {
        { 138, 3, 49 }, { 138, 3, 148 }, { 138, 3, 210 }, { 138, 6, 201 },
        { 139, 0, 1 }, { 139, 1, 25 }, { 139, 4, 15 },
        { 140, 0, 3 }, { 140, 0, 0x7F }, { 140, 0, 0xA0 }, { 140, 0, 0xFF }, { 140, 1, 0 }, { 140, 1, 4 },
        { 140, 2, 9 }, { 140, 3, 62 }, { 140, 3, 188 }, { 140, 4, 2 }, { 140, 4, 31 }, { 140, 5, 151 },
        { 140, 6, 151 }, { 140, 7, 62 }, { 140, 7, 144 },
        { 141, 0, 2 }, { 141, 1, 2 }, { 141, 2, 2 }, { 141, 3, 2 }, { 141, 4, 10 }, { 141, 4, 86 }, { 141, 5, 9 },
        { 141, 5, 61 }, { 141, 6, 31 }, { 141, 7, 144 },
        { 142, 0, 179 }, { 142, 1, 99 }, { 142, 1, 161 }, { 142, 2, 69 }, { 142, 2, 111 }, { 142, 3, 49 }, { 142, 3, 86 },
        { 142, 4, 1 }, { 142, 7, 1 },
    };

    [Theory, MemberData(nameof(OutOfRange))]
    public void ValidateRefusesEveryOutOfRangeByte(int table, int index, byte value) =>
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(table, With(table, (index, value)), Group(table)));

    public static TheoryData<int, int, byte> AtTheLimits => new()
    {
        { 138, 3, 50 }, { 138, 3, 70 }, { 138, 3, 147 }, { 138, 6, 200 }, { 138, 0, 0x97 },
        { 139, 0, 0 },
        { 140, 0, 0 }, { 140, 0, 2 }, { 140, 0, 0x80 }, { 140, 0, 0x97 }, { 140, 0, 0x9F }, { 140, 1, 1 }, { 140, 1, 3 },
        { 140, 2, 10 }, { 140, 2, 255 }, { 140, 3, 63 }, { 140, 3, 187 }, { 140, 4, 3 }, { 140, 4, 30 }, { 140, 5, 150 },
        { 140, 6, 0 }, { 140, 7, 63 }, { 140, 7, 143 },
        { 141, 0, 0 }, { 141, 3, 1 }, { 141, 4, 11 }, { 141, 4, 85 }, { 141, 5, 10 }, { 141, 5, 60 },
        { 141, 6, 0 }, { 141, 6, 30 }, { 141, 7, 0 }, { 141, 7, 143 },
        { 142, 0, 180 }, { 142, 0, 255 }, { 142, 1, 100 }, { 142, 1, 160 }, { 142, 2, 70 }, { 142, 2, 110 },
        { 142, 3, 50 }, { 142, 3, 85 },
    };

    [Theory, MemberData(nameof(AtTheLimits))]
    public void ValidateAcceptsEveryByteAtItsLimits(int table, int index, byte value) =>
        Assert.Null(OverlayV10.ValidateCopy(table, With(table, (index, value)), Group(table)));

    [Fact]
    public void TheFlashedGroupsAreValid()
    {
        foreach (var table in new[] { 138, 139, 140, 141, 142 })
            Assert.Null(OverlayV10.ValidateCopy(table, Group(table), Group(table)));
    }

    [Fact]
    public void LcTakesItsTriggers()
    {
        var flash = FakeOverlayEcu.Image().AsSpan(0x7132C, 8).ToArray(); var trigger = flash.ToArray(); trigger[0] = OverlayTriggers.AirConOn;
        Assert.Null(OverlayV10.ValidateCopy(OverlaySettings.LaunchControl, trigger, flash));
    }

    [Fact]
    public void AHoldOpeningAboveTheBlipOpeningIsRefused()
    {
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.Blip, With(140, (5, 50), (6, 51)), Group(140)));
        Assert.Null(OverlayV10.ValidateCopy(OverlayV10.Blip, With(140, (5, 50), (6, 50)), Group(140)));
    }

    [Fact]
    public void TheRefusalSaysWhichRuleAGroupBroke()
    {
        Assert.Equal("A pops setting is outside its limits", OverlayV10.Refusal(OverlaySettings.Pops, With(138, (6, 201))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.Blip, With(140, (5, 151))));
        Assert.Equal("A blip setting is outside its limits: the hold opening is above the blip opening",
            OverlayV10.Refusal(OverlayV10.Blip, With(140, (5, 40), (6, 41))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.BlipGears, With(141, (3, 2))));
        Assert.Equal("A blip setting is outside its limits", OverlayV10.Refusal(OverlayV10.BlipRatios, With(142, (0, 179))));
        Assert.Null(OverlayV10.Refusal(OverlaySettings.LaunchControl, new byte[8]));
    }

    [Fact]
    public void ACopyOfAnotherLengthIsRefused() =>
        Assert.Equal(OverlayReason.Range, OverlayV10.ValidateCopy(OverlayV10.Blip, Group(140).AsSpan(0, 4), Group(140)));

    [Fact]
    public void PlannerKeepsPoolAWithin668()
    {
        Assert.Equal(896, OverlayMemory.Budget);
        Assert.Equal(668, OverlayMemory.PoolAEnd);
        Assert.True(OverlayMemory.Fits(660, 8));
        Assert.False(OverlayMemory.Fits(664, 8));
    }

    public static TheoryData<int[]> FittingSets => new() { new[] { 1, 2, 3, 4 }, new[] { 0, 1, 3, 4 }, new[] { 0, 1, 2, 4 }, new[] { 0, 1, 2, 3 } };

    [Theory, MemberData(nameof(FittingSets))]
    public void ThePlannerFitsFourOfTheFirstFiveMaps(int[] tables)
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var table in tables)
        {
            var copy = detection.Flash[table].ToArray(); copy[0] = (byte)(copy[0] == 255 ? 254 : copy[0] + 1);
            Assert.Null(client.Send(table, copy, detection.Flash[table], client.Status()).Refused);
        }
    }

    [Fact]
    public void TheSettingsGoLiveAndReadBack()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var (table, copy) in new[] { (140, With(140, (0, 1), (5, 100))), (141, With(141, (6, 30), (7, 40))), (138, With(138, (3, 147))) })
        {
            Assert.Null(client.Send(table, copy, detection.Flash[table], client.Status()).Refused);
            var entry = client.Status().Tables[table];
            Assert.Equal(OverlayState.On, entry.State);
            Assert.Equal(copy, client.ReadCopy(table, entry));
        }
    }

    [Fact]
    public void AnInvalidCopyIsRefusedBeforeSending()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        var requests = ecu.Requests.Count;
        Assert.Equal(OverlayReason.Range, client.Send(OverlayV10.Blip, With(140, (5, 40), (6, 41)), detection.Flash[OverlayV10.Blip], client.Status()).Refused);
        Assert.Equal(OverlayReason.Range, client.Send(OverlaySettings.Pops, With(138, (3, 210)), detection.Flash[OverlaySettings.Pops], client.Status()).Refused);
        Assert.Equal(OverlayReason.Range, client.Send(OverlayV10.Reserved, With(139, (0, 1)), detection.Flash[OverlayV10.Reserved], client.Status()).Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3 or 9);
    }

    [Theory] // V10.4: the pops spark angle at most +30.0 °BTDC (143), as the firmware's pops_limits
    [InlineData(0, true)] [InlineData(143, true)] [InlineData(144, false)] [InlineData(255, false)]
    public void ThePopsAngleIsLimitedToPlus30(byte angle, bool accepted)
    {
        Assert.Equal(((byte)0, (byte)143), OverlayV10.PopsLimits[2]);
        var pops = OverlayV10.PopsGroup.Fields.Single(field => field.Offset == 2).Definition!;
        Assert.Equal(30.0, pops.RangeHigh);
        var copy = OverlayV104Settings.Defaults[..8];
        copy[2] = angle;
        Assert.Equal(accepted ? null : OverlayReason.Range, OverlayV10.ValidateCopy(OverlaySettings.Pops, copy, OverlayV104Settings.Defaults[..8]));
    }
}
