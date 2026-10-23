using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class UndoEverywhereTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-undo-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void Undo(Window window) => window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");

    private static void Redo(Window window) => window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");

    private static void RedoShift(Window window) => window.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Z, "Z");

    private Task<(MainWindow Window, ShellViewModel Shell)> Open(string? select) =>
        TuneScreen.OpenAsync(select: select, binPath: TuneScreen.CopyCar(_folder));

    private static double LaunchLimit(ShellViewModel shell) =>
        shell.Tune.Document!.Reader.ReadConstant((XdfConstant)shell.Tune.Document.Xdf.FindByTitle("c_lc_n_max")!).Physical;

    [AvaloniaFact]
    public async Task ValueSetInTheValueBoxUndoesWhileTheBoxKeepsFocus()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        var before = LaunchLimit(shell);
        var box = Find.Named<TextBox>(window, "ValueInput");
        box.Focus();
        shell.Tune.Value!.Input = "4000";
        Assert.True(shell.Tune.Value.Apply());
        Assert.Equal(4000, LaunchLimit(shell), 0);
        Assert.True(box.IsFocused);

        Undo(window);
        Assert.Equal(before, LaunchLimit(shell));
        Redo(window);
        Assert.Equal(4000, LaunchLimit(shell), 0);
        Undo(window);
        RedoShift(window);
        Assert.Equal(4000, LaunchLimit(shell), 0);
    }

    [AvaloniaFact]
    public async Task ToolbarEditUndoesWhileTheToolValueBoxKeepsFocus()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("ip_iga_ron98_pl__n__maf");
        Find.Named<TextBox>(window, "ToolValue").Focus();
        shell.Tune.Editor!.Selection = new CellRange(12, 0, 12, 0);
        shell.Tune.ToolValue = "-10";
        Assert.True(shell.Tune.RunTool(EditKind.Add));
        Assert.Equal(21.375, shell.Tune.Editor.Values.Physical[12, 0]);

        Undo(window);
        Assert.Equal(31.5, shell.Tune.Editor.Values.Physical[12, 0]);
    }

    [AvaloniaFact]
    public async Task PinningAnItemUndoesAndRedoes()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        var item = shell.Tune.SelectedItem!;
        Assert.False(shell.Tune.Settings.IsPinned(item.Title));

        shell.Tune.TogglePin(item);
        Assert.True(shell.Tune.Settings.IsPinned(item.Title));
        Assert.Equal("Undo: Pin c_lc_n_max", shell.Tune.UndoTip);

        Undo(window);
        Assert.False(shell.Tune.Settings.IsPinned(item.Title));
        Assert.Equal("Pin", shell.Tune.PinText);
        Redo(window);
        Assert.True(shell.Tune.Settings.IsPinned(item.Title));
        Assert.Contains(shell.Tune.Rail, entry => entry is RailItem { IsPinned: true } pinned && pinned.Item == item);
    }

    [AvaloniaFact]
    public async Task EditsAndPinsUndoInTheOrderTheyWereMade()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        var item = shell.Tune.SelectedItem!;
        var before = LaunchLimit(shell);
        shell.Tune.Value!.Input = "4000";
        shell.Tune.Value.Apply();
        shell.Tune.TogglePin(item);

        Undo(window);
        Assert.False(shell.Tune.Settings.IsPinned(item.Title));
        Assert.Equal(4000, LaunchLimit(shell), 0);
        Assert.Equal("Undo: Set c_lc_n_max to 4000", shell.Tune.UndoTip);

        Undo(window);
        Assert.Equal(before, LaunchLimit(shell));
        Assert.False(shell.Tune.CanUndo);

        Redo(window);
        Redo(window);
        Assert.Equal(4000, LaunchLimit(shell), 0);
        Assert.True(shell.Tune.Settings.IsPinned(item.Title));
        Assert.False(shell.Tune.CanRedo);
    }

    [AvaloniaFact]
    public async Task ANewActionClearsRedo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        var item = shell.Tune.SelectedItem!;
        shell.Tune.Value!.Input = "4000";
        shell.Tune.Value.Apply();
        Undo(window);
        Assert.True(shell.Tune.CanRedo);

        shell.Tune.TogglePin(item);

        Assert.False(shell.Tune.CanRedo);
        Undo(window);
        Undo(window);
        Assert.False(shell.Tune.Settings.IsPinned(item.Title));
        Assert.NotEqual(4000, LaunchLimit(shell), 0);
    }

    [AvaloniaFact]
    public async Task ASearchBoxKeepsItsOwnTextUndo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        shell.Tune.Value!.Input = "4000";
        shell.Tune.Value.Apply();
        Find.Named<TextBox>(window, "SearchBox").Focus();

        Undo(window);

        Assert.Equal(4000, LaunchLimit(shell), 0);
    }

    [AvaloniaFact]
    public async Task LivePinsUndoOnLiveAndLeaveTuneEditsAlone()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        shell.Tune.Value!.Input = "4000";
        shell.Tune.Value.Apply();

        shell.SelectedTab = ShellViewModel.LiveTab;
        Dispatcher.UIThread.RunJobs();
        var channel = shell.Live.OtherValues.First();
        shell.Live.TogglePinned(channel);
        Assert.True(channel.IsPinned);

        Undo(window);
        Assert.False(channel.IsPinned);
        Assert.DoesNotContain(channel, shell.Live.PinnedValues);
        Assert.Equal(4000, LaunchLimit(shell), 0);

        Redo(window);
        Assert.True(channel.IsPinned);
        Assert.Contains(channel, shell.Live.PinnedValues);

        Undo(window);
        Undo(window);
        Assert.Equal(4000, LaunchLimit(shell), 0);
    }

    [AvaloniaFact]
    public async Task UndoOnTuneDoesNotTouchLivePins()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await Open("c_lc_n_max");
        var channel = shell.Live.OtherValues.First();
        shell.Live.TogglePinned(channel);
        shell.SelectedTab = ShellViewModel.TuneTab;
        Dispatcher.UIThread.RunJobs();

        Undo(window);

        Assert.True(channel.IsPinned);
    }
}
