using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The pops group (138): no XDF entry exists for it, so its eight settings carry their own titles, descriptions, units,
/// scaling and limits.
/// </summary>
public sealed class LiveTuningPopsTests
{
    private static readonly byte[] Defaults = OverlayV104Settings.Defaults[..8];

    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell, LiveTuningViewModel Page, FakeOverlayEcu Ecu, byte[] Before)> OpenPopsSessionAsync()
    {
        var (window, shell) = await TuneScreen.OpenAsync(time: new ManualTicker());
        shell.SelectedTab = ShellViewModel.LiveTuningTab;
        // The open bin is the V10.4 bin: its overlay area holds the flashed pops settings.
        for (var i = 0; i < 8; i++)
            shell.Tune.Document!.Image.WriteElement(new MS43Logger.Core.Tuning.XdfData(0x4FF80 + i, 8, false, true, 1, 1, null), 0, Defaults[i]);
        var before = shell.Tune.Document!.Image.ToArray();
        var ecu = new FakeOverlayEcu(before.ToArray());
        var page = shell.LiveTuning;
        await page.AttachLine(ecu);
        return (window, shell, page, ecu, before);
    }

    private static LiveMapRow Pops(LiveTuningViewModel page) => page.Maps.Single(row => row.Map.Index == OverlaySettings.Pops);

    [CarAvaloniaFact]
    public async Task PopsGroupIsListedWithItsCategoryAndDescriptions()
    {
        Assert.True(TuneScreen.Available);
        var (window, _, page, _, _) = await OpenPopsSessionAsync();
        try
        {
            Assert.True(page.HasVerifiedCatalogue);
            Assert.Equal(143, page.Maps.Count);
            Assert.Equal("135 supported maps + 7 settings groups · load while RAM fits", page.CatalogueText);
            var pops = Pops(page);
            Assert.Equal("Pops & bangs", pops.Title);
            Assert.Equal(new[] { "Pops & Bangs" }, pops.Categories);
            Assert.Contains("Pops ignition angle: The fixed spark angle", pops.Description);
            Assert.Contains("cruise control", pops.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0xCFF80", pops.Detail);

            page.Selected = pops;
            Assert.Equal(8, page.ScalarFields.Count);
            Assert.Equal(["Switch", "Duration", "Ignition angle", "AFR target (petrol)", "Minimum engine speed",
                "Minimum coolant temperature", "Throttle opening", "Minimum vehicle speed"], page.ScalarFields.Select(f => f.Label));
            var angle = page.ScalarFields[2].Editor;
            Assert.Equal(0, angle.RawValue);
            Assert.Equal("-23.625", angle.ReadingText);
            Assert.Equal(5, page.ScalarFields[0].Editor.Choices!.Count);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task TheSwitchNamesEachTrigger_AndTheButtonBitsAreNotRead()
    {
        var (window, _, page, ecu, _) = await OpenPopsSessionAsync();
        try
        {
            page.Selected = Pops(page);
            var choices = page.ScalarFields[0].Editor.Choices!;
            // The car checks: the A/C button sets bit 7 of 615h byte 0 (29 Oct), so "A/C on" is 97h; switching ASC off sets
            // bit 2 of 153h byte 0 (3 Nov), so "ASC off" is 82h.
            Assert.Equal([0L, 1, 2, 0x82, 0x97], choices.Select(choice => choice.Value));
            Assert.Equal(["Off", "Always on", "Cruise control armed", "ASC off", "A/C on"], choices.Select(choice => choice.Label));
            await page.TickAsync();
            Assert.DoesNotContain(ecu.Requests, p => p[0] == 0x06 && p[2] == 0x00 && p[3] == 0xEF && p[4] is 0x47 or 0x37);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task SearchFindsPopsByCruiseAndByAfr()
    {
        var (window, _, page, _, _) = await OpenPopsSessionAsync();
        try
        {
            page.MapSearch = "pops cruise";
            Assert.Contains(Pops(page), page.FilteredMaps);
            page.MapSearch = "afr target";
            Assert.Contains(Pops(page), page.FilteredMaps);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task PopsSettingsEditSendAndReadBack()
    {
        var (window, _, page, ecu, _) = await OpenPopsSessionAsync();
        try
        {
            page.Selected = Pops(page);
            var switchField = page.ScalarFields[0].Editor;
            switchField.SelectedChoice = switchField.Choices!.Single(choice => choice.Value == 2);
            var angle = page.ScalarFields[2].Editor;
            angle.Input = "-20.25"; Assert.True(angle.Apply()); Assert.Equal(9, angle.RawValue);
            var afr = page.ScalarFields[3].Editor;
            Assert.Equal("7.0", afr.ReadingText);
            afr.Input = "12.0"; Assert.True(afr.Apply()); Assert.Equal(120, afr.RawValue);
            afr.Input = "11.5"; Assert.True(afr.Apply()); Assert.Equal(115, afr.RawValue);
            await page.SendSelectedAsync();
            Assert.Null(page.Problem);
            var entry = ecu.Entries[OverlaySettings.Pops];
            Assert.Equal(OverlayState.On, entry.State);
            Assert.Equal(new byte[] { 2, 0x28, 9, 115, 0x7D, 0x9E, 0x1E, 0x14 }, ecu.Copies.AsSpan(entry.Offset, 8).ToArray());
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task OutOfRangePopsValueIsRefusedNotWarned()
    {
        var (window, _, page, ecu, _) = await OpenPopsSessionAsync();
        try
        {
            page.Selected = Pops(page);
            var rpm = page.ScalarFields[4].Editor;
            rpm.Input = "1504"; rpm.Apply();
            var requests = ecu.Requests.Count;
            await page.SendSelectedAsync();
            Assert.Contains("outside its limits", page.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { page.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task CopyToTuneWritesTheEightBytesIntoTheOverlayArea()
    {
        var (window, shell, page, _, before) = await OpenPopsSessionAsync();
        try
        {
            page.Selected = Pops(page);
            var duration = page.ScalarFields[1].Editor;
            duration.Input = "3.0"; Assert.True(duration.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            await page.CopyToTuneAsync([Pops(page)]);
            var expected = before.ToArray(); expected[0x4FF81] = 30;
            // The bin keeps every other byte; MS4X Flasher corrects the checksums when it writes, as for any edit.
            var after = shell.Tune.Document!.Image.ToArray();
            Assert.Equal(expected.AsSpan(0x4FF80, 8).ToArray(), after.AsSpan(0x4FF80, 8).ToArray());
            Assert.Equal(before.AsSpan(0, 0x4FF80).ToArray(), after.AsSpan(0, 0x4FF80).ToArray());
            Assert.Equal(before.AsSpan(0x4FF88).ToArray(), after.AsSpan(0x4FF88).ToArray());
        }
        finally { page.Dispose(); window.Close(); }
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
