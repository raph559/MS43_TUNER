using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Live tab's fault memory dialog: read on open, read only, every state worded.</summary>
public sealed class FaultMemoryViewTests
{
    /// <summary>Answers DS2 06 from a byte map with the MS43X001 layout; Fail makes every read time out.</summary>
    private sealed class Ecu : IDs2Line
    {
        private readonly Dictionary<int, byte> _memory = new();
        public byte QueueFlags { set => _memory[FaultMemory.QueueFlagsAddress] = value; }
        public bool Fail { get; set; }
        public Exception? Error { get; set; }
        public bool Layout { get; set; } = true;
        public List<byte> Commands { get; } = new();

        public Ecu(params (int Path, byte Status)[] stored)
        {
            for (var p = 0; p < FaultPaths.All.Count; p++) _memory[FaultMemory.DescriptorsAddress + 22 * p] = FaultPaths.All[p].Ds2Code;
            _memory[FaultMemory.StoredCountAddress] = (byte)stored.Length;
            for (var i = 0; i < stored.Length; i++)
            {
                _memory[FaultMemory.StoredListAddress + i] = (byte)stored[i].Path;
                _memory[FaultMemory.RecordsAddress + 12 * stored[i].Path] = stored[i].Status;
                _memory[FaultMemory.RecordsAddress + 12 * stored[i].Path + 2] = 4;
            }
        }

        public void WaitMilliseconds(int milliseconds) { }

        public byte[] Request(IReadOnlyList<byte> payload, int listenMilliseconds = 400)
        {
            Commands.Add(payload[0]);
            if (Fail) throw new TimeoutException("The ECU did not answer.");
            if (Error is not null) throw Error;
            var address = payload[2] << 16 | payload[3] << 8 | payload[4];
            var frame = new byte[payload[5] + 4];
            frame[0] = 0x12; frame[1] = (byte)frame.Length; frame[2] = 0xA0;
            for (var i = 0; i < payload[5]; i++)
                frame[3 + i] = Layout || address != FaultMemory.DescriptorsAddress ? _memory.GetValueOrDefault(address + i) : (byte)0xEE;
            for (var i = 0; i < frame.Length - 1; i++) frame[^1] ^= frame[i];
            return frame;
        }
    }

