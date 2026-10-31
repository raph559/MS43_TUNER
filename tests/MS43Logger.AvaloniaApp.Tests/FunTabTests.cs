using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The Fun tab: LC, NLS, RAL, hardcut and pops in one place. Connected (a verified overlay) it edits the live page's
/// copy and sends through the live page; without a car it edits the open bin through Tune's history.
/// </summary>
public sealed class FunTabTests
{
    private static readonly byte[] PopsDefaults = [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14];

    private static void Put(ShellViewModel shell, int offset, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            shell.Tune.Document!.Image.WriteElement(new XdfData(offset + i, 8, false, true, 1, 1, null), 0, bytes[i]);
    }

    /// <summary>The car bin, made a V8 bin (overlay header version 8 and the pops defaults) when asked.</summary>
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell, FunViewModel Fun, FakeOverlayV4Ecu? Ecu)> Open(
        int? ecu = null, bool v8Bin = false)
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        if (v8Bin)
        {
            Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', 8, 0]);
            Put(shell, 0x4FF80, PopsDefaults);
        }
        shell.SelectedTab = ShellViewModel.FunTab;
        FakeOverlayV4Ecu? fake = null;
        if (ecu is { } version)
        {
            fake = new FakeOverlayV4Ecu(shell.Tune.Document!.Image.ToArray(), version);
            await shell.LiveTuning.AttachLine(fake);
        }
        return (window, shell, shell.Fun, fake);
    }

    private static void Close(Avalonia.Controls.Window window, ShellViewModel shell)
    {
        shell.LiveTuning.Dispose(); shell.Fun.Dispose(); window.Close();
    }

    private static void Choose(FunViewModel fun, string key) => fun.Selected = fun.Features.Single(f => f.Key == key);

    private static LiveScalarRow Row(FunViewModel fun, string label) => fun.ScalarRows.Single(r => r.Label == label);

    [CarAvaloniaTheory]
    [InlineData(false, new[] { "lc", "nls", "ral", "hardcut" })]
    [InlineData(true, new[] { "lc", "nls", "ral", "hardcut", "pops" })]
    public async Task OfflineFeaturesFollowTheBin(bool v8Bin, string[] keys)
    {
        var (window, shell, fun, _) = await Open(v8Bin: v8Bin);
        try
        {
            Assert.Equal(FunMode.Bin, fun.Mode);
            Assert.StartsWith("No car · editing the open bin ", fun.ModeText);
            Assert.Equal(FunFeatures.Order, fun.Features.Select(f => f.Key));
            Assert.Equal(keys, fun.Features.Where(f => f.IsAvailable).Select(f => f.Key));
            if (v8Bin)
            {
                Choose(fun, "hardcut"); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Screens.Save(window, "fun-bin-hardcut");
                window.Width = 1024; window.Height = 680; Choose(fun, "lc"); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Screens.Save(window, "fun-bin-lc-1024");
            }
        }
        finally { Close(window, shell); }
    }

    // Owner, 2026-10-28: every feature stays in the list; one the bin or the ECU does not have is greyed, says why, and
    // offers nothing to edit (the car file has no overlay, so its pops bytes are not pops settings).
    [CarAvaloniaFact]
    public async Task AFeatureTheBinLacksIsListedGreyedWithItsReason()
    {
        var (window, shell, fun, _) = await Open();
        try
        {
            var before = shell.Tune.Document!.Image.ToArray();
            var pops = fun.Features.Single(f => f.Key == "pops");
            Assert.False(pops.IsAvailable);
            Assert.Equal("Unavailable", pops.SwitchText);
            Assert.Equal("This bin has no live overlay; the pops need V8. Open the V8 bin to set them.", pops.Unavailable);
            Assert.Equal("lc", fun.Selected!.Key);

            fun.Selected = pops;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(fun.IsGroup);
            Assert.False(fun.IsHardcut);
            Assert.Empty(fun.ScalarRows);
            Assert.Equal(pops.Unavailable, Find.Named<Avalonia.Controls.TextBlock>(window, "FunUnavailable").Text);
            Assert.True(Find.Named<Avalonia.Controls.TextBlock>(window, "FunUnavailable").IsEffectivelyVisible);
            Assert.False(Find.Named<Avalonia.Controls.Control>(window, "FunRows").IsEffectivelyVisible);
            Assert.False(Find.Named<Avalonia.Controls.Control>(window, "FunSaveHint").IsEffectivelyVisible);
            Screens.Save(window, "fun-bin-pops-unavailable");
            Assert.Equal(before, shell.Tune.Document!.Image.ToArray());
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public void WithNoBinEveryFeatureIsListedGreyed()
    {
        var shell = new ShellViewModel(new MainWindowViewModel(ports: () => ["COM1"]),
            new TuneViewModel(AppSettings.Load(Path.Combine(Path.GetTempPath(), "ms43-ui-" + Guid.NewGuid().ToString("N")))));
        var fun = shell.Fun;
        Assert.Equal(FunFeatures.Order, fun.Features.Select(f => f.Key));
        Assert.All(fun.Features, f => Assert.False(f.IsAvailable));
        Assert.All(fun.Features, f => Assert.Equal("Open the car's bin on the Tune tab, or connect the car.", f.Unavailable));
        shell.Fun.Dispose(); shell.LiveTuning.Dispose();
    }

    [CarAvaloniaFact] // RF5
    public async Task AGroupWhoseXdfConstantsAreMissingIsNotListed()
    {
        var (window, shell, _, _) = await Open();
        try
        {
            var lc = OverlayV6.Maps.Single(m => m.Index == OverlayV6.LaunchControl);
            var broken = lc with { ScalarGroup = lc.ScalarGroup! with { Fields = [.. lc.ScalarGroup.Fields.Select(f => f.Offset == 3 ? f with { Key = "c_lc_missing" } : f)] } };
            var groups = OverlayV8.Maps.Where(m => m.ScalarGroup is not null).Select(m => m.Index == lc.Index ? broken : m).ToArray();
            Assert.DoesNotContain("lc", FunFeatures.Offline(shell.Tune.Document!, groups));
            Assert.Contains("nls", FunFeatures.Offline(shell.Tune.Document!, groups));
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task WithAStockEcuTheBarSaysWhyLiveIsUnavailable()
    {
        var (window, shell, fun, _) = await Open();
        try
        {
            await shell.LiveTuning.AttachLine(new FakeStockEcu());
            Assert.Equal(FunMode.Bin, fun.Mode);
            Assert.Contains(" · live unavailable: ", fun.ModeText);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task LiveEditInFunSendsWhatTheLivePageSends()
    {
        var (w1, s1, fun, ecu1) = await Open(8, v8Bin: true);
        var (w2, s2, _, ecu2) = await Open(8, v8Bin: true);
        try
        {
            Choose(fun, "pops");
            var angle = Row(fun, "Ignition angle").Editor; angle.Input = "-20.25"; Assert.True(angle.Apply());
            await fun.SendAsync();
            Assert.Null(fun.Problem);

            var page = s2.LiveTuning; page.Selected = page.Maps.Single(m => m.Map.Index == OverlayV8.Pops);
            var pageAngle = page.ScalarFields.Single(f => f.Label == "Ignition angle").Editor; pageAngle.Input = "-20.25"; Assert.True(pageAngle.Apply());
            await page.SendSelectedAsync();

            Assert.Equal(OverlayState.On, ecu1!.Entries[OverlayV8.Pops].State);
            Assert.Equal(ecu2!.Copies.AsSpan(ecu2.Entries[OverlayV8.Pops].Offset, 8).ToArray(),
                ecu1.Copies.AsSpan(ecu1.Entries[OverlayV8.Pops].Offset, 8).ToArray());
        }
        finally { Close(w1, s1); Close(w2, s2); }
    }

    [CarAvaloniaFact]
    public async Task LiveEditInFunShowsInTheLivePage()
    {
        var (window, shell, fun, _) = await Open(6);
        try
        {
            Choose(fun, "lc");
            var rpm = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max").Editor; rpm.Input = "4800"; Assert.True(rpm.Apply());
            var page = shell.LiveTuning; page.Selected = page.Maps.Single(m => m.Map.Index == OverlayV6.LaunchControl);
            Assert.Equal(150, page.ScalarFields.Single(f => f.Key == "c_lc_n_max").Editor.RawValue);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task OutOfRangePopsIsRefusedInFunAsOnThePage()
    {
        var (window, shell, fun, ecu) = await Open(8, v8Bin: true);
        try
        {
            Choose(fun, "pops");
            var rpm = Row(fun, "Minimum engine speed").Editor; rpm.Input = "1504"; rpm.Apply();
            var requests = ecu!.Requests.Count;
            await fun.SendAsync();
            Assert.Contains("outside its limits", fun.Problem);
            Assert.DoesNotContain(ecu.Requests.Skip(requests), p => p[0] == 0x6D && p[1] is 1 or 2 or 3);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // RF4
    public async Task ChoosingInFunLeavesTheLivePageAlone()
    {
        var (window, shell, fun, _) = await Open(8, v8Bin: true);
        try
        {
            var selected = shell.LiveTuning.Selected; var fields = shell.LiveTuning.ScalarFields.Count;
            foreach (var feature in fun.Features.ToArray()) fun.Selected = feature;
            Assert.Same(selected, shell.LiveTuning.Selected);
            Assert.Equal(fields, shell.LiveTuning.ScalarFields.Count);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // RF1, RF2
    public async Task ConnectingAndDisconnectingSwitchesWhatTheRowsEdit()
    {
        var (window, shell, fun, _) = await Open(v8Bin: true);
        try
        {
            Choose(fun, "lc");
            var offline = fun.ScalarRows.ToArray();
            var rpm = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max").Editor; rpm.Input = "4800"; Assert.True(rpm.Apply());
            Assert.Equal(150, shell.Tune.Document!.Image.Bytes[0x7132F]);

            var ecu = new FakeOverlayV4Ecu(shell.Tune.Document.Image.ToArray(), 8);
            await shell.LiveTuning.AttachLine(ecu);
            Assert.Equal(FunMode.Live, fun.Mode);
            Assert.DoesNotContain(fun.ScalarRows, row => offline.Contains(row));
            rpm = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max").Editor; rpm.Input = "5120"; Assert.True(rpm.Apply());
            Assert.Equal(150, shell.Tune.Document.Image.Bytes[0x7132F]);
            Assert.Equal(160, shell.LiveTuning.Page!.Image.Bytes[0x7132F]);

            await shell.LiveTuning.AttachLine(null);
            Assert.Equal(FunMode.Bin, fun.Mode);
            rpm = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max").Editor; rpm.Input = "4512"; Assert.True(rpm.Apply());
            Assert.Equal(141, shell.Tune.Document.Image.Bytes[0x7132F]);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task HardcutSendsThroughTheLimiter()
    {
        var (window, shell, fun, ecu) = await Open(6);
        try
        {
            Choose(fun, "hardcut");
            Assert.True(fun.IsHardcut);
            fun.LiveTuning.LimiterGears[3].Rpm = "6880";
            await fun.SendLimiterAsync();
            Assert.Null(fun.Problem);
            Assert.Equal(215, ecu!.Limiter.GearLimits[3]);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task OfflineEditIsTheSameBytesAsTuneAndOneUndoStep()
    {
        var (window, shell, fun, _) = await Open();
        try
        {
            Choose(fun, "lc");
            var before = shell.Tune.Document!.Image.ToArray();
            var rpm = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max").Editor; rpm.Input = "4800"; Assert.True(rpm.Apply());
            var expected = before.ToArray(); expected[0x7132F] = 150;
            Assert.Equal(expected, shell.Tune.Document.Image.ToArray());
            Assert.True(shell.Tune.Undos.CanUndo);
            shell.Tune.Undo();
            Assert.Equal(before, shell.Tune.Document.Image.ToArray());
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // RF3
    public async Task OfflineOutOfRangeIsRefusedAndUndone()
    {
        var (window, shell, fun, _) = await Open(v8Bin: true);
        try
        {
            Choose(fun, "pops");
            var before = shell.Tune.Document!.Image.ToArray();
            var rpm = Row(fun, "Minimum engine speed").Editor; rpm.Input = "1504"; rpm.Apply();
            Assert.Equal(before, shell.Tune.Document.Image.ToArray());
            Assert.False(shell.Tune.Undos.CanUndo);
            Assert.Contains("outside its limits", rpm.Message);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task OfflineHardcutWritesTheBinAndRefusesABadRpm()
    {
        var (window, shell, fun, _) = await Open();
        try
        {
            Choose(fun, "hardcut");
            fun.OfflineGears[3].Rpm = "6880";
            fun.ApplyOfflineLimiter();
            Assert.Null(fun.Problem);
            Assert.Equal(215, shell.Tune.Document!.Image.Bytes[0x75D4A]);
            var before = shell.Tune.Document.Image.ToArray();
            fun.OfflineGears[3].Rpm = "6881";
            fun.ApplyOfflineLimiter();
            Assert.NotNull(fun.Problem);
            Assert.Equal(before, shell.Tune.Document.Image.ToArray());
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact]
    public async Task TheListShowsEachSwitch()
    {
        var (window, shell, fun, _) = await Open(v8Bin: true);
        try
        {
            Put(shell, 0x4FF80, [0x97]);
            fun.Refresh();
            Assert.Equal("A/C on", fun.Features.Single(f => f.Key == "pops").SwitchText);
            Put(shell, 0x4FF80, [0x82]);
            fun.Refresh();
            Assert.Equal("ASC off", fun.Features.Single(f => f.Key == "pops").SwitchText);
            Put(shell, 0x4FF80, [2]);
            fun.Refresh();
            Assert.Equal("Cruise armed", fun.Features.Single(f => f.Key == "pops").SwitchText);
            Assert.Contains(fun.Features.Single(f => f.Key == "hardcut").SwitchText, new[] { "Fuel cut", "Ignition cut" });
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // review C1, C2
    public async Task OfflineChecksOnlyFunEdits_AndARefusalLeavesNoRedo()
    {
        var (window, shell, fun, _) = await Open(v8Bin: true);
        try
        {
            Choose(fun, "pops");
            var rpm = Row(fun, "Minimum engine speed").Editor; rpm.Input = "1504"; rpm.Apply();
            Assert.False(shell.Tune.Undos.CanUndo); Assert.False(shell.Tune.Undos.CanRedo);
            // An edit made in Tune, outside Fun, stays: here the overboost byte beside the RAL group.
            var neighbour = shell.Tune.Document!.Xdf.Constants.Single(c => c.Title == "c_ob_thr_e85");
            var editor = new ValueEditorViewModel(shell.Tune.Document, shell.Tune.History!, neighbour, shell.Tune.Catalog!);
            var before = shell.Tune.Document.Image.Bytes[0x71343];
            editor.Input = (editor.Current.Physical + 5).ToString(System.Globalization.CultureInfo.InvariantCulture); editor.Apply();
            Assert.NotEqual(before, shell.Tune.Document.Image.Bytes[0x71343]);
            Choose(fun, "ral");
            var lc = fun.ScalarRows[1].Editor; lc.Input = lc.ReadingText; lc.Apply();
            Assert.NotEqual(before, shell.Tune.Document.Image.Bytes[0x71343]);
            editor.Dispose();
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // review I1
    public async Task ConnectedTypingSurvivesTheLivePagesTick()
    {
        var (window, shell, fun, _) = await Open(6);
        try
        {
            Choose(fun, "lc");
            var row = fun.ScalarRows.Single(r => r.Key == "c_lc_n_max");
            row.Editor.Input = "48";
            await shell.LiveTuning.TickAsync(); await shell.LiveTuning.TickAsync();
            Assert.Same(row, fun.ScalarRows.Single(r => r.Key == "c_lc_n_max"));
            Assert.Equal("48", row.Editor.Input);
        }
        finally { Close(window, shell); }
    }

    [CarAvaloniaFact] // review I2, I4
    public async Task WriteToBinNeedsASend_AndTheHardcutUndoRevertsAll()
    {
        var (window, shell, fun, _) = await Open(6);
        try
        {
            Choose(fun, "lc");
            await fun.WriteToBinAsync();
            Assert.Contains("Send these settings", fun.Problem);
        }
        finally { Close(window, shell); }
        var (w2, s2, offline, _) = await Open();
        try
        {
            Choose(offline, "hardcut");
            var before = s2.Tune.Document!.Image.ToArray();
            offline.OfflineGears[3].Rpm = "6880"; offline.OfflineHysteresis = "64"; offline.ApplyOfflineLimiter();
            Assert.NotEqual(before, s2.Tune.Document.Image.ToArray());
            offline.Undo();
            Assert.Equal(before, s2.Tune.Document.Image.ToArray());
        }
        finally { Close(w2, s2); }
    }

    /// <summary>An ECU without the overlay: its header is not "LTOV".</summary>
    private sealed class FakeStockEcu : IDs2Line
    {
        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            var data = payload[0] == 0x06 ? new byte[payload[5]] : [];
            var frame = new byte[data.Length + 4]; frame[0] = 0x12; frame[1] = (byte)frame.Length; frame[2] = 0xA0;
            data.CopyTo(frame, 3); frame[^1] = frame[..^1].Aggregate((byte)0, (a, b) => (byte)(a ^ b));
            return frame;
        }
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
