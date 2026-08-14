using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Swap A and B on Compare: B's file becomes the bin edited on Tune and A's becomes B, after a confirmation.</summary>
public sealed class CompareSwapTests : IDisposable
{
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-swap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell, string A, string B)?> Open()
    {
        if (!TuneScreen.Available || Release is null)
        {
            return null;
        }

        var a = TuneScreen.CopyCar(_folder);
        var b = Path.Combine(_folder, "RELEASE.bin");
        File.Copy(Release, b);
        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: a);
        shell.SelectedTab = ShellViewModel.CompareTab;
        Assert.True(shell.Compare.OpenB(b));
        Dispatcher.UIThread.RunJobs();
        return (window, shell, a, b);
    }

    [AvaloniaFact]
    public async Task SwapAsksFirstAndCancelChangesNothing()
    {
        if (await Open() is not var (_, shell, a, b))
        {
            return;
        }

        string? asked = null;
        shell.Compare.AskToSwap = message => { asked = message; return Task.FromResult(false); };

        Assert.False(await shell.Compare.SwapAsync());

        Assert.Equal("Swap A and B? RELEASE.bin opens as A, the bin you edit, and CAR.bin becomes B.", asked);
        Assert.Equal(Path.GetFullPath(a), shell.Tune.Document!.Path);
        Assert.Equal(Path.GetFullPath(b), shell.Compare.PathB);
    }

    [AvaloniaFact]
    public async Task SwapMakesBTheEditedBinAndAB()
    {
        if (await Open() is not var (_, shell, a, b))
        {
            return;
        }

        var before = shell.Compare.Result!.Items.Count;
        shell.Compare.AskToSwap = _ => Task.FromResult(true);

        Assert.True(await shell.Compare.SwapAsync());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Path.GetFullPath(b), shell.Tune.Document!.Path);
        Assert.Equal("RELEASE.bin", shell.Compare.FileA);
        Assert.Equal("CAR.bin", shell.Compare.FileB);
        Assert.Equal(Path.GetFullPath(a), shell.Compare.PathB);
        Assert.Equal(before, shell.Compare.Result!.Items.Count);

        // B − A turns over: what A had more of, B has now.
        var map = shell.Compare.Result.Items.Single(item => item.Item.Title == "ip_iga_ron98_pl__n__maf");
        Assert.NotNull(map.LargestChange);
    }

    [AvaloniaFact]
    public async Task UnsavedEditsAreAskedAboutAndCancelStopsTheSwap()
    {
        if (await Open() is not var (_, shell, a, _))
        {
            return;
        }

        var ign = (XdfTable)shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf")!;
        shell.Tune.History!.Apply(TableEdits.Step(shell.Tune.Document, ign, new CellRange(0, 0, 0, 0), 1), "test edit");
        shell.Compare.AskToSwap = _ => Task.FromResult(true);
        var savePrompts = 0;
        shell.Tune.AskToSave = _ => { savePrompts++; return Task.FromResult(SaveChoice.Cancel); };

        Assert.False(await shell.Compare.SwapAsync());

        Assert.Equal(1, savePrompts);
        Assert.Equal(Path.GetFullPath(a), shell.Tune.Document.Path);
        Assert.True(shell.Tune.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task TheSwapButtonOpensTheConfirmation()
    {
        if (await Open() is not var (window, shell, _, _))
        {
            return;
        }

        var button = Find.Named<Button>(window, "SwapButton");
        Assert.True(button.IsEffectivelyVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var dialogs = Find.Named<Shell.DialogHost>(window, "Dialogs");
        Assert.True(dialogs.IsOpen);
        Assert.True(Find.Named<Button>(window, "ConfirmActionButton").IsEffectivelyVisible);
        Assert.Equal("Swap", Find.Named<Button>(window, "ConfirmActionButton").Content);
        Screens.Save(window, "compare-swap-confirm");

        Find.Named<Button>(window, "ConfirmCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialogs.IsOpen);
        Assert.Equal("CAR.bin", shell.Compare.FileA);
    }

    // The two names slide past each other into their new rows, and the swap icon turns half a turn.
    [AvaloniaFact]
    public async Task TheNamesCrossOverWhenTheySwap()
    {
        var animations = MotionSettings.AnimationsEnabled;
        MotionSettings.AnimationsEnabled = true;
        try
        {
            if (await Open() is not var (window, shell, _, _))
            {
                return;
            }

            shell.Compare.AskToSwap = _ => Task.FromResult(true);
            var fileA = Find.Named<TextBlock>(window, "FileAText");
            var fileB = Find.Named<TextBlock>(window, "FileBText");
            // The animation moves the TranslateTransform inside the group it gives the text; the group's own Value is cached.
            double Offset(Control control) => control.RenderTransform is Avalonia.Media.TransformGroup group
                ? group.Children.OfType<Avalonia.Media.TranslateTransform>().FirstOrDefault()?.Y ?? 0
                : control.RenderTransform?.Value.M32 ?? 0;

            // Frame by frame from the click: the clock ticks on every wait, as the app's render timer would.
            Find.Named<Button>(window, "SwapButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var (aBelow, bAbove) = (false, false);
            for (var i = 0; i < 60 && !(aBelow && bAbove); i++)
            {
                await Task.Delay(8);
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                aBelow |= Offset(fileA) > 1;
                bAbove |= Offset(fileB) < -1;
            }

            Assert.Equal("RELEASE.bin", shell.Compare.FileA);
            Assert.True(aBelow, "A's name never came from B's row");
            Assert.True(bAbove, "B's name never came from A's row");

            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(10);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(0, Offset(fileA), 3);
            Assert.Equal(0, Offset(fileB), 3);
        }
        finally
        {
            MotionSettings.AnimationsEnabled = animations;
        }
    }
}
