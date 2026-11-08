using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class LiveTuningV5Tests
{
    [CarAvaloniaFact]
    public async Task MoreThanFiveMapsShowTheirTrackingCost_AndRemainSelectableAfterPolling()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync();
        try
        {
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var ecu = new FakeOverlayV4Ecu(File.ReadAllBytes(TuneScreen.Car!), version: 5);
            var page = shell.LiveTuning; await page.AttachLine(ecu);
            Assert.True(page.IsV5); Assert.Equal("124 supported maps · load while RAM fits", page.CatalogueText);
            var rows = page.Maps.Where(row => row.Map.Index >= 5 && row.Map.Bytes == 64).Take(12).ToArray();
            for (var i = 0; i < rows.Length; i++)
            {
                page.Selected = rows[i]; Assert.True(page.CanSendSelected);
                if (i >= 5) Assert.Contains(i == 5 ? "+ 24 tracking" : "+ 8 tracking", rows[i].CapacityText);
                await page.SendSelectedAsync(); Assert.Null(page.Problem);
            }
            Assert.Equal(840, page.MemoryUsed); Assert.Equal(932, page.MemoryCapacity);
            Assert.Contains("12 maps loaded", page.AllocationText); Assert.Contains("72 bytes", page.AllocationText);
            Assert.DoesNotContain("/ 5", page.AllocationText);
            page.LoadedOnly = true; Assert.Equal(12, page.FilteredMaps.Count);
            await page.TickAsync(); Assert.Same(rows[^1], page.Selected); Assert.NotNull(page.Editor);
            window.Width = 1440; window.Height = 1000; Dispatcher.UIThread.RunJobs();
            Assert.Equal(840, Find.Named<ProgressBar>(window, "LiveMemoryBar").Value);
            Screens.Save(window, "live-v5-twelve-maps");
            await page.SetOnAsync(rows[^1], false); await page.FreeAsync(rows[^1]);
            Assert.Equal(768, page.MemoryUsed); Assert.Equal(11, page.FilteredMaps.Count);
            page.LoadedOnly = false; page.Selected = rows[^1];
            Assert.True(page.CanSendSelected); await page.SendSelectedAsync(); Assert.Equal(840, page.MemoryUsed);
            await page.RevertAllAsync(); Assert.Equal(0, page.MemoryUsed);
            await page.AttachLine(new FakeOverlayV4Ecu(File.ReadAllBytes(TuneScreen.Car!)));
            Assert.False(page.IsV5); Assert.Contains("choose up to 5", page.CatalogueText);
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }
}
