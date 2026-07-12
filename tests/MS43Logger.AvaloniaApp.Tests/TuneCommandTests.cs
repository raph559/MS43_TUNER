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
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);

        Assert.True(shell.Tune.Save());

        Assert.Equal("Saved", shell.Tune.SaveStateText);
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
        Assert.Equal("Saved", shell.Tune.SaveStateText);

        // Ctrl K focuses the search field of the tab in front.
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Assert.True(Find.Named<TextBox>(window, "SearchBox").IsFocused);

        shell.SelectedTab = ShellViewModel.LiveTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Assert.Equal(ShellViewModel.LiveTab, shell.SelectedTab);
        Assert.True(Find.Named<TextBox>(window, "ChannelFilter").IsFocused);
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
        Assert.EndsWith(". The file on disk is unchanged.", error.Text);
        Assert.Equal(TuneAction.SaveAs, shell.Tune.ErrorAction);
        Assert.Equal("Save as…", Find.Named<Button>(window, "ErrorActionButton").Content);
    }

    [AvaloniaFact]
    public async Task FactsFollowEdits()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(select: "c_lc_n_max");
        shell.Tune.Value!.Input = "4000";
        shell.Tune.Value.Apply();
        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "Value", Text: "4000 rpm", Detail: "raw 125 (0x7D)" });

        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf");
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 19, 15);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Set, 60));
        Assert.Equal("All 320 cells are 60.0 °CRK", shell.Tune.ScaleCaption);
    }
}
