using MS43Logger.Core.LiveTuning;

namespace MS43Logger.Core.Tests;

/// <summary>
/// Protocol 10.1 (header minor 1): V10 with 139/4 the rise allowed once stabbing (32 rpm, 5..31), 141/6 the blip lead
/// (10 ms, 0..30) and the pops AFR target down to 7.4 (138/3 up to 255). A V10 ECU (minor 0) keeps V10's rules. The
/// rules are the firmware's (sim OverlayV10Tests).
/// </summary>
public sealed class OverlayV101Tests
{
    private static FakeOverlayV4Ecu Ecu(int minor = 1)
    {
        var image = FakeOverlayEcu.Image();
        Array.Fill(image, (byte)219, 0x75D47, 6); image[0x70BFF] = 2;
        (minor >= 1 ? V10Groups.DefaultsV101 : V10Groups.Defaults).CopyTo(image, 0x4FF80);
        return new(image, OverlayV10.Version) { Minor = minor };
    }

    private static byte[] Group(int table, int minor = 1) =>
        (minor >= 1 ? V10Groups.DefaultsV101 : V10Groups.Defaults).AsSpan(8 * (table - OverlayV8.Pops), 8).ToArray();

    private static byte[] With(int table, int minor, params (int Index, byte Value)[] changes)
    {
        var copy = Group(table, minor);
        foreach (var (index, value) in changes) copy[index] = value;
        return copy;
    }

    private static OverlayScalarDefinition Definition(IReadOnlyList<OverlayMap> maps, int table, int offset) =>
        maps[table].ScalarGroup!.Fields.Single(field => field.Offset == offset).Definition!;

    [Fact]
    public void AV101EcuGetsV101sCatalogue()
    {
        var detection = new OverlayClient(Ecu()).Detect();
        Assert.Null(detection.Problem);
        Assert.Equal("10.1", detection.Header!.Label);
        Assert.Equal(OverlayV10.MapsFor(1), detection.Catalogue);
        Assert.NotEqual(OverlayV10.Maps, detection.Catalogue);
        Assert.Equal(OverlayV10.Maps, new OverlayClient(Ecu(minor: 0)).Detect().Catalogue);
        Assert.Equal(OverlayV10.MapsFor(1), OverlayV9.MapsFor(10, 1));
        Assert.Equal(OverlayV10.Maps, OverlayV9.MapsFor(10, 0));
        Assert.Equal(OverlayV10.Maps, OverlayV10.MapsFor(0));
    }

    [Fact]
    public void V101AddsTwoSettingsAndMovesNothing()
    {
        var v10 = OverlayV10.Maps; var v101 = OverlayV10.MapsFor(1);
        Assert.Equal(v10.Select(m => (m.Index, m.Key, m.Address, m.Bytes, m.Flags)), v101.Select(m => (m.Index, m.Key, m.Address, m.Bytes, m.Flags)));
        Assert.Equal(["Load time", "Stab size", "Stab length", "Gap", "Rise allowed during stabs"],
            v101[OverlayV10.Stabs].ScalarGroup!.Fields.Select(f => f.Label));
        Assert.Equal(["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st", "Tap threshold", "Tap length", "Blip lead"],
            v101[OverlayV10.BlipGears].ScalarGroup!.Fields.Select(f => f.Label));
        for (var i = 0; i < v10.Count; i++)
            if (i is not (OverlayV8.Pops or OverlayV10.Stabs or OverlayV10.BlipGears)) Assert.Equal(v10[i], v101[i]);
        Assert.Equal(4, v10[OverlayV10.Stabs].ScalarGroup!.Fields.Count);
        Assert.Equal(6, v10[OverlayV10.BlipGears].ScalarGroup!.Fields.Count);
    }

    public static TheoryData<int, int, string, double, double> Ranges => new()
    {
        // table, byte, units, lowest and highest physical value: the firmware's limits through each equation
        { 139, 4, "rpm", 160, 992 }, { 141, 6, "ms", 0, 300 }, { 138, 3, "AFR", 7.40, 14.7 },
    };

    [Theory, MemberData(nameof(Ranges))]
    public void TheNewSettingsShowTheFirmwaresLimits(int table, int offset, string units, double low, double high)
    {
        var definition = Definition(OverlayV10.MapsFor(1), table, offset);
        Assert.Equal((units, low, high), (definition.Units, definition.RangeLow, definition.RangeHigh));
        Assert.Equal(definition.Category, table == OverlayV10.BlipGears ? OverlayV10.BlipCategory : OverlayV8.Category);
    }

