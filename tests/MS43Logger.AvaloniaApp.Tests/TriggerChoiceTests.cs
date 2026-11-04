using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The overlay's triggers for launch control (124), no-lift shift (125) and rolling anti-lag (126), by name wherever the
/// app lists an activation byte: Live tuning, the Fun tab, the Tune tab and Compare. A bin without V10.4 keeps the XDF's list.
/// </summary>
public sealed class TriggerChoiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-triggers-" + Guid.NewGuid().ToString("N"));

    public TriggerChoiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static void Put(ShellViewModel shell, int offset, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            shell.Tune.Document!.Image.WriteElement(new XdfData(offset + i, 8, false, true, 1, 1, null), 0, bytes[i]);
    }

    /// <summary>The car bin made a V10.4 bin, live on a V10.4 ECU.</summary>
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell)> Live()
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', 10, 4]);
        Put(shell, 0x4FF80, OverlayV104Settings.Defaults);
        shell.SelectedTab = ShellViewModel.LiveTuningTab;
        await shell.LiveTuning.AttachLine(new FakeOverlayEcu(shell.Tune.Document!.Image.ToArray()));
        return (window, shell);
    }

    private static IReadOnlyList<ChoiceItem> ActivationChoices(ShellViewModel shell, int table)
    {
        var page = shell.LiveTuning;
        page.Selected = page.Maps.Single(row => row.Map.Index == table);
        return page.ScalarFields[0].Editor.Choices!;
    }

    private static void Close(Avalonia.Controls.Window window, ShellViewModel shell)
    {
        shell.LiveTuning.Dispose(); shell.Fun.Dispose(); window.Close();
    }

    [CarAvaloniaFact]
    public async Task TheFunListShowsTheTriggersShortName()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        try
        {
            Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', 10, 4]);
            Put(shell, 0x4FF80, OverlayV104Settings.Defaults);
            Put(shell, 0x7132C, [0x97]);
            shell.SelectedTab = ShellViewModel.FunTab;
            shell.Fun.Refresh();
            Assert.Equal("A/C + clutch", shell.Fun.Features.Single(f => f.Key == "lc").SwitchText);
        }
        finally { Close(window, shell); }
    }

    /// <summary>The car bin with an overlay header of <paramref name="version"/>.<paramref name="minor"/> and LC's activation byte.</summary>
    private string Bin(int version, int minor, byte lc)
    {
        var bytes = File.ReadAllBytes(TuneScreen.Car!);
        "LTOV"u8.CopyTo(bytes.AsSpan(0x4B544)); bytes[0x4B548] = (byte)version; bytes[0x4B549] = (byte)minor;
        bytes[0x7132C] = lc;
        var path = Path.Combine(_folder, $"v{version}.{minor}-{lc:X2}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [CarAvaloniaFact]
    public async Task TuneNamesTriggersOnlyForTheCurrentOverlay()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(select: "c_conf_lc", binPath: Bin(10, 4, 0x97), time: new ManualTicker());
        try
        {
            var value = shell.Tune.Value!;
            Assert.Equal([0L, 1, 2, 0x82, 0x97], value.Choices!.Select(c => c.Value));
            Assert.Equal("A/C on + clutch", value.SelectedChoice?.Label);
        }
        finally { Close(window, shell); }

        (window, shell) = await TuneScreen.OpenAsync(select: "c_conf_lc", binPath: Bin(10, 2, 1), time: new ManualTicker());   // an older overlay
        try { Assert.Equal([0L, 1, 2], shell.Tune.Value!.Choices!.Select(c => c.Value)); }
        finally { Close(window, shell); }
    }

    [CarFact]
    public void CompareNamesTriggersOnTheCurrentOverlaySide()
    {
        Assert.True(TuneScreen.Available);
        var xdf = XdfDefinition.LoadFromFile(TuneScreen.Xdf!);
        var current = BinImage.FromBytes(File.ReadAllBytes(Bin(10, 4, 0x97)));
        var older = BinImage.FromBytes(File.ReadAllBytes(Bin(9, 0, 1)));
        var item = BinComparer.Compare(xdf, current, older).Items.Single(i => i.Item.Title == "c_conf_lc");
        Assert.Equal("A/C on + clutch", item.ValueA);
        Assert.Contains("clutch is pressed", item.ValueB);
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
