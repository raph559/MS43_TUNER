using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class LiveTuningV4Tests
{
    [AvaloniaFact]
    public async Task CatalogueSelectionShowsActualMemoryAndLoadsAnotherMapAfterRemoval()
    {
        Assert.True(TuneScreen.Available, "The pinned car and XDF are required for this acceptance test.");
        var (window, shell) = await TuneScreen.OpenAsync();
        try
        {
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var ecu = new FakeOverlayV4Ecu(File.ReadAllBytes(TuneScreen.Car!));
            Assert.Equal("No live-tuning overlay in this BIN.", shell.LiveTuning.CatalogueText);
            Assert.False(shell.LiveTuning.HasVerifiedCatalogue);
            await shell.LiveTuning.AttachLine(ecu); var page = shell.LiveTuning;
            Assert.True(page.IsV4); Assert.Equal(124, page.Maps.Count);
            Assert.True(page.HasVerifiedCatalogue);
            Assert.Equal("124 supported maps · choose up to 5", page.CatalogueText);
            Assert.True(Find.Named<ListBox>(window, "LiveTuningMaps").IsEffectivelyVisible);
            Assert.All(page.Maps, row => Assert.True(row.CanLoad, row.Map.Key + " has no usable XDF definition"));
            Assert.Equal(932, page.MemoryCapacity); Assert.Equal(0, page.MemoryUsed);
            for (var i = 0; i < 5; i++) { page.Selected = page.Maps[i]; await page.SendSelectedAsync(); }
            Assert.Equal(932, page.MemoryUsed); Assert.Contains("5 / 5", page.AllocationText);
            page.Selected = page.Maps[5]; Assert.False(page.CanSendSelected);
            await page.SetOnAsync(page.Maps[0], false); await page.FreeAsync(page.Maps[0]);
            Assert.True(page.CanSendSelected); await page.SendSelectedAsync();
            Assert.Equal(644, page.MemoryUsed); Assert.Equal(OverlayState.On, page.Maps[5].State);
            page.MapSearch = page.Maps[5].Map.Key;
            Assert.Single(page.FilteredMaps);
            page.MapSearch = string.Empty; page.LoadedOnly = true;
            Assert.Equal(5, page.FilteredMaps.Count);
            page.Selected = page.Maps[1]; window.Width = 1400; window.Height = 900;
            await page.TickAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(page.Maps[1], page.Selected); Assert.NotNull(page.Editor);
            Assert.Equal(page.MemoryUsed, Find.Named<ProgressBar>(window, "LiveMemoryBar").Value);
            Screens.Save(window, "live-v4-allocated");
            await page.AttachLine(null);
            Assert.False(page.HasVerifiedCatalogue);
            Assert.Equal("No live-tuning overlay in this BIN.", page.CatalogueText);
            await page.AttachLine(new FakeOverlayEcu(File.ReadAllBytes(TuneScreen.Car!), hasOverlay: false));
            Assert.Equal("No live-tuning overlay on this ECU.", page.CatalogueText);
            Assert.False(page.IsV4);
            Assert.False(Find.Named<ListBox>(window, "LiveTuningMaps").IsEffectivelyVisible);
            Assert.False(Find.Named<Expander>(window, "LiveLimiterPanel").IsEffectivelyVisible);
            Screens.Save(window, "live-stock-ms4x-no-overlay");
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task LimiterSendsOnlyOnRequestAndRejectsUnrepresentableRpm()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync();
        try
        {
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var ecu = new FakeOverlayV4Ecu(File.ReadAllBytes(TuneScreen.Car!));
            await shell.LiveTuning.AttachLine(ecu); var page = shell.LiveTuning;
            Assert.Equal("7008", page.LimiterGears[0].Rpm);
            page.LimiterGears[0].Rpm = "6000"; page.LimiterEnabled = true;
            Assert.DoesNotContain(ecu.Requests, p => p is [0x6D, 9, ..]);
            await page.SendLimiterAsync(); Assert.Contains("multiples of 32", page.Problem);
            page.LimiterGears[0].Rpm = "6016"; await page.SendLimiterAsync();
            Assert.Null(page.Problem); Assert.Equal(188, ecu.Limiter.GearLimits[0]); Assert.True(ecu.Limiter.Enabled);
            Assert.True(page.AllEdited);
            await page.SetAllEditedAsync(false); Assert.False(page.AllEdited);
            await page.SetAllEditedAsync(true); Assert.True(page.AllEdited);
            window.Width = 1400; window.Height = 1000;
            var limiterPanel = Find.Named<Expander>(window, "LiveLimiterPanel");
            foreach (var theme in new[] { "night", "day" })
            {
                shell.IsNight = theme == "night";
                shell.IsDay = theme == "day";
                limiterPanel.IsExpanded = false;
                limiterPanel.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var header = Find.Named<ToggleButton>(limiterPanel, "PART_ToggleButton");
                Find.Named<Button>(window, "LiveTuningCopy").Focus(NavigationMethod.Pointer);
                window.MouseMove(new Point(700, 850));
                Screens.Save(window, $"live-limiter-{theme}-closed");
                window.MouseMove(header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value);
                Screens.Save(window, $"live-limiter-{theme}-hover");
                window.MouseMove(new Point(700, 850));
                Assert.True(header.Focus(NavigationMethod.Tab));
                Screens.Save(window, $"live-limiter-{theme}-focus");
                Assert.Contains(":focus-visible", header.Classes);
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.True(limiterPanel.IsExpanded);
                Screens.Save(window, $"live-limiter-{theme}-open");
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Assert.False(limiterPanel.IsExpanded);
            }
            shell.IsNight = true;
            limiterPanel.IsExpanded = true;
            Screens.Save(window, "live-v4-limiter");
            Find.Named<Button>(window, "LiveLimiterCopy").BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Screens.Save(window, "live-v4-limiter-controls");
            await page.RevertAllAsync(); Assert.False(ecu.Limiter.Enabled); Assert.Equal(0, page.MemoryUsed);
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task CopyLimiterUsesConfirmedValuesAndLeavesOtherCalibrationUntouched()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync();
        try
        {
            var ecu = new FakeOverlayV4Ecu(File.ReadAllBytes(TuneScreen.Car!));
            var page = shell.LiveTuning; await page.AttachLine(ecu);
            var before = shell.Tune.Document!.Image.ToArray();
            page.CopyLimiterToTune(); Assert.Contains("Send the limiter", page.Problem);
            page.LimiterGears[0].Rpm = "6016"; page.LimiterHysteresis = "96"; page.LimiterMode = 0; page.LimiterEnabled = true;
            await page.SendLimiterAsync(); Assert.Null(page.Problem);
            page.LimiterGears[0].Rpm = "6208"; page.LimiterHysteresis = "128"; page.LimiterMode = 1;
            var requests = ecu.Requests.Count;
            page.CopyLimiterToTune(); Assert.StartsWith("Copied the confirmed", page.Problem);
            Assert.Equal(requests, ecu.Requests.Count);
            var expected = before.ToArray(); expected[0x75D47] = 188; expected[0x70BFF] = 3; expected[0x7131E] = 0;
            Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
            for (var i = 0; i < 3; i++) Assert.True(shell.Tune.History!.Undo());
            Assert.Equal(before, shell.Tune.Document.Image.ToArray());
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }
}
