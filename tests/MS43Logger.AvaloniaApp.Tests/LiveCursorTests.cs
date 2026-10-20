using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MS43Logger.Core;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Live samples fed through the Live screen's view model into the Tune tab's cursor.</summary>
internal static class LiveFeed
{
    public static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");

    public static void Push(ShellViewModel shell, params (string Channel, long Raw)[] values) => Push(shell, Ms43x, values);

    public static void Push(ShellViewModel shell, AdxDefinition definition, params (string Channel, long Raw)[] values)
    {
        var payload = LivePayloads.Build(Ms43x, values);
        shell.Live.IsConnected = true;
        shell.Live.CurrentDefinition = definition;
        shell.Live.ShowSample(new LiveSample(DateTimeOffset.UtcNow, 0, Ms43x.DecodePayload(payload), payload));
    }

    public static async Task<(MainWindow Window, ShellViewModel Shell, ManualTime Time)> OpenAsync(string select = "ip_iga_ron98_pl__n__maf")
    {
        var time = new ManualTime();
        var (window, shell) = await TuneScreen.OpenAsync(select, time: time);
        shell.Tune.LiveCursor.AutoTick = false;
        return (window, shell, time);
    }
}

public class LiveCursorTests
{
    [AvaloniaFact]
    public async Task SampleMovesTheCursorOnTheVisibleMap()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var cursor = shell.Tune.LiveCursor;
        Assert.Equal("Live input: N (rpm) on rows, MAFIGA (mg/stk) on columns. Connect to see the engine's position on this map.", cursor.Readout);
        Assert.False(cursor.ReadoutIsLive);

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        cursor.Tick();

