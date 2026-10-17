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
