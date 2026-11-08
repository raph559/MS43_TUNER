using Avalonia.Headless.XUnit;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tests;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// V9's triggers for launch control (124), no-lift shift (125) and rolling anti-lag (126), by name wherever the app
/// lists an activation byte: Live tuning, the Fun tab, the Tune tab and Compare. A V8 or older bin keeps the XDF's list.
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

    /// <summary>The car bin with its overlay header at <paramref name="version"/>, live on a fake ECU of that version.</summary>
    private static async Task<(Avalonia.Controls.Window Window, ShellViewModel Shell)> Live(int version)
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', (byte)version, 0]);
        Put(shell, 0x4FF80, [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14]);
        shell.SelectedTab = ShellViewModel.LiveTuningTab;
        await shell.LiveTuning.AttachLine(new FakeOverlayV4Ecu(shell.Tune.Document!.Image.ToArray(), version));
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

    [AvaloniaFact]
    public async Task V9ListsTheTriggersByName()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await Live(9);
        try
        {
            var lc = ActivationChoices(shell, 124);
            Assert.Equal([0L, 1, 2, 0x82, 0x97], lc.Select(c => c.Value));
            Assert.Equal(["Off", "Clutch pressed", "Cruise light on + clutch", "ASC off + clutch", "A/C on + clutch"], lc.Select(c => c.Label));
            var ral = ActivationChoices(shell, 126);
            Assert.Equal([0L, 1, 0x82, 0x97], ral.Select(c => c.Value));
            Assert.Equal(["Off", "“−” pressed", "ASC off + “−”", "A/C on + “−”"], ral.Select(c => c.Label));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact] // RF7
    public async Task V8KeepsTheStockChoices()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await Live(8);
        try
        {
            Assert.Equal([0L, 1, 2], ActivationChoices(shell, 124).Select(c => c.Value));
            Assert.Equal([0L, 1], ActivationChoices(shell, 126).Select(c => c.Value));
        }
        finally { Close(window, shell); }
    }

    [AvaloniaFact]
    public async Task TheFunListShowsTheTriggersShortName()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(select: null, time: new ManualTicker());
        try
        {
            Put(shell, 0x4B544, [(byte)'L', (byte)'T', (byte)'O', (byte)'V', 9, 0]);
            Put(shell, 0x4FF80, [0x00, 0x14, 0x17, 0xB8, 0x32, 0x9E, 0x00, 0x14]);
            Put(shell, 0x7132C, [0x97]);
            shell.SelectedTab = ShellViewModel.FunTab;
            shell.Fun.Refresh();
            Assert.Equal("A/C + clutch", shell.Fun.Features.Single(f => f.Key == "lc").SwitchText);
        }
        finally { Close(window, shell); }
    }

    private string Bin(int version, byte lc)
    {
        var bytes = File.ReadAllBytes(TuneScreen.Car!);
        "LTOV"u8.CopyTo(bytes.AsSpan(0x4B544)); bytes[0x4B548] = (byte)version; bytes[0x4B549] = 0;
        bytes[0x7132C] = lc;
        var path = Path.Combine(_folder, $"v{version}-{lc:X2}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [AvaloniaFact]
    public async Task TheTuneTabNamesTheTriggerInAV9Bin()
    {
        Assert.True(TuneScreen.Available);
        var (window, shell) = await TuneScreen.OpenAsync(select: "c_conf_lc", binPath: Bin(9, 0x97), time: new ManualTicker());
        try
        {
            var value = shell.Tune.Value!;
            Assert.Equal([0L, 1, 2, 0x82, 0x97], value.Choices!.Select(c => c.Value));
            Assert.Equal("A/C on + clutch", value.SelectedChoice?.Label);
        }
        finally { Close(window, shell); }

        (window, shell) = await TuneScreen.OpenAsync(select: "c_conf_lc", binPath: Bin(8, 1), time: new ManualTicker());
        try { Assert.Equal([0L, 1, 2], shell.Tune.Value!.Choices!.Select(c => c.Value)); }
        finally { Close(window, shell); }
    }

    [Fact]
    public void CompareNamesTheTriggerOnTheV9Side()
    {
        Assert.True(TuneScreen.Available);
        var xdf = XdfDefinition.LoadFromFile(TuneScreen.Xdf!);
        var v9 = BinImage.FromBytes(File.ReadAllBytes(Bin(9, 0x97)));
        var v8 = BinImage.FromBytes(File.ReadAllBytes(Bin(8, 1)));
        var item = BinComparer.Compare(xdf, v9, v8).Items.Single(i => i.Item.Title == "c_conf_lc");
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
