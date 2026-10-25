using Avalonia.Headless.XUnit;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>With a V9 ECU the watched map's cursor comes from the record, and the use line from the record's read count.</summary>
public sealed class WatchedCursorTests
{
    private const uint PartLoad = 0x77315;                                  // ip_iga_ron98_pl__n__maf
    private const uint E85PartLoad = 0x77494;                               // ip_iga_e85_pl__n__maf

    private static readonly WatchRecord Cell = new(WatchKind.MapIndices, 1, 3, 0x8000, 5, 0x4000, 16);

    private static void Push(ShellViewModel shell, WatchRecord? record, uint? watched, (int, byte)[]? frame = null)
    {
        var payload = LivePayloads.Build(LiveFeed.Ms43x, frame ?? LiveFeed.PartLoad, ("N", 800), ("MAFIGA", 778)); // logged: cell (0, 0)
        shell.Live.IsConnected = true;
        shell.Live.CurrentDefinition = LiveFeed.Ms43x;
        shell.Live.ShowSample(new LiveSample(DateTimeOffset.UtcNow, 0, LiveFeed.Ms43x.DecodePayload(payload), payload,
            Watch: record, WatchedAddress: watched));
    }

    private sealed class Target : IWatchTarget
    {
        public List<uint?> Sent { get; } = new();
        public void SetWatch(uint? address) => Sent.Add(address);
        public uint? Watched => Sent.LastOrDefault();
    }

    [CarAvaloniaFact]
    public async Task WatchedMapDrawsTheRecordsCellNotTheLoggedOne()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        Push(shell, Cell, PartLoad);
        shell.Tune.LiveCursor.Tick();
        var fix = shell.Tune.Editor!.Live!.Fix;
        Assert.Equal(3.5, fix.Row!.Value.Position);
        Assert.Equal(5.25, fix.Col!.Value.Position);
        Assert.StartsWith("Live exact cell", shell.Tune.LiveCursor.Readout);
    }

    [CarAvaloniaFact]
    public async Task MovingCounterSaysInUseWhereTheRulesSayNotInUse()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        Push(shell, Cell, PartLoad, [(0x00, 0x02)]);                   // the rules: not in use at idle
        shell.Tune.LiveCursor.Tick();
        Assert.Equal("In use", shell.Tune.LiveCursor.Use);
        Assert.False(shell.Tune.LiveCursor.UseIsDim);
    }

    [CarAvaloniaFact] // the car's flex-fuel substitutes are 0 %: the ECU reads its E85 maps but counts them for nothing
    public async Task MovingCounterKeepsNotInUseForAMapAtAZeroShare()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync("ip_iga_e85_pl__n__maf");
        Push(shell, Cell, E85PartLoad);                                  // part load: the rules use the blend
        shell.Tune.LiveCursor.Tick();
        Assert.Equal("Not in use · RON98 100 %", shell.Tune.LiveCursor.Use);
        Assert.True(shell.Tune.LiveCursor.UseIsDim);
        Assert.True(shell.Tune.Editor!.Live!.IsUnused);                 // the cursor in Ink3, not Now
        Assert.Equal(3.5, shell.Tune.Editor.Live.Fix.Row!.Value.Position);
    }

    [CarAvaloniaFact]
    public async Task StoppedCounterSaysNotReadNowWithTheTimeSinceTheLastRead()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, time) = await LiveFeed.OpenAsync();
        Push(shell, Cell, PartLoad);
        shell.Tune.LiveCursor.Tick();
        time.Advance(TimeSpan.FromSeconds(12));
        Push(shell, Cell, PartLoad);                                     // the same count: no read since
        shell.Tune.LiveCursor.Tick();
        Assert.Equal("Not read now · last read 12 s ago", shell.Tune.LiveCursor.Use);
        Assert.True(shell.Tune.LiveCursor.UseIsDim);
        Assert.True(shell.Tune.Editor!.Live!.IsStale);                  // the last read's cell, dimmed
        Assert.Equal(3.5, shell.Tune.Editor.Live.Fix.Row!.Value.Position);
    }

    [CarAvaloniaFact]
    public async Task NeverReadSaysNotReadSinceConnect()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        Push(shell, new WatchRecord(WatchKind.None, 0, 0, 0, 0, 0, 0), PartLoad);
        shell.Tune.LiveCursor.Tick();
        Assert.Equal("Not read now · not read since connect", shell.Tune.LiveCursor.Use);
        Assert.Null(shell.Tune.Editor!.Live);
    }

    [CarAvaloniaFact]
    public async Task SwitchingMapsSendsOneWatchAndShowsNoOldCell()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        var target = new Target();
        var coordinator = new MapWatchCoordinator(() => target);
        shell.Tune.LiveCursor.Coordinator = coordinator;
        coordinator.Activate(shell.Tune.LiveCursor);
        Assert.Equal([PartLoad], target.Sent);
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_is__n__maf");
        Assert.Equal([PartLoad, 0x775D4u], target.Sent);
        coordinator.Activate(shell.Tune.LiveCursor);                    // the same table again: nothing more is sent
        Assert.Equal(2, target.Sent.Count);
        Push(shell, Cell, PartLoad);                                     // a reply to a poll from before the switch
        shell.Tune.LiveCursor.Tick();
        Assert.NotEqual(3.5, shell.Tune.Editor!.Live?.Fix.Row?.Position);
    }

    [CarAvaloniaFact]
    public async Task TheFocusedMapWindowIsTheWatchedOne()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        var target = new Target();
        var coordinator = new MapWatchCoordinator(() => target);
        shell.Tune.LiveCursor.Coordinator = coordinator;
        coordinator.Activate(shell.Tune.LiveCursor);
        var window = shell.Tune.OpenMapWindow((XdfTable)shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_is__n__maf")!);
        Assert.Same(coordinator, window.LiveCursor.Coordinator);
        window.LiveCursor.Focused();
        Assert.Equal(0x775D4u, target.Sent[^1]);
        shell.Tune.LiveCursor.Focused();
        Assert.Equal(PartLoad, target.Sent[^1]);
    }

    [CarAvaloniaFact]
    public async Task AV8EcuKeepsTheLoggedInputCursor()
    {
        Assert.True(TuneScreen.Available);
        var (_, shell, _) = await LiveFeed.OpenAsync();
        Push(shell, null, null);
        shell.Tune.LiveCursor.Tick();
        Assert.Equal(new AxisPosition(0, 0, 0), shell.Tune.Editor!.Live!.Fix.Row);
    }
}
