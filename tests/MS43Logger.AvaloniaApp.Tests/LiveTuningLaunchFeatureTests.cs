using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class LiveTuningV6Tests
{
    [CarAvaloniaFact]
    public async Task ScalarGroupsEditPrivately_SendTogether_AndCopyOnlyConfirmedOwnedBytes()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(time: new ManualTicker());
        try
        {
            shell.SelectedTab = ShellViewModel.LiveTuningTab;
            var before = shell.Tune.Document!.Image.ToArray();
            var ecu = new FakeOverlayV4Ecu(before, version: 6);
            var page = shell.LiveTuning; await page.AttachLine(ecu);
            Assert.True(page.IsV6); Assert.Equal(127, page.Maps.Count);
            Assert.Equal("124 supported maps + 3 settings groups · load while RAM fits", page.CatalogueText);
            Assert.Equal(0, page.MemoryUsed);

            page.Selected = page.Maps[124]; Assert.Equal(8, page.ScalarFields.Count); Assert.Null(page.Editor);
            Assert.True(page.CanSendSelected);
            var rpm = page.ScalarFields.Single(field => field.Key == "c_lc_n_max");
            var requests = ecu.Requests.Count;
            rpm.Editor.Input = "4800"; Assert.True(rpm.Editor.Apply()); Assert.Equal(150, rpm.Editor.RawValue);
            Assert.Equal(requests, ecu.Requests.Count); Assert.Equal(before, shell.Tune.Document.Image.ToArray());
            await page.SendSelectedAsync(); Assert.Null(page.Problem); Assert.Equal(8, page.MemoryUsed);
            Screens.Save(window, "live-v6-launch-control");
            rpm.Editor.Input = "5120"; Assert.True(rpm.Editor.Apply());
            await page.CopyToTuneAsync([page.Maps[124]]);
            Assert.Equal(150, shell.Tune.Document.Image.Bytes[0x7132F]);
            rpm.Editor.Input = "5216"; await page.TickAsync();
            Assert.Same(rpm, page.ScalarFields.Single(field => field.Key == "c_lc_n_max"));
            Assert.Equal("5216", rpm.Editor.Input);

            page.Selected = page.Maps[125]; Assert.Equal(8, page.ScalarFields.Count);
            var activation = page.ScalarFields.Single(field => field.Key == "c_conf_nls").Editor;
            activation.SelectedChoice = activation.Choices!.Single(choice => choice.Value == 2);
            Assert.Contains("cruise control LED", activation.SelectedChoice.Text);
            await page.SendSelectedAsync(); Assert.Null(page.Problem); Assert.Equal(16, page.MemoryUsed);
            Screens.Save(window, "live-v6-no-lift-shift");

            page.Selected = page.Maps[126]; Assert.Equal(7, page.ScalarFields.Count);
            Assert.Contains("next rolling anti-lag activation", page.ScalarNote);
            var afr = page.ScalarFields.Single(field => field.Key == "c_ral_af_target_ron98").Editor;
            afr.Input = "14.0"; Assert.True(afr.Apply());
            await page.SendSelectedAsync(); Assert.Null(page.Problem); Assert.Equal(24, page.MemoryUsed);
            afr.Input = "14.5"; Assert.True(afr.Apply());
            requests = ecu.Requests.Count;
            await page.CopyToTuneAsync([page.Maps[126]]);
            Assert.Equal(requests, ecu.Requests.Count);
            var expected = before.ToArray(); expected[0x7132F] = 150; expected[0x71342] = 140;
            Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
            Assert.Equal(before[0x71343], ecu.Copies[ecu.Entries[126].Offset + 7]);

            ecu.Reset(); await page.TickAsync();
            Assert.All(page.Maps.Skip(124), row => Assert.Equal(OverlayState.On, row.State));
            Assert.All(ecu.Entries.Skip(124), entry => Assert.Equal(OverlayState.On, entry.State));
            Assert.Equal(150, ecu.Copies[ecu.Entries[124].Offset + 3]);
            Assert.Equal(2, ecu.Copies[ecu.Entries[125].Offset]);
            Assert.Equal(140, ecu.Copies[ecu.Entries[126].Offset + 6]);
            Assert.True(page.Maps[126].HasUnsent);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Find.Named<StackPanel>(window, "LiveScalarPanel").IsEffectivelyVisible);
            Assert.Equal(24, Find.Named<ProgressBar>(window, "LiveMemoryBar").Value);
            Screens.Save(window, "live-v6-rolling-anti-lag");
            afr.Input = "15.0";
            requests = ecu.Requests.Count;
            await page.AttachLine(null);
            Assert.False(page.HasScalarEditor); Assert.False(page.CanSendSelected);
            Assert.Same(afr, page.ScalarFields.Single(field => field.Key == "c_ral_af_target_ron98").Editor);
            Assert.Equal(145, afr.RawValue); Assert.Equal("15.0", afr.Input);
            await page.SendSelectedAsync(); Assert.Equal(requests, ecu.Requests.Count);
            Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
            await page.AttachLine(ecu);
            Assert.Same(afr, page.ScalarFields.Single(field => field.Key == "c_ral_af_target_ron98").Editor);
            Assert.Equal(145, afr.RawValue); Assert.Equal("15.0", afr.Input);
            Assert.True(page.Maps[126].HasUnsent);
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
    }

    [CarAvaloniaFact]
    public async Task ScalarSelectionAndMemoryCostFollowTheV6Catalogue_ThenDisappearOnV5()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(time: new ManualTicker());
        try
        {
            var bytes = File.ReadAllBytes(TuneScreen.Car!);
            var page = shell.LiveTuning; await page.AttachLine(new FakeOverlayV4Ecu(bytes, version: 6));
            var ordinary = page.Maps.Where(row => row.Map.Index >= 5 && row.Map.Index < 124 && row.Map.Bytes == 64).Take(5).ToArray();
            foreach (var row in ordinary) { page.Selected = row; await page.SendSelectedAsync(); }
            page.MapSearch = "c_conf_ral";
            Assert.Single(page.FilteredMaps); Assert.Same(page.Maps[126], page.Selected);
            Assert.Contains("+ 24 tracking", page.Selected!.CapacityText);
            await page.SendSelectedAsync(); Assert.Null(page.Problem);
            Assert.Equal(352, page.MemoryUsed);
            await page.TickAsync(); Assert.Same(page.Maps[126], page.Selected); Assert.Equal(7, page.ScalarFields.Count);
            page.MapSearch = string.Empty;
            await page.AttachLine(new FakeOverlayV4Ecu(bytes, version: 5));
            Assert.False(page.IsV6); Assert.Equal(124, page.Maps.Count);
            Assert.Empty(page.ScalarFields); Assert.False(page.HasScalarEditor);
            Assert.Equal("124 supported maps · load while RAM fits", page.CatalogueText);
        }
        finally { shell.LiveTuning.Dispose(); window.Close(); }
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
