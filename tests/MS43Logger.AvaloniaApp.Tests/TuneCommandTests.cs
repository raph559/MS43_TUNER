using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TuneCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-cmd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell, string Path)> OpenCopy()
    {
        var path = TuneScreen.CopyCar(_folder);
        var (window, shell) = await TuneScreen.OpenAsync(binPath: path);
        return (window, shell, path);
    }

    private static void EditOneCell(ShellViewModel shell)
    {
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
    }

    [AvaloniaFact]
    public async Task ToolbarAppliesToTheSelection()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await OpenCopy();
        shell.Tune.Editor!.Selection = new CellRange(12, 0, 12, 0);
        shell.Tune.ToolValue = "-10";

        Assert.True(shell.Tune.RunTool(EditKind.Add));
        Assert.Equal(21.375, shell.Tune.Editor.Values.Physical[12, 0]);
        Assert.True(shell.Tune.CanUndo);
        Screens.Save(window, "tune-toolbar");

        shell.Tune.Undo();
        Assert.Equal(31.5, shell.Tune.Editor.Values.Physical[12, 0]);
        shell.Tune.Redo();
        Assert.Equal(21.375, shell.Tune.Editor.Values.Physical[12, 0]);

        shell.Tune.ToolValue = "abc";
        Assert.False(shell.Tune.RunTool(EditKind.Set));
        Assert.Contains("abc", shell.Tune.Editor.Message);
    }

    [AvaloniaFact]
    public async Task SaveWritesFileAndClearsCount()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, path) = await OpenCopy();
        EditOneCell(shell);
        Assert.Equal("1 unsaved change", shell.Tune.DirtyText);

        Assert.True(shell.Tune.Save());

        Assert.Equal("Saved", shell.Tune.DirtyText);
        Assert.Equal(81, File.ReadAllBytes(path)[0x77315]);
        Assert.True(File.Exists(path + ".bak"));
        Assert.True(File.Exists(Path.Combine(_folder, "CAR.log")));
    }

    [AvaloniaFact]
    public async Task CloseWithUnsavedEditsAsks()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, path) = await OpenCopy();
        var asked = 0;
        var answer = SaveChoice.Cancel;
        shell.Tune.AskToSave = _ =>
        {
            asked++;
            return Task.FromResult(answer);
        };
        EditOneCell(shell);

        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.Equal(1, asked);

        answer = SaveChoice.DontSave;
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        Assert.Equal(80, File.ReadAllBytes(path)[0x77315]);
    }

    [AvaloniaFact]
    public async Task OpeningAnotherBinAsks()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, path) = await OpenCopy();
        var document = shell.Tune.Document;
        shell.Tune.AskToSave = _ => Task.FromResult(SaveChoice.Cancel);
        EditOneCell(shell);

        Assert.False(await shell.Tune.ConfirmDiscardAsync());
        Assert.Same(document, shell.Tune.Document);

        shell.Tune.AskToSave = _ => Task.FromResult(SaveChoice.Save);
        Assert.True(await shell.Tune.ConfirmDiscardAsync());
        Assert.Equal(81, File.ReadAllBytes(path)[0x77315]);
    }

    [AvaloniaFact]
    public async Task CtrlKFocusesSearchAndCtrlZUndoes()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await OpenCopy();
        EditOneCell(shell);

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Assert.Equal("Saved", shell.Tune.DirtyText);

        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Assert.True(Find.Named<TextBox>(window, "SearchBox").IsFocused);
    }
}

public sealed class ReviewFixTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-fix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task FailedSaveIsShownWhileEditing()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));

        Assert.False(shell.Tune.Save(Path.Combine(_folder, "missing", "CAR.bin")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var error = Find.Named<TextBlock>(window, "EditError");
        Assert.True(error.IsEffectivelyVisible);
        Assert.StartsWith("Not saved", error.Text);
    }

    [AvaloniaFact]
    public async Task OpenAndSaveAsAreReachableWithABinOpen()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();

        foreach (var name in new[] { "OpenBinTool", "OpenXdfTool", "SaveAsTool" })
        {
            Assert.True(Find.Named<Button>(window, name).IsEffectivelyVisible, name);
        }
    }
}
