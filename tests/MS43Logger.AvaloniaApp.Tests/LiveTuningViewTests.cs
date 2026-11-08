using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp.Tests;

public class LiveTuningViewTests
{
    private static (MainWindow Window, ShellViewModel Shell) Shown()
    {
        var (window, shell) = TestShell.Create();
        window.Show();
        window.FindControl<ToggleButton>("TabLiveTuning")!.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    [AvaloniaFact]
    public void TheTab_ShowsThePage()
    {
        var (window, shell) = Shown();
        Assert.Equal(ShellViewModel.LiveTuningTab, shell.SelectedTab);
        Assert.True(window.GetVisualDescendants().OfType<LiveTuningView>().Single().IsVisible);
        Assert.False(window.GetVisualDescendants().OfType<TuneView>().Single().IsVisible);
    }

    [AvaloniaFact]
    public void AnUnverifiedCatalogueIsHiddenInsteadOfAdvertisingDefaultMaps()
    {
        var (window, _) = Shown();
        var list = Find.Named<ListBox>(window, "LiveTuningMaps");
        Assert.False(list.IsEffectivelyVisible);
        Assert.Equal("Connect to check live-map support.", Find.Named<TextBlock>(window, "LiveCatalogueText").Text);
    }

    [AvaloniaFact]
    public void RevertAll_IsVisibleInEveryState()
    {
        var (window, shell) = Shown();
        Assert.True(shell.LiveTuning.IsReadOnly);
        var revert = Find.Named<Button>(window, "LiveTuningRevert");
        Assert.True(revert.IsEffectivelyVisible);
        Assert.Equal("Revert all", revert.Content);
    }

    [AvaloniaFact]
    public void TheNotProvenNote_IsShown()
    {
        var (window, _) = Shown();
        var note = Find.Named<TextBlock>(window, "LiveTuningNotProven");
        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal("The live-tuning overlay has been tested in the emulator. Real ECU operation remains unvalidated.", note.Text);
    }

    [AvaloniaFact]
    public void ReadOnly_ShowsTheReason_AndDisablesTheGrid()
    {
        var (window, _) = Shown();
        Assert.True(Find.Named<Control>(window, "LiveTuningReadOnly").IsEffectivelyVisible);
        Assert.Equal("Not connected.", Find.Named<TextBlock>(window, "LiveTuningReadOnlyText").Text);
        Assert.False(Find.Named<MapGrid>(window, "LiveTuningGrid").IsEffectivelyEnabled);
        Assert.False(Find.Named<Button>(window, "LiveTuningSend").IsEffectivelyEnabled);
    }
}