    [Fact]
    public async Task StoredFaultsAreListedOldestFirstWithTheirDetails()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu((124, 0x20 | 0x40 | 0x08), (0, 0x20 | 0x80 | 0x01)));
        await faults.ReadAsync();
        Assert.Equal("2 stored faults, oldest first.", faults.Summary);
        Assert.Equal(new FaultRow("P1567", "msw_tog", "implausible · present now · frequency count 4"), faults.Faults[0]);
        Assert.Equal(new FaultRow("P0103", "maf", "above maximum · not present now · sporadic · frequency count 4 · turns on the engine light"),
            faults.Faults[1]);
        Assert.True(faults.HasFaults);
    }

    [Fact]
    public async Task NoOverflowIsSaid()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu());
        await faults.ReadAsync();
        Assert.Equal("Task queues since key-on: no overflow.", faults.Queues);
    }

    [Fact]
    public async Task AnOverflowNamesItsQueue()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu((124, 0x28)) { QueueFlags = 0x80 });
        await faults.ReadAsync();
        Assert.Equal("Task queues since key-on: queue 2 overflowed, the ECU ran out of CPU time.", faults.Queues);
    }

    [Fact]
    public async Task AFailedReadShowsNoQueueLine()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu { Fail = true });
        await faults.ReadAsync();
        Assert.Null(faults.Queues);
    }

    [Fact] // the link closing mid-read must not leave "No stored faults." or the last read's queue line on screen
    public async Task AClosedLineSaysTheReadFailedAndDropsTheLastResult()
    {
        var ecu = new Ecu((124, 0x28)) { QueueFlags = 0x80 };
        var faults = new FaultMemoryViewModel(() => ecu);
        await faults.ReadAsync();
        Assert.NotNull(faults.Queues);
        ecu.Error = new InvalidOperationException("The session is not connected.");
        await faults.ReadAsync();
        Assert.StartsWith("The fault memory could not be read: The session is not connected.", faults.Summary);
        Assert.Null(faults.Queues);
        Assert.Empty(faults.Faults);
        Assert.False(faults.ShowsNone);
        Assert.True(faults.CanReadAgain);
    }

    [Fact]
    public async Task OnlyMemoryReadsAreSent()
    {
        var ecu = new Ecu((124, 0x28));
        await new FaultMemoryViewModel(() => ecu).ReadAsync();
        Assert.All(ecu.Commands, command => Assert.Equal(0x06, command));
    }

    [Fact]
    public async Task AnEmptyMemorySaysSo()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu());
        await faults.ReadAsync();
        Assert.Equal("No stored faults.", faults.Summary);
        Assert.True(faults.ShowsNone);
        Assert.False(faults.HasFaults);
    }

    [Fact]
    public async Task NoConnectionAsksToConnect()
    {
        var faults = new FaultMemoryViewModel(() => null);
        await faults.ReadAsync();
        Assert.Equal("Connect to the ECU to read its fault memory.", faults.Summary);
    }

    [Fact]
    public async Task ATimeoutSaysSoAndCanBeReadAgain()
    {
        var ecu = new Ecu((124, 0x28)) { Fail = true };
        var faults = new FaultMemoryViewModel(() => ecu);
        await faults.ReadAsync();
        Assert.StartsWith("The fault memory could not be read: The ECU did not answer.", faults.Summary);
        Assert.True(faults.CanReadAgain);
        ecu.Fail = false;
        await faults.ReadAsync();
        Assert.Equal("1 stored fault.", faults.Summary);
    }

    [Fact]
    public async Task AnotherLayoutIsNotRead()
    {
        var faults = new FaultMemoryViewModel(() => new Ecu((124, 0x28)) { Layout = false });
        await faults.ReadAsync();
        Assert.Equal("This ECU's fault memory is not laid out as MS43X001's; nothing was read.", faults.Summary);
        Assert.Empty(faults.Faults);
    }

    [AvaloniaFact]
    public async Task TheLiveTabsButtonWorksOnlyWhileConnected()
    {
        if (!TuneScreen.Available) return;
        var (window, shell, _) = await LiveFeed.OpenAsync();
        shell.SelectedTab = ShellViewModel.LiveTab;
        Dispatcher.UIThread.RunJobs();
        var button = Find.Named<Button>(window, "FaultMemoryButton");
        var asked = 0;
        shell.Live.ShowFaultMemoryRequested += (_, _) => asked++;
        shell.Live.IsConnected = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEnabled);
        shell.Live.IsConnected = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, asked);
    }

    [AvaloniaFact]
    public async Task TheDialogIsCapturedInBothThemes()
    {
        if (!TuneScreen.Available) return;
        var (window, shell, _) = await LiveFeed.OpenAsync();
        shell.SelectedTab = ShellViewModel.LiveTab;
        foreach (var (night, name) in new[] { (false, "day"), (true, "night") })
        {
            if (night) shell.IsNight = true; else shell.IsDay = true;
            var faults = new FaultMemoryViewModel(() => new Ecu((124, 0x20 | 0x40 | 0x08), (0, 0x20 | 0x80 | 0x01), (83, 0x24)) { QueueFlags = 0x80 });
            _ = Find.Named<MS43Logger.AvaloniaApp.Shell.DialogHost>(window, "Dialogs").ShowAsync(new FaultMemoryView(faults));
            for (var i = 0; i < 50 && (faults.IsReading || faults.Faults.Count == 0); i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(3, faults.Faults.Count);
            Screens.Save(window, $"fault-memory-{name}");
            Find.Named<MS43Logger.AvaloniaApp.Shell.DialogHost>(window, "Dialogs").Close();
        }
    }

    [AvaloniaFact]
    public async Task TheDialogReadsOnOpenAndShowsTheRows()
    {
        var view = new FaultMemoryView(new FaultMemoryViewModel(() => new Ecu((124, 0x28), (1, 0x24))));
        var window = new Window { Content = view };
        window.Show();
        for (var i = 0; i < 50 && ((FaultMemoryViewModel)view.DataContext!).IsReading; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2 stored faults, oldest first.", view.FindControl<TextBlock>("SummaryText")!.Text);
        Assert.Equal(2, view.FindControl<ItemsControl>("FaultList")!.ItemCount);
        Assert.True(view.FindControl<Button>("ReadAgainButton")!.IsEnabled);
        window.Close();
    }
}
