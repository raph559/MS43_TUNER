using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The current overlay's groups (V10.4) on its ECU and in its bin: "Pops & bangs" alone (139 is reserved), the three blip
/// groups together, their limits refused before sending, the Fun tab's pages and the bin write. An older overlay, on the
/// ECU or in the bin, shows none of them.
/// </summary>
public sealed class LiveTuningFeatureGroupsTests
{
    private static void Put(ShellViewModel shell, int offset, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            shell.Tune.Document!.Image.WriteElement(new XdfData(offset + i, 8, false, true, 1, 1, null), 0, bytes[i]);
    }

    /// <summary>
    /// The car bin made an overlay bin of <paramref name="version"/>.<paramref name="minor"/> (its header, and V10.4's flashed
    /// groups from 0x4FF80), on <paramref name="tab"/>, with a fake ECU of the same header on it when <paramref name="ecu"/>.
    /// </summary>
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell, FakeOverlayEcu? Ecu, byte[] Before)> Open(
        bool ecu, string tab, int version = OverlayV10.Version, int minor = OverlayV10.Minor)
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', (byte)version, (byte)minor]);
        Put(shell, 0x4FF80, OverlayV104Settings.Defaults);
        shell.SelectedTab = tab;
        var before = shell.Tune.Document!.Image.ToArray();
        FakeOverlayEcu? fake = null;
        if (ecu)
        {
            fake = new FakeOverlayEcu(before.ToArray()) { HeaderVersion = version, Minor = minor };
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

    private static (double Low, double High) Range(ValueEditorViewModel field) =>
        (field.Limits.XdfLow ?? field.Limits.StorableMin, field.Limits.XdfHigh ?? field.Limits.StorableMax);

    private static readonly string[] PopsLabels = ["Switch", "Duration", "Ignition angle", "AFR target (petrol)", "Minimum engine speed",
        "Minimum coolant temperature", "Throttle opening", "Minimum vehicle speed"];

    private static readonly string[] BlipLabels = ["Switch", "Signs", "Minimum vehicle speed", "Maximum engine speed", "Maximum time",
        "Blip opening", "Hold opening", "Spark angle"];

    private static readonly string[] GearLabels = ["5th → 4th", "4th → 3rd", "3rd → 2nd", "2nd → 1st", "Tap threshold", "Tap length",
        "Blip lead", "Spark above the target"];

    private static readonly string[] RatioLabels = ["rpm per km/h, 1st", "rpm per km/h, 2nd", "rpm per km/h, 3rd", "rpm per km/h, 4th"];

    private static readonly string[] StabLabels = ["Load time", "Stab size", "Stab length", "Gap", "Rise allowed during stabs"];

    [CarAvaloniaFact] // 139 is in the verified catalogue but never listed, and pops shows alone
    public async Task PopsShowsAloneAndNoStabAnywhere()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell, _, _) = await Open(true, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            Assert.True(page.HasVerifiedCatalogue);
            Assert.Equal(143, page.Maps.Count);
            Assert.Equal("135 supported maps + 7 settings groups · load while RAM fits", page.CatalogueText);
            Assert.Equal(["Pops & bangs"], page.FilteredMaps.Where(row => row.Categories.Contains("Pops & Bangs")).Select(row => row.Title));
            Assert.True(Row(page, OverlayV10.Reserved).Map.Reserved);
            Assert.DoesNotContain(page.FilteredMaps, row => row.Map.Index == OverlayV10.Reserved);
            Assert.DoesNotContain(page.FilteredMaps, row => row.Title.Contains("stab", StringComparison.OrdinalIgnoreCase));
            page.Selected = Row(page, OverlayV8.Pops);
            Assert.Equal(PopsLabels, page.ScalarFields.Select(f => f.Label));
            Assert.DoesNotContain(page.ScalarFields, f => f.Description.Contains("stab", StringComparison.OrdinalIgnoreCase));
            shell.SelectedTab = ShellViewModel.FunTab;
            Choose(shell.Fun, FunFeatures.Pops);
            Assert.Equal(PopsLabels, shell.Fun.ScalarRows.Select(r => r.Label));
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task TheBlipGroupsShowTogether()
    {
        var (window, shell, _, _) = await Open(true, ShellViewModel.LiveTuningTab);
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

    [CarAvaloniaFact] // the real AFR, the blip lead and the spark above the target, with the firmware's limits
    public async Task TheNewerSettingsShowTheirLimits()
    {
        var (window, shell, _, _) = await Open(true, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV8.Pops);
            var afr = Field(page.ScalarFields, "AFR target (petrol)");
            Assert.Equal(("AFR", 70L, (5.0, 14.7)), (afr.Units, afr.RawValue, Range(afr)));
            page.Selected = Row(page, OverlayV10.BlipGears);
            var lead = Field(page.ScalarFields, "Blip lead");
            Assert.Equal(("ms", 15L, (0.0, 300.0)), (lead.Units, lead.RawValue, Range(lead)));
            var cap = Field(page.ScalarFields, "Spark above the target");
            Assert.Equal(("°BTDC", 0L, (-23.625, 30.0)), (cap.Units, cap.RawValue, Range(cap)));
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task TheRealAfrTheLeadAndTheCapGoLive()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV8.Pops);
            var afr = Field(page.ScalarFields, "AFR target (petrol)"); afr.Input = "5.0"; Assert.True(afr.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            page.Selected = Row(page, OverlayV10.BlipGears);
            var lead = Field(page.ScalarFields, "Blip lead"); lead.Input = "200"; Assert.True(lead.Apply());
            var cap = Field(page.ScalarFields, "Spark above the target"); cap.Input = "-10.125"; Assert.True(cap.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            Assert.Equal(50, ecu!.Copies[ecu.Entries[OverlayV8.Pops].Offset + 3]);
            Assert.Equal(20, ecu.Copies[ecu.Entries[OverlayV10.BlipGears].Offset + 6]);
            Assert.Equal(36, ecu.Copies[ecu.Entries[OverlayV10.BlipGears].Offset + 7]);      // (-10.125 + 23.625) / 0.375
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task TheBlipsChoicesAreNamed()
    {
        var (window, shell, _, _) = await Open(true, ShellViewModel.LiveTuningTab);
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

    [CarAvaloniaFact]
    public async Task AValueOutsideItsLimitIsRefusedBeforeSending()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.LiveTuningTab);
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

    [CarAvaloniaFact]
    public async Task AHoldAboveTheBlipOpeningIsRefusedBeforeSending()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            page.Selected = Row(page, OverlayV10.Blip);
            var opening = Field(page.ScalarFields, "Blip opening"); opening.Input = "10.0"; Assert.True(opening.Apply());
            var hold = Field(page.ScalarFields, "Hold opening"); hold.Input = "20.0"; Assert.True(hold.Apply());   // in range, above 10.0
            var requests = ecu!.Requests.Count;
            await page.SendSelectedAsync();
            Assert.Contains("the hold opening is above the blip opening", page.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task TheFunTabListsTheBlipWithItsSwitch()
    {
        var (window, shell, _, _) = await Open(false, ShellViewModel.FunTab);
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

    [CarAvaloniaFact] // the blip's page has a switch per downshift
    public async Task TheFunPagesHoldEveryGroupOfTheirFeature()
    {
        foreach (var live in new[] { true, false })
        {
            var (window, shell, _, _) = await Open(live, ShellViewModel.FunTab);
            var fun = shell.Fun;
            try
            {
                Assert.Equal(live ? FunMode.Live : FunMode.Bin, fun.Mode);
                Choose(fun, FunFeatures.Pops);
                Assert.Equal(PopsLabels, fun.ScalarRows.Select(r => r.Label));
                Choose(fun, FunFeatures.Blip);
                Assert.Equal(BlipLabels.Concat(GearLabels).Concat(RatioLabels), fun.ScalarRows.Select(r => r.Label));
                Assert.DoesNotContain(fun.ScalarRows, r => StabLabels.Contains(r.Label));
            }
            finally { Close(window, shell); }
        }
    }

    [CarAvaloniaFact]
    public async Task SendingTheBlipPagePutsItsThreeGroupsLive()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
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
            Assert.Equal(new byte[] { 1, 3, 0x1E, 0xBB, 0x0F, 0x96, 0x19, 0x75 }, ecu!.Copies.AsSpan(ecu.Entries[OverlayV10.Blip].Offset, 8).ToArray());
            Assert.Equal(new byte[] { 1, 1, 1, 1, 0x15, 0x28, 0x0F, 0 }, ecu.Copies.AsSpan(ecu.Entries[OverlayV10.BlipGears].Offset, 8).ToArray());
            Assert.Equal(OverlayState.Free, ecu.Entries[OverlayV8.Pops].State);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task AHoldAboveTheOpeningIsRefusedInFunAsOnThePage()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var opening = Field(fun.ScalarRows, "Blip opening"); opening.Input = "10.0"; Assert.True(opening.Apply());
            var hold = Field(fun.ScalarRows, "Hold opening"); hold.Input = "20.0"; Assert.True(hold.Apply());
            var requests = ecu!.Requests.Count;
            await fun.SendAsync();
            Assert.Contains("the hold opening is above the blip opening", fun.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // a refusal must never leave the feature switched on with stale settings
    public async Task AnInvalidGearsGroupSendsNothingFromTheBlipPage()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
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

    [CarAvaloniaFact] // the group holding the feature's switch goes last
    public async Task TheBlipPageSendsItsSwitchGroupLast()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
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

    [CarAvaloniaFact] // after a refusal the page says what went live, and the feature's switch stays as it was
    public async Task ARefusedGroupSaysWhatWentLiveAndKeepsTheSwitchOff()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
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

    [CarAvaloniaFact] // the Fun switch follows the group that holds the feature's switch
    public async Task TheFunSwitchFollowsTheBlipsSwitchGroup()
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab);
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

    [CarAvaloniaFact]
    public async Task OfflineAHoldAboveTheOpeningIsUndone()
    {
        var (window, shell, _, before) = await Open(false, ShellViewModel.FunTab);
        var fun = shell.Fun;
        try
        {
            Choose(fun, FunFeatures.Blip);
            var opening = Field(fun.ScalarRows, "Blip opening"); opening.Input = "10.0"; Assert.True(opening.Apply());
            Assert.Equal(50, shell.Tune.Document!.Image.ToArray()[0x4FF95]);
            var hold = Field(fun.ScalarRows, "Hold opening"); hold.Input = "20.0"; hold.Apply();
            Assert.Equal(before[0x4FF96], shell.Tune.Document!.Image.ToArray()[0x4FF96]);
        }
        finally { Close(window, shell); }
    }

    public static TheoryData<int, int> OlderOverlays => new() { { 9, 0 }, { 10, 2 } };

    [CarAvaloniaTheory, MemberData(nameof(OlderOverlays))]
    public async Task AnOlderEcuIsRefusedAndShowsNoneOfTheGroups(int version, int minor)
    {
        var (window, shell, ecu, _) = await Open(true, ShellViewModel.FunTab, version, minor);
        try
        {
            var label = minor == 0 ? $"{version}" : $"{version}.{minor}";
            Assert.False(shell.LiveTuning.HasVerifiedCatalogue);
            Assert.Equal($"Overlay V{label} not supported: flash the V10.4 bin.", shell.LiveTuning.CatalogueText);
            Assert.DoesNotContain(ecu!.Requests, p => p[0] == 0x6D);
            Assert.Equal(FunMode.Bin, shell.Fun.Mode);
            foreach (var key in new[] { FunFeatures.Pops, FunFeatures.Blip })
            {
                var feature = shell.Fun.Features.Single(f => f.Key == key);
                Assert.False(feature.IsAvailable);
                Assert.Contains($"This bin has overlay V{label}; the app works with V10.4 only.", feature.Unavailable);
            }
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task AnOlderBinOfflineShowsNoPopsOrBlip()
    {
        var (window, shell, _, _) = await Open(false, ShellViewModel.FunTab, version: 9, minor: 0);
        try
        {
            var blip = shell.Fun.Features.Single(f => f.Key == FunFeatures.Blip);
            Assert.False(blip.IsAvailable);
            Assert.Equal("This bin has overlay V9; the app works with V10.4 only. Open the V10.4 bin to set the blip.", blip.Unavailable);
            Assert.False(shell.Fun.Features.Single(f => f.Key == FunFeatures.Pops).IsAvailable);
            Assert.True(shell.Fun.Features.Single(f => f.Key == FunFeatures.LaunchControl).IsAvailable);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task EditsOfTheGroupsGoIntoTheBin()
    {
        var (window, shell, _, before) = await Open(true, ShellViewModel.LiveTuningTab);
        var page = shell.LiveTuning;
        try
        {
            (int Index, string Label, string Value)[] edits =
            [
                (OverlayV8.Pops, "Duration", "3.0"), (OverlayV10.Blip, "Blip opening", "12.0"),
                (OverlayV10.BlipGears, "Tap length", "300"), (OverlayV10.BlipRatios, "rpm per km/h, 3rd", "45.0"),
            ];
            foreach (var (index, label, value) in edits)
            {
                page.Selected = Row(page, index);
                var field = Field(page.ScalarFields, label); field.Input = value; Assert.True(field.Apply());
                await page.SendSelectedAsync(); Assert.Null(page.Problem);
            }
            await page.CopyToTuneAsync(edits.Select(edit => Row(page, edit.Index)).ToList());
            var expected = before.ToArray(); expected[0x4FF81] = 30; expected[0x4FF95] = 60; expected[0x4FF9D] = 30; expected[0x4FFA2] = 90;
            var after = shell.Tune.Document!.Image.ToArray();
            Assert.Equal(expected.AsSpan(0x4FF80, 40).ToArray(), after.AsSpan(0x4FF80, 40).ToArray());
            Assert.Equal(before.AsSpan(0, 0x4FF80).ToArray(), after.AsSpan(0, 0x4FF80).ToArray());
            Assert.Equal(before.AsSpan(0x4FFA8).ToArray(), after.AsSpan(0x4FFA8).ToArray());
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
