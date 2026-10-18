using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// V10's groups on a protocol 10 ECU and in a V10 bin: "Pops stabs" beside "Pops & bangs", the three blip groups together,
/// their limits refused before sending, the Fun tab's pages and the bin write. A V9 ECU shows none of them.
/// </summary>
public sealed class V10GroupsTests
{
    private static void Put(ShellViewModel shell, int offset, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            shell.Tune.Document!.Image.WriteElement(new XdfData(offset + i, 8, false, true, 1, 1, null), 0, bytes[i]);
    }

    /// <summary>
    /// The car bin made an overlay bin of <paramref name="binVersion"/> (its header, and V10's flashed groups from 0x4FF80, or
    /// pops' alone for V9), on <paramref name="tab"/>, with a fake ECU of <paramref name="ecu"/> on it when given.
    /// </summary>
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell, FakeOverlayV4Ecu? Ecu, byte[] Before)> Open(
        int? ecu, string tab, int binVersion = 10, int minor = 0)
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', (byte)binVersion, (byte)minor]);
        var groups = minor >= 2 ? V10Groups.DefaultsV102 : minor >= OverlayV10.V101Minor ? V10Groups.DefaultsV101 : V10Groups.Defaults;
        Put(shell, 0x4FF80, binVersion >= 10 ? groups : groups.AsSpan(0, 8));
        shell.SelectedTab = tab;
        var before = shell.Tune.Document!.Image.ToArray();
        FakeOverlayV4Ecu? fake = null;
        if (ecu is { } version)
        {
            fake = new FakeOverlayV4Ecu(before.ToArray(), version) { Minor = minor };
            await shell.LiveTuning.AttachLine(fake);
        }
        return (window, shell, fake, before);
    }

    private static void Close(Avalonia.Controls.Window window, ShellViewModel shell)
    {
        shell.LiveTuning.Dispose(); shell.Fun.Dispose(); window.Close();
    }

    private static LiveMapRow Row(LiveTuningViewModel page, int index) => page.Maps.Single(row => row.Map.Index == index);

    private static ValueEditorViewModel Field(IEnumerable<LiveScalarRow> rows, string label) => rows.Single(row => row.Label == label).Editor;

    private static void Choose(FunViewModel fun, string key) => fun.Selected = fun.Features.Single(f => f.Key == key);

    private static readonly string[] PopsLabels = ["Switch", "Duration", "Ignition angle", "AFR target", "Minimum engine speed",
        "Minimum coolant temperature", "Throttle opening", "Minimum vehicle speed"];

    private static readonly string[] BlipLabels = ["Switch", "Signs", "Minimum vehicle speed", "Maximum engine speed", "Maximum time",
        "Blip opening", "Hold opening", "Spark angle"];

    private static readonly string[] GearLabels = ["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st", "Tap threshold", "Tap length"];

    private static readonly string[] RatioLabels = ["rpm per km/h, 1st", "rpm per km/h, 2nd", "rpm per km/h, 3rd", "rpm per km/h, 4th"];

    [AvaloniaFact]
    public async Task PopsStabsShowsWithPops()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell, _, _) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            Assert.True(page.HasVerifiedCatalogue);
            Assert.Equal(143, page.Maps.Count);
            Assert.Equal("135 supported maps + 8 settings groups · load while RAM fits", page.CatalogueText);
            Assert.Equal(["Pops & bangs", "Pops stabs"], page.Maps.Where(row => row.Categories.Contains("Pops & Bangs")).Select(row => row.Title));
            var stabs = Row(page, OverlayV10.Stabs);
            Assert.Equal(new[] { "Pops & Bangs" }, stabs.Categories);
            Assert.Contains("0xCFF88", stabs.Detail);
            page.Selected = stabs;
            Assert.Equal(["Load time", "Stab size", "Stab length", "Gap"], page.ScalarFields.Select(f => f.Label));
            Assert.Equal(["s", "°TPS", "ms", "ms"], page.ScalarFields.Select(f => f.Editor.Units));
            // An XDF end that is the byte's own end (a floor of 0) narrows nothing, so the editor keeps only the storable one.
            Assert.Equal([(0.0, 5.0), (0.0, 30.0), (50.0, 300.0), (100.0, 1000.0)], page.ScalarFields.Select(f =>
                (f.Editor.Limits.XdfLow ?? f.Editor.Limits.StorableMin, f.Editor.Limits.XdfHigh ?? f.Editor.Limits.StorableMax)));
            Assert.Equal([20L, 75, 15, 25], page.ScalarFields.Select(f => f.Editor.RawValue));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheBlipGroupsShowTogether()
    {
        var (window, shell, _, _) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            Assert.Equal(["Downshift blip", "Downshift blip: gears and tap", "Downshift blip: gear ratios"],
                page.Maps.Where(row => row.Categories.Contains("Downshift blip")).Select(row => row.Title));
            Assert.All(new[] { 140, 141, 142 }, index => Assert.Equal(new[] { "Downshift blip" }, Row(page, index).Categories));
            page.MapSearch = "downshift";
            Assert.Equal([140, 141, 142], page.FilteredMaps.Select(row => row.Map.Index).Order());
            page.MapSearch = string.Empty;
            page.Selected = Row(page, OverlayV10.Blip);
            Assert.Equal(BlipLabels, page.ScalarFields.Select(f => f.Label));
            page.Selected = Row(page, OverlayV10.BlipGears);
            Assert.Equal(GearLabels, page.ScalarFields.Select(f => f.Label));
            page.Selected = Row(page, OverlayV10.BlipRatios);
            Assert.Equal(RatioLabels, page.ScalarFields.Select(f => f.Label));
            Assert.Equal([226L, 134, 89, 67], page.ScalarFields.Select(f => f.Editor.RawValue));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheBlipsChoicesAreNamed()
    {
        var (window, shell, _, _) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Blip);
            Assert.Equal([0L, 1, 2, 0x82, 0x97], page.ScalarFields[0].Editor.Choices!.Select(choice => choice.Value));
            Assert.Equal(["Off", "Always on", "Cruise control armed", "ASC off", "A/C on"], page.ScalarFields[0].Editor.Choices!.Select(c => c.Label));
            Assert.Equal(["While braking", "On a pedal tap", "Braking or tap"], page.ScalarFields[1].Editor.Choices!.Select(c => c.Label));
            page.Selected = Row(page, OverlayV10.BlipGears);
            Assert.All(page.ScalarFields.Take(4), f => Assert.Equal(["Off", "On"], f.Editor.Choices!.Select(c => c.Label)));
            Assert.Equal([1L, 1, 1, 0], page.ScalarFields.Take(4).Select(f => f.Editor.RawValue));
            Assert.Contains("tap only", page.ScalarFields[3].Description);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task AValueOutsideItsLimitIsRefusedBeforeSending()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Blip);
            var opening = Field(page.ScalarFields, "Blip opening"); opening.Input = "30.2"; opening.Apply();
            var requests = ecu!.Requests.Count;
            await page.SendSelectedAsync();
            Assert.Contains("outside its limits", page.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task AHoldAboveTheBlipOpeningIsRefusedBeforeSending()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Blip);
            var hold = Field(page.ScalarFields, "Hold opening"); hold.Input = "20.0"; Assert.True(hold.Apply());   // in range, above 15.0
            var requests = ecu!.Requests.Count;
            await page.SendSelectedAsync();
            Assert.Contains("the hold opening is above the blip opening", page.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheFunTabListsTheBlipWithItsSwitch()
    {
        var (window, shell, _, _) = await Open(null, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Assert.Equal(FunMode.Bin, fun.Mode);
            Assert.Equal(["lc", "nls", "ral", "hardcut", "pops", "blip"], fun.Features.Select(f => f.Key));
            var blip = fun.Features.Single(f => f.Key == FunFeatures.Blip);
            Assert.Equal("Downshift blip", blip.Title);
            Assert.True(blip.IsAvailable);
            Assert.Equal("Off", blip.SwitchText);
            Put(shell, 0x4FF90, [0x97]); fun.Refresh();
            Assert.Equal("A/C on", blip.SwitchText);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // spec 7.2 and 7.9: the stabs show inside "Pops & bangs", and the blip's page has a switch per downshift
    public async Task TheFunPagesHoldEveryGroupOfTheirFeature()
    {
        var (window, shell, _, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Assert.Equal(FunMode.Live, fun.Mode);
            Choose(fun, FunFeatures.Pops);
            Assert.Equal(PopsLabels.Concat(["Load time", "Stab size", "Stab length", "Gap"]), fun.ScalarRows.Select(r => r.Label));
            Choose(fun, FunFeatures.Blip);
            Assert.Equal(BlipLabels.Concat(GearLabels).Concat(RatioLabels), fun.ScalarRows.Select(r => r.Label));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task SendingTheBlipPagePutsItsThreeGroupsLive()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var on = Field(fun.ScalarRows, "Switch"); on.SelectedChoice = on.Choices!.Single(choice => choice.Value == 1);
            var first = Field(fun.ScalarRows, "2nd → 1st"); first.SelectedChoice = first.Choices!.Single(choice => choice.Value == 1);
            await fun.SendAsync();
            Assert.Null(fun.Problem);
            foreach (var index in new[] { OverlayV10.Blip, OverlayV10.BlipGears, OverlayV10.BlipRatios })
                Assert.Equal(OverlayState.On, ecu!.Entries[index].State);
            Assert.Equal(new byte[] { 1, 3, 0x1E, 0xBB, 0x0F, 0x4B, 0x19, 0x5A }, ecu!.Copies.AsSpan(ecu.Entries[OverlayV10.Blip].Offset, 8).ToArray());
            Assert.Equal(new byte[] { 1, 1, 1, 1, 0x15, 0x28, 0, 0 }, ecu.Copies.AsSpan(ecu.Entries[OverlayV10.BlipGears].Offset, 8).ToArray());
            Assert.Equal(OverlayState.Free, ecu.Entries[OverlayV8.Pops].State);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task AHoldAboveTheOpeningIsRefusedInFunAsOnThePage()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var hold = Field(fun.ScalarRows, "Hold opening"); hold.Input = "20.0"; Assert.True(hold.Apply());
            var requests = ecu!.Requests.Count;
            await fun.SendAsync();
            Assert.Contains("the hold opening is above the blip opening", fun.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // final review: a refusal must never leave the feature switched on with stale settings
    public async Task AnInvalidGearsGroupSendsNothingFromTheBlipPage()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var on = Field(fun.ScalarRows, "Switch"); on.SelectedChoice = on.Choices!.Single(choice => choice.Value == 1);
            var tap = Field(fun.ScalarRows, "Tap length"); tap.Input = "610"; tap.Apply();      // 61, one above the firmware's 60
            var requests = ecu!.Requests.Count;
            await fun.SendAsync();
            Assert.Contains("A blip setting is outside its limits", fun.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3 or 9);
            Assert.Equal(OverlayState.Free, ecu.Entries[OverlayV10.Blip].State);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // final review: the group holding the feature's switch goes last
    public async Task TheBlipPageSendsItsSwitchGroupLast()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var on = Field(fun.ScalarRows, "Switch"); on.SelectedChoice = on.Choices!.Single(choice => choice.Value == 1);
            var requests = ecu!.Requests.Count;
            await fun.SendAsync();
            Assert.Null(fun.Problem);
            var loads = ecu.Requests.Skip(requests).Where(p => p is [0x6D, 1, ..]).Select(p => (int)p[10]).ToArray();
            Assert.Equal([OverlayV10.BlipRatios, OverlayV10.BlipGears, OverlayV10.Blip], loads);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // final review: after a refusal the page says what went live, and the feature's switch stays as it was
    public async Task ARefusedGroupSaysWhatWentLiveAndKeepsTheSwitchOff()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var on = Field(fun.ScalarRows, "Switch"); on.SelectedChoice = on.Choices!.Single(choice => choice.Value == 1);
            ecu!.RefuseLoad = (OverlayV10.BlipGears, OverlayReason.Overlap);
            await fun.SendAsync();
            Assert.Equal(OverlayState.On, ecu.Entries[OverlayV10.BlipRatios].State);
            Assert.Equal(OverlayState.Free, ecu.Entries[OverlayV10.Blip].State);
            Assert.StartsWith("blip_gears does not fit.", fun.Problem);                        // the session's own no-room text
            Assert.Contains("Downshift blip: gear ratios went live; the rest, the switch included, was not sent.", fun.Problem);
            Assert.False(fun.IsOn);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // final review: the Fun switch follows the group that holds the feature's switch
    public async Task TheFunSwitchFollowsTheBlipsSwitchGroup()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.FunTab);
        var fun = shell.Fun;
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Blip);                                   // 140 live alone, from the Live page
            var on = Field(page.ScalarFields, "Switch"); on.SelectedChoice = on.Choices!.Single(choice => choice.Value == 1);
            await page.SendSelectedAsync();
            Assert.Null(page.Problem);
            Choose(fun, FunFeatures.Blip);
            Assert.True(fun.IsOn);
            Assert.True(fun.CanSwitch);
            fun.IsOn = false;
            await page.TickAsync();
            Assert.Equal(OverlayState.Ready, ecu!.Entries[OverlayV10.Blip].State);
            Assert.Equal(OverlayState.Free, ecu.Entries[OverlayV10.BlipGears].State);
            Assert.Null(page.Problem);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task OfflineAHoldAboveTheOpeningIsUndone()
    {
        var (window, shell, _, before) = await Open(null, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var hold = Field(fun.ScalarRows, "Hold opening"); hold.Input = "20.0"; hold.Apply();
            Assert.Equal(before[0x4FF96], shell.Tune.Document!.Image.ToArray()[0x4FF96]);
            var opening = Field(fun.ScalarRows, "Blip opening"); opening.Input = "10.0"; Assert.True(opening.Apply());
            Assert.Equal(50, shell.Tune.Document!.Image.ToArray()[0x4FF95]);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheOpeningHoverWarnsUnder4TpsOnV10Only()
    {
        foreach (var (version, warns) in new[] { (10, true), (9, false) })
        {
            var (window, shell, _, _) = await Open(version, ShellViewModel.LiveTuningTab, binVersion: version);
            var page = shell.LiveTuning;
            try
            {
                page.Selected = Row(page, OverlayV8.Pops);
                var hover = page.ScalarFields.Single(f => f.Label == "Throttle opening").Description;
                Assert.Equal(warns, hover.Contains("Below 4.0 °TPS the stabs' gaps blur", StringComparison.Ordinal));
            }
            finally { Close(window, shell); }
        }
    }

    [AvaloniaFact]
    public async Task AV9EcuShowsNoneOfTheNewGroups()
    {
        var (window, shell, _, _) = await Open(9, ShellViewModel.FunTab, binVersion: 9);
        try
        {
            Assert.Equal(139, shell.LiveTuning.Maps.Count);
            Assert.DoesNotContain(shell.LiveTuning.Maps, row => row.Map.Index > OverlayV8.Pops);
            var blip = shell.Fun.Features.Single(f => f.Key == FunFeatures.Blip);
            Assert.False(blip.IsAvailable);
            Assert.Equal("The ECU runs overlay V9; the blip needs V10. Flash the V10 bin to use it.", blip.Unavailable);
            Choose(shell.Fun, FunFeatures.Pops);
            Assert.Equal(PopsLabels, shell.Fun.ScalarRows.Select(r => r.Label));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task AV9BinOfflineShowsNoBlip()
    {
        var (window, shell, _, _) = await Open(null, ShellViewModel.FunTab, binVersion: 9);
        try
        {
            var blip = shell.Fun.Features.Single(f => f.Key == FunFeatures.Blip);
            Assert.False(blip.IsAvailable);
            Assert.Equal("This bin has overlay V9; the blip needs V10. Open the V10 bin to set it.", blip.Unavailable);
            Choose(shell.Fun, FunFeatures.Pops);
            Assert.Equal(PopsLabels, shell.Fun.ScalarRows.Select(r => r.Label));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task EditsOfTheNewGroupsGoIntoTheBin()
    {
        var (window, shell, _, before) = await Open(10, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            (int Index, string Label, string Value)[] edits =
            [
                (OverlayV10.Stabs, "Stab size", "20.0"), (OverlayV10.Blip, "Blip opening", "12.0"),
                (OverlayV10.BlipGears, "Tap length", "300"), (OverlayV10.BlipRatios, "rpm per km/h, 3rd", "45.0"),
            ];
            foreach (var (index, label, value) in edits)
            {
                page.Selected = Row(page, index);
                var field = Field(page.ScalarFields, label); field.Input = value; Assert.True(field.Apply());
                await page.SendSelectedAsync(); Assert.Null(page.Problem);
            }
            await page.CopyToTuneAsync(edits.Select(edit => Row(page, edit.Index)).ToList());
            var expected = before.ToArray(); expected[0x4FF89] = 100; expected[0x4FF95] = 60; expected[0x4FF9D] = 30; expected[0x4FFA2] = 90;
            var after = shell.Tune.Document!.Image.ToArray();
            Assert.Equal(expected.AsSpan(0x4FF80, 40).ToArray(), after.AsSpan(0x4FF80, 40).ToArray());
            Assert.Equal(before.AsSpan(0, 0x4FF80).ToArray(), after.AsSpan(0, 0x4FF80).ToArray());
            Assert.Equal(before.AsSpan(0x4FFA8).ToArray(), after.AsSpan(0x4FFA8).ToArray());
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // V10.1: the rise allowed once stabbing and the blip lead show after V10's settings, with V10.1's limits
    public async Task AV101EcuShowsTheRiseAndTheLead()
    {
        var (window, shell, _, _) = await Open(10, ShellViewModel.LiveTuningTab, minor: 1);
        var page = shell.LiveTuning;
        try
        {
            Assert.True(page.HasVerifiedCatalogue);
            page.Selected = Row(page, OverlayV10.Stabs);
            Assert.Equal(["Load time", "Stab size", "Stab length", "Gap", "Rise allowed during stabs"], page.ScalarFields.Select(f => f.Label));
            var rise = Field(page.ScalarFields, "Rise allowed during stabs");
            Assert.Equal(("rpm", 15L, 160.0, 992.0),
                (rise.Units, rise.RawValue, rise.Limits.XdfLow ?? rise.Limits.StorableMin, rise.Limits.XdfHigh ?? rise.Limits.StorableMax));
            page.Selected = Row(page, OverlayV10.BlipGears);
            Assert.Equal(GearLabels.Append("Blip lead"), page.ScalarFields.Select(f => f.Label));
            var lead = Field(page.ScalarFields, "Blip lead");
            Assert.Equal(("ms", 15L, 0.0, 300.0),
                (lead.Units, lead.RawValue, lead.Limits.XdfLow ?? lead.Limits.StorableMin, lead.Limits.XdfHigh ?? lead.Limits.StorableMax));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheRiseAndTheLeadGoLiveOnAV101Ecu()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.LiveTuningTab, minor: 1);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Stabs);
            var rise = Field(page.ScalarFields, "Rise allowed during stabs"); rise.Input = "640"; Assert.True(rise.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            page.Selected = Row(page, OverlayV10.BlipGears);
            var lead = Field(page.ScalarFields, "Blip lead"); lead.Input = "200"; Assert.True(lead.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            Assert.Equal(20, ecu!.Copies[ecu.Entries[OverlayV10.Stabs].Offset + 4]);
            Assert.Equal(20, ecu.Copies[ecu.Entries[OverlayV10.BlipGears].Offset + 6]);
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // V10.2: the pops AFR is the real AFR in petrol AFR, and the blip's gears page ends with the cap angle
    public async Task AV102EcuShowsTheRealAfrAndTheCap()
    {
        var (window, shell, _, _) = await Open(10, ShellViewModel.LiveTuningTab, minor: 2);
        var page = shell.LiveTuning;
        try
        {
            Assert.True(page.HasVerifiedCatalogue);
            page.Selected = Row(page, OverlayV8.Pops);
            var afr = Field(page.ScalarFields, "AFR target (petrol)");
            Assert.Equal(("AFR", 70L, 5.0, 14.7),
                (afr.Units, afr.RawValue, afr.Limits.XdfLow ?? afr.Limits.StorableMin, afr.Limits.XdfHigh ?? afr.Limits.StorableMax));
            page.Selected = Row(page, OverlayV10.BlipGears);
            Assert.Equal(GearLabels.Append("Blip lead").Append("Spark above the target"), page.ScalarFields.Select(f => f.Label));
            var cap = Field(page.ScalarFields, "Spark above the target");
            Assert.Equal(("°BTDC", 0L, -23.625, 30.0),
                (cap.Units, cap.RawValue, cap.Limits.XdfLow ?? cap.Limits.StorableMin, cap.Limits.XdfHigh ?? cap.Limits.StorableMax));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheRealAfrAndTheCapGoLiveOnAV102Ecu()
    {
        var (window, shell, ecu, _) = await Open(10, ShellViewModel.LiveTuningTab, minor: 2);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV8.Pops);
            var afr = Field(page.ScalarFields, "AFR target (petrol)"); afr.Input = "5.0"; Assert.True(afr.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            page.Selected = Row(page, OverlayV10.BlipGears);
            var cap = Field(page.ScalarFields, "Spark above the target"); cap.Input = "-10.125"; Assert.True(cap.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            Assert.Equal(50, ecu!.Copies[ecu.Entries[OverlayV8.Pops].Offset + 3]);
            Assert.Equal(36, ecu.Copies[ecu.Entries[OverlayV10.BlipGears].Offset + 7]);      // (-10.125 + 23.625) / 0.375
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // RF3: AFR 7.4 goes live on V10.1; a V10 ECU refuses it before anything is sent
    public async Task TheRicherAfrGoesLiveOnV101Only()
    {
        foreach (var (minor, sent) in new[] { (1, true), (0, false) })
        {
            var (window, shell, ecu, _) = await Open(10, ShellViewModel.LiveTuningTab, minor: minor);
            var page = shell.LiveTuning;
            try
            {
                page.Selected = Row(page, OverlayV8.Pops);
                var afr = Field(page.ScalarFields, "AFR target"); afr.Input = "7.4"; Assert.True(afr.Apply());
                Assert.Equal(255L, afr.RawValue);
                var requests = ecu!.Requests.Count;
                await page.SendSelectedAsync();
                if (sent) Assert.Null(page.Problem);
                else
                {
                    Assert.Equal("A pops setting is outside its limits. Nothing was sent.", page.Problem);
                    Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
                }
            }
            finally { Close(window, shell); }
        }
    }

    [AvaloniaFact]
    public async Task AV101BinOfflineShowsTheNewSettingsInFun()
    {
        var (window, shell, _, _) = await Open(null, ShellViewModel.FunTab, minor: 1);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Pops);
            Assert.Equal(PopsLabels.Concat(["Load time", "Stab size", "Stab length", "Gap", "Rise allowed during stabs"]),
                fun.ScalarRows.Select(r => r.Label));
            Choose(fun, FunFeatures.Blip);
            Assert.Equal(BlipLabels.Concat(GearLabels).Append("Blip lead").Concat(RatioLabels), fun.ScalarRows.Select(r => r.Label));
        }
        finally { Close(window, shell); }
    }

    private sealed class ManualTicker : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