    [Fact] // RF3: V10 keeps its AFR floor of 9.99, and its stabs and gears groups their four and six settings
    public void AV10ShowsV10sLimits()
    {
        Assert.Equal(9.99, Definition(OverlayV10.Maps, OverlayV8.Pops, 3).RangeLow);
        Assert.DoesNotContain(OverlayV10.Maps[OverlayV10.Stabs].ScalarGroup!.Fields, f => f.Offset == 4);
        Assert.DoesNotContain(OverlayV10.Maps[OverlayV10.BlipGears].ScalarGroup!.Fields, f => f.Offset == 6);
    }

    [Fact]
    public void TheNewSettingsSayWhatTheyDo()
    {
        var rise = Definition(OverlayV10.MapsFor(1), OverlayV10.Stabs, 4).Description;
        Assert.Contains("160 rpm", rise);
        Assert.Contains("stabs have begun", rise);
        var lead = Definition(OverlayV10.MapsFor(1), OverlayV10.BlipGears, 6).Description;
        Assert.Contains("192 rpm", lead);
        Assert.Contains("0 waits for the engine speed itself", lead);
        var opening = Definition(OverlayV10.MapsFor(1), OverlayV8.Pops, 6).Description;
        Assert.Contains("rises 160 rpm during the load", opening);
        Assert.Contains("Below 4.0 °TPS", opening);
    }

    public static TheoryData<int, int, byte, int, bool> Rules => new()
    {
        // table, byte, value, minor, accepted
        { 139, 4, 5, 1, true }, { 139, 4, 31, 1, true }, { 139, 4, 4, 1, false }, { 139, 4, 32, 1, false }, { 139, 4, 0, 1, false },
        { 141, 6, 0, 1, true }, { 141, 6, 30, 1, true }, { 141, 6, 31, 1, false },
        { 138, 3, 255, 1, true }, { 138, 3, 211, 1, true }, { 138, 3, 128, 1, true }, { 138, 3, 127, 1, false },
        { 139, 5, 1, 1, false }, { 141, 7, 1, 1, false },
        // V10 (minor 0): the new bytes are reserved and the AFR stops at 210
        { 139, 4, 15, 0, false }, { 139, 4, 0, 0, true }, { 141, 6, 15, 0, false }, { 141, 6, 0, 0, true },
        { 138, 3, 211, 0, false }, { 138, 3, 210, 0, true },
    };

    [Theory, MemberData(nameof(Rules))]
    public void EachVersionAppliesItsOwnLimits(int table, int index, byte value, int minor, bool accepted) =>
        Assert.Equal(accepted ? null : OverlayReason.Range,
            OverlayV9.ValidateFor(OverlayV10.Version, table, With(table, minor, (index, value)), Group(table, minor), minor));

    [Fact]
    public void V101sDefaultsAreValid()
    {
        foreach (var table in new[] { 138, 139, 140, 141, 142 })
            Assert.Null(OverlayV9.ValidateFor(OverlayV10.Version, table, Group(table), Group(table), 1));
    }

    [Fact]
    public void AV101EcuTakesTheNewSettingsLive()
    {
        var ecu = Ecu(); var client = new OverlayClient(ecu); var detection = client.Detect();
        foreach (var (table, copy) in new[] { (139, With(139, 1, (4, 20))), (141, With(141, 1, (6, 30))), (138, With(138, 1, (3, 255))) })
        {
            Assert.Null(client.Send(table, copy, detection.Flash[table], client.Status()).Refused);
            var entry = client.Status().Tables[table];
            Assert.Equal(OverlayState.On, entry.State);
            Assert.Equal(copy, client.ReadCopy(table, entry));
        }
    }

    [Fact] // RF3: a V10 ECU refuses V10.1's bytes before anything is sent
    public void AV10EcuRefusesTheNewBytesBeforeSending()
    {
        var ecu = Ecu(minor: 0); var client = new OverlayClient(ecu); var detection = client.Detect();
        var requests = ecu.Requests.Count;
        Assert.Equal(OverlayReason.Range, client.Send(139, With(139, 0, (4, 15)), detection.Flash[139], client.Status()).Refused);
        Assert.Equal(OverlayReason.Range, client.Send(138, With(138, 0, (3, 255)), detection.Flash[138], client.Status()).Refused);
        Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3 or 9);
    }
}
