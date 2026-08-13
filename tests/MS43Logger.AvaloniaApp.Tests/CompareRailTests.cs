using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The Compare list resizes and collapses as the Tune rail does, and remembers its own width and state.</summary>
public sealed class CompareRailTests : IDisposable
{
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-crail-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell)?> Open()
    {
        if (!TuneScreen.Available || Release is null)
        {
            return null;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: TuneScreen.CopyCar(_folder));
        shell.SelectedTab = ShellViewModel.CompareTab;
        Assert.True(shell.Compare.OpenB(Release));
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static double Width(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return Find.Named<Control>(window, name).Bounds.Width;
    }

    [AvaloniaFact]
    public async Task DraggingTheListEdgeResizesItOnly()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        Assert.Equal(280, Width(window, "CompareList"));
        var handle = Find.Named<Control>(window, "CompareResizeHandle");
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(60, 0));
        window.MouseUp(start + new Point(60, 0), MouseButton.Left);

        Assert.Equal(340, Width(window, "CompareList"));
        Assert.Equal(340, shell.Tune.Settings.CompareRailWidth);
        Assert.Null(shell.Tune.Settings.RailWidth);
    }

    [AvaloniaFact]
    public async Task TheListCollapsesAndCtrlBAndCtrlKWorkOnCompare()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        Find.Named<Button>(window, "CompareCollapseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Compare.RailCollapsed);
        Assert.Equal(48, Width(window, "CompareList"));
        Assert.False(shell.Tune.RailCollapsed);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Assert.False(shell.Compare.RailCollapsed);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
        Assert.True(shell.Compare.RailCollapsed);
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Compare.RailCollapsed);
        Assert.True(Find.Named<TextBox>(window, "CompareSearchBox").IsFocused);

        shell.Compare.SetRailCollapsed(true);
        Dispatcher.UIThread.RunJobs();
        Find.Named<Button>(window, "CompareExpandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(280, Width(window, "CompareList"));
        Assert.True(AppSettings.Load(shell.Tune.Settings.Folder) is { CompareRailCollapsed: false });
    }
}
