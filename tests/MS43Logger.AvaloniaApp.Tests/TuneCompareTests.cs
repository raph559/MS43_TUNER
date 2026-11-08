using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Compare inside Tune: with a B open, the map shows A, B or A − B, differing cells are ringed, and B's values copy in.</summary>
public sealed class TuneCompareTests : IDisposable
{
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-tcmp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell)?> Open(string select = "ip_iga_ron98_pl__n__maf", bool withB = true)
    {
        if (!TuneScreen.Available || Release is null)
        {
            return null;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: select, binPath: TuneScreen.CopyCar(_folder));
        if (withB)
        {
            Assert.True(shell.Compare.OpenB(Release));
        }

        shell.SelectedTab = ShellViewModel.TuneTab;
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static TableComparison Compared(ShellViewModel shell, string title, BinImage b) =>
        BinComparer.CompareTable(shell.Tune.Document!.Xdf, shell.Tune.Document.Image, b, (XdfTable)shell.Tune.Document.Xdf.FindByTitle(title)!);

    [AvaloniaFact]
    public async Task WithoutBThereIsNoCompareMode()
    {
        if (await Open(withB: false) is not var (window, shell))
        {
            return;
        }

        Assert.False(shell.Tune.ShowsCompare);
        Assert.False(Find.Named<Control>(window, "TuneCompareSeg").IsEffectivelyVisible);
        Assert.False(Find.Named<Button>(window, "CopyFromBTool").IsEffectivelyVisible);
    }

    // The owner: Tune showed no compare until Compare had been opened once ("Edit in Tune"); the remembered B opened only
    // when the Compare tab was shown.
    [AvaloniaFact]
    public async Task TheRememberedBShowsOnTuneWithoutVisitingCompare()
    {
        if (!TuneScreen.Available || Release is null)
        {
            return;
        }

        var settingsFolder = Path.Combine(_folder, "settings");
        var settings = AppSettings.Load(settingsFolder);
        settings.LastCompareBin = Release;
        settings.Save();
        var shell = new ShellViewModel(new MainWindowViewModel(ports: () => ["COM1"]), new TuneViewModel(AppSettings.Load(settingsFolder)));
        var window = new MainWindow(shell);
        shell.SelectedTab = ShellViewModel.TuneTab;
        window.Show();

        await shell.Tune.OpenAsync(TuneScreen.CopyCar(_folder), TuneScreen.Xdf);
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf");
        Dispatcher.UIThread.RunJobs();

        Assert.False(shell.Compare.IsVisible);
        Assert.Equal(Path.GetFileName(Release), shell.Tune.CompareFileB);
        Assert.True(shell.Tune.ShowsCompareGrid);
        Assert.True(Find.Named<Border>(window, "CompareStrip").IsEffectivelyVisible);
    }

    // The owner: the compare controls sat among the edit tools with nothing to say they belong to Compare.
    [AvaloniaFact]
    public async Task CompareControlsSitInTheirOwnLabelledStrip()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        var strip = Find.Named<Border>(window, "CompareStrip");
        var toolbar = Find.Named<WrapPanel>(window, "ToolBar");
        Assert.True(strip.IsEffectivelyVisible);
        Assert.Equal("Compare with " + shell.Tune.CompareFileB, Find.Named<TextBlock>(window, "CompareStripLabel").Text);
        foreach (var name in new[] { "TuneCompareSeg", "CopyFromBTool" })
        {
            var control = Find.Named<Control>(window, name);
            Assert.Contains(strip, control.GetVisualAncestors());
            Assert.DoesNotContain(toolbar, control.GetVisualAncestors());
        }

        Assert.True(strip.Bounds.Top >= toolbar.Bounds.Bottom, $"strip at {strip.Bounds.Top}, toolbar ends {toolbar.Bounds.Bottom}");

        shell.Tune.IsGridDelta = true;
        window.Width = 1440;
        window.Height = 900;
        Screens.Save(window, "tune-compare-strip-night");
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Screens.Save(window, "tune-compare-strip-day");
        Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public async Task WithoutBTheStripIsHidden()
    {
        if (await Open(withB: false) is not var (window, _))
        {
            return;
        }

        Assert.False(Find.Named<Border>(window, "CompareStrip").IsEffectivelyVisible);
    }

    // As on Compare (TunerPro's compare): A − B in units, in counts, or as a percentage of B, shown with the A − B grid.
    [AvaloniaFact]
    public async Task TheDifferenceReadsInUnitsRawOrPercent()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        var tune = shell.Tune;
        var compared = Compared(shell, "ip_iga_ron98_pl__n__maf", BinImage.Load(Release!));
        var seg = Find.Named<Border>(window, "TuneDeltaSeg");
        Assert.False(seg.IsEffectivelyVisible);

        tune.IsGridDelta = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(seg.IsEffectivelyVisible);
        Assert.True(Find.Named<ToggleButton>(window, "TuneDeltaUnits").IsChecked);

        Find.Named<ToggleButton>(window, "TuneDeltaPercent").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var percent = BinComparer.Difference(compared, DeltaMode.Percent);
        Assert.Equal(DeltaMode.Percent, tune.ShownEditor!.DeltaMode);
        Assert.Equal(percent.Physical[0, 0], tune.ShownEditor.Values.Physical[0, 0]);
        Assert.Equal(DeltaMode.Percent, shell.Compare.DeltaMode);
        Assert.Equal(DeltaMode.Percent, tune.Settings.CompareDeltaMode);

        Find.Named<ToggleButton>(window, "TuneDeltaRaw").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(compared.Delta.Raw[0, 0], tune.ShownEditor!.Values.Physical[0, 0]);
    }

    [AvaloniaFact]
    public async Task ACompareChoiceMadeOnCompareShowsInTune()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        shell.Compare.DeltaMode = DeltaMode.Raw;
        shell.Tune.IsGridDelta = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Find.Named<ToggleButton>(window, "TuneDeltaRaw").IsChecked);
        Assert.Equal(DeltaMode.Raw, shell.Tune.ShownEditor!.DeltaMode);
    }

    [AvaloniaFact]
    public async Task TheMapShowsAOrBOrTheDifferenceWithoutLeavingTune()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        var tune = shell.Tune;
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        var compared = Compared(shell, "ip_iga_ron98_pl__n__maf", BinImage.Load(Release!));
        Assert.True(Find.Named<ToggleButton>(window, "TuneViewB").IsEffectivelyVisible);
        Assert.Same(tune.Editor, grid.Editor);

        tune.Editor!.Selection = new CellRange(3, 4, 5, 6);
        tune.GridView = "b";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(compared.B.Physical[3, 4], grid.Editor!.Values.Physical[3, 4]);
        // This map's row axis differs between the two bins: the note says the cells are compared position by position.
        Assert.Contains("position by position", tune.CompareAxisNote);
        Assert.True(grid.Editor.IsReadOnly);
        Assert.Equal(new CellRange(3, 4, 5, 6), grid.Editor.Selection);
        Screens.Save(window, "tune-compare-b");

        tune.GridView = "delta";
        Dispatcher.UIThread.RunJobs();
        Assert.True(grid.Editor!.IsDelta);
        Assert.Equal(compared.Delta.Physical[3, 4], grid.Editor.Values.Physical[3, 4]);

        // A selection made on B or A − B is A's selection too.
        grid.Editor.Selection = new CellRange(8, 1, 8, 1);
        tune.GridView = "a";
        Dispatcher.UIThread.RunJobs();
        Assert.Same(tune.Editor, grid.Editor);
        Assert.Equal(new CellRange(8, 1, 8, 1), tune.Editor.Selection);
    }

    [AvaloniaFact]
    public async Task CellsThatDifferFromBAreRingedOnA()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        var compared = Compared(shell, "ip_iga_ron98_pl__n__maf", BinImage.Load(Release!));
        Assert.Equal(compared.Changed, shell.Tune.Editor!.DiffersCells);
        Screens.Save(window, "tune-compare-a");
    }

    [AvaloniaFact]
    public async Task CopyFromBWritesTheSelectionInOneUndoStep()
    {
        if (await Open() is not var (window, shell))
        {
            return;
        }

        var tune = shell.Tune;
        var b = Compared(shell, "ip_iga_ron98_pl__n__maf", BinImage.Load(Release!)).B;
        var editor = tune.Editor!;
        editor.Selection = new CellRange(3, 4, 4, 5);
        var before = editor.Values.Raw[3, 4];

        Assert.True(tune.CopyFromB());

        Assert.Equal(b.Raw[3, 4], editor.Values.Raw[3, 4]);
        Assert.Equal(b.Raw[4, 5], editor.Values.Raw[4, 5]);
        Assert.StartsWith("Undo: Copy ", tune.UndoTip);
        Assert.EndsWith("from RELEASE.bin".Replace("RELEASE.bin", Path.GetFileName(Release!)), tune.UndoTip);
        Assert.DoesNotContain((3, 4), editor.DiffersCells);
        tune.Undo();
        Assert.Equal(before, editor.Values.Raw[3, 4]);

        // Ctrl Shift B does the same.
        window.KeyPress(Key.B, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.B, "B");
        Assert.Equal(b.Raw[3, 4], editor.Values.Raw[3, 4]);
    }

    [AvaloniaFact]
    public async Task TheKeyLineReadsBAtTheActiveCell()
    {
        if (await Open() is not var (_, shell))
        {
            return;
        }

        var tune = shell.Tune;
        var compared = Compared(shell, "ip_iga_ron98_pl__n__maf", BinImage.Load(Release!));
        tune.Editor!.Selection = new CellRange(3, 4, 3, 4);
        Dispatcher.UIThread.RunJobs();

        Assert.StartsWith("B ", tune.CompareText);
        Assert.Contains(ValueCodec.Format(compared.B.Physical[3, 4], compared.B.Raw[3, 4], compared.A.Table.Z.DecimalPlaces, compared.A.Table.Z.OutputType, compared.A.Table.Z.Data.ElementBits), tune.CompareText);
    }

    [AvaloniaFact]
    public async Task AValueTakesBsValue()
    {
        if (await Open(select: "c_tco_min_aj") is not var (window, shell))
        {
            return;
        }

        var tune = shell.Tune;
        var constant = (XdfConstant)tune.SelectedItem!;
        var bRaw = BinImage.Load(Release!).ReadElement(constant.Data, 0);
        Assert.NotEqual(bRaw, tune.Value!.Current.Raw);
        Assert.True(Find.Named<Button>(window, "UseBValueButton").IsEffectivelyVisible);
        Assert.StartsWith("B ", tune.CompareText);

        Assert.True(tune.CopyFromB());

        Assert.Equal(bRaw, tune.Value.Current.Raw);
    }
}