        var live = shell.Tune.Editor!.Live!;
        Assert.Equal((5, 1), (live.Fix.Cells[0].Row, live.Fix.Cells[0].Col));
        Assert.False(live.IsStale);
        Assert.True(cursor.ReadoutIsLive);
        Assert.StartsWith("Live 2480 rpm · ", cursor.Readout);
        Assert.Contains(" · map ", cursor.Readout);
        Assert.EndsWith(" °CRK", cursor.Readout);
        Assert.DoesNotContain("→", cursor.Readout);
        Assert.DoesNotContain("\u2212", cursor.Readout);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(cursor.Readout, Find.Named<TextBlock>(window, "LiveReadout").Text);
    }

    [AvaloniaFact]
    public async Task TenSamplesBetweenTicksGiveOneRedraw()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        var redraws = 0;
        shell.Tune.Editor!.PropertyChanged += (_, e) => redraws += e.PropertyName == nameof(TableEditorViewModel.Live) ? 1 : 0;

        for (var i = 0; i < 10; i++)
        {
            LiveFeed.Push(shell, ("N", 2000 + i * 10), ("MAFIGA", 1415));
        }

        shell.Tune.LiveCursor.Tick();
        shell.Tune.LiveCursor.Tick();

        Assert.Equal(1, redraws);
        Assert.Equal(2090, shell.Tune.Editor.Live!.Fix.Inputs[0].Input);
    }

    [AvaloniaFact]
    public async Task HiddenTabDoesNothing()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        shell.SelectedTab = ShellViewModel.LiveTab;
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        Assert.Null(shell.Tune.Editor!.Live);

        shell.SelectedTab = ShellViewModel.TuneTab;
        shell.Tune.LiveCursor.Tick();
        Assert.NotNull(shell.Tune.Editor.Live);
    }

    [AvaloniaFact]
    public async Task SwitchingTableClearsTheTrail()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        var cursor = shell.Tune.LiveCursor;
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        cursor.Tick();
        LiveFeed.Push(shell, ("N", 2500), ("MAFIGA", 1415));
        cursor.Tick();
        Assert.Equal(2, shell.Tune.Editor!.Live!.Trail.Count);

        var old = shell.Tune.Editor;
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_af_target_ron98__maf__n");
        Assert.Null(old.Live);
        LiveFeed.Push(shell, ("N", 2500), ("MAFTI", 5000));
        cursor.Tick();
        Assert.Single(shell.Tune.Editor!.Live!.Trail);
    }

    [AvaloniaFact]
    public async Task StaleAfterOneSecond()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, time) = await LiveFeed.OpenAsync();
        var cursor = shell.Tune.LiveCursor;
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        cursor.Tick();
        time.Advance(TimeSpan.FromSeconds(1.1));
        cursor.Tick();

        Assert.True(shell.Tune.Editor!.Live!.IsStale);
        Assert.False(cursor.ReadoutIsLive);
        Assert.EndsWith(" (stale)", cursor.Readout);
    }

    [AvaloniaFact]
    public async Task TrailSettingChangesTheTrail()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, time) = await LiveFeed.OpenAsync();
        var cursor = shell.Tune.LiveCursor;
        cursor.Trail = TrailMode.Off;
        for (var i = 0; i < 3; i++)
        {
            LiveFeed.Push(shell, ("N", 2480 + 100 * i), ("MAFIGA", 1415));
            cursor.Tick();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Empty(shell.Tune.Editor!.Live!.Trail);

        cursor.Trail = TrailMode.Long;
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        cursor.Tick();
        time.Advance(TimeSpan.FromSeconds(2));
        LiveFeed.Push(shell, ("N", 2600), ("MAFIGA", 1415));
        cursor.Tick();

        Assert.Equal(2, shell.Tune.Editor.Live!.Trail.Count);
    }

    [AvaloniaFact]
    public async Task TrailFollowsTheSetting()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        Assert.Equal(TrailMode.Short, shell.Tune.LiveCursor.Trail);

        var settings = new SettingsViewModel(shell.Tune.Settings, shell) { IsTrailLong = true };

        Assert.Equal(TrailMode.Long, shell.Tune.LiveCursor.Trail);
        settings.IsTrailOff = true;
        Assert.Equal(TrailMode.Off, shell.Tune.LiveCursor.Trail);
    }

    [AvaloniaFact]
    public async Task DisconnectClears()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        shell.Live.IsConnected = true;
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        Assert.NotNull(shell.Tune.Editor!.Live);

        shell.Live.Disconnect();
        Assert.Null(shell.Tune.Editor.Live);
        Assert.False(shell.Tune.LiveCursor.ReadoutIsLive);
        Assert.EndsWith("Connect to see the engine's position on this map.", shell.Tune.LiveCursor.Readout);
    }

    [AvaloniaFact]
    public async Task SampleArrivingAfterDisconnectIsIgnored()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        shell.Live.Disconnect();

        // A poll already in flight when Disconnect ran is still posted to the UI thread.
        var payload = LivePayloads.Build(LiveFeed.Ms43x, ("N", 3000), ("MAFIGA", 1415));
        shell.Live.ShowSample(new LiveSample(DateTimeOffset.UtcNow, 1, LiveFeed.Ms43x.DecodePayload(payload), payload));
        shell.Tune.LiveCursor.Tick();

        Assert.Null(shell.Tune.Editor!.Live);
        Assert.EndsWith("Connect to see the engine's position on this map.", shell.Tune.LiveCursor.Readout);
    }

    [AvaloniaFact]
    public async Task UnreadableSampleDoesNotThrow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        shell.Live.IsConnected = true;
        shell.Live.CurrentDefinition = LiveFeed.Ms43x;
        shell.Live.ShowSample(new LiveSample(DateTimeOffset.UtcNow, 0, Array.Empty<DecodedValue>(), new byte[10]));

        shell.Tune.LiveCursor.Tick();

        Assert.Null(shell.Tune.Editor!.Live);
        Assert.StartsWith("No live input: the last sample could not be read", shell.Tune.LiveCursor.Readout);
        Assert.False(shell.Tune.LiveCursor.ReadoutIsLive);
    }

    [AvaloniaFact]
    public async Task NoInputTableSaysSo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync("id_lam_tco_min__tco_st");
        LiveFeed.Push(shell, ("TCO", 124));
        shell.Tune.LiveCursor.Tick();

        Assert.StartsWith("No live input:", shell.Tune.LiveCursor.Readout);
        Assert.False(shell.Tune.LiveCursor.ReadoutIsLive);
        Assert.Null(shell.Tune.Editor!.Live);
    }

    [AvaloniaFact]
    public async Task WrongLoggerDefinitionSaysSo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        LiveFeed.Push(shell, BundledDefinitions.Load("b25"), ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();

        Assert.StartsWith("No live input: the logger definition", shell.Tune.LiveCursor.Readout);
        Assert.Null(shell.Tune.Editor!.Live);
    }

    [AvaloniaFact]
    public async Task AxisEditRebinds()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await LiveFeed.OpenAsync();
        var document = shell.Tune.Document!;
        var axis = (XdfTable)document.Xdf.FindByTitle("ldpm_n_14")!;
        shell.Tune.History!.Apply(TableEdits.Set(document, axis, new CellRange(5, 0, 5, 0), 2300), "test");

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 778));
        shell.Tune.LiveCursor.Tick();

        Assert.Equal(58982 / 65536.0, shell.Tune.Editor!.Live!.Fix.Row!.Value.Fraction);
    }
}
