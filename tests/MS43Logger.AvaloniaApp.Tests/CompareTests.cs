using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class CompareTests : IDisposable
{
    private static readonly string? Release = TuneRailTests.Local("MS43X001_release_E46_M54B25.bin");
    private static readonly string? Stock = TuneRailTests.Local("MS43_430069_stock.bin");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-cmp-" + Guid.NewGuid().ToString("N"));

    private static bool Available => TuneScreen.Available && Release is not null && Stock is not null;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A copy of the car file as A on the Compare tab, with the release bin as B.</summary>
    private async Task<(MainWindow Window, ShellViewModel Shell, CompareViewModel Compare)> Open(string? b = null)
    {
        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: TuneScreen.CopyCar(_folder));
        shell.SelectedTab = ShellViewModel.CompareTab;
        Assert.True(shell.Compare.OpenB(b ?? Release!));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, shell, shell.Compare);
    }

    private static ItemDifference Item(CompareViewModel compare, string title) =>
        compare.Result!.Items.Single(item => item.Item.Title == title);

    private static IEnumerable<string> Rows(CompareViewModel compare) =>
        compare.Entries.OfType<CompareRow>().Select(row => row.Difference.Item.Title);

    [AvaloniaFact]
    public async Task CompareTabListsDifferencesGroupedByCategory()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();

        Assert.True(window.GetVisualDescendants().OfType<CompareView>().Single().IsVisible);
        Assert.NotNull(window.FindControl<ToggleButton>("TabCompare"));
        Assert.IsType<CompareGroup>(compare.Entries[0]);
        Assert.Contains("c_inj_flow", Rows(compare));
        Assert.Contains("ip_iga_ron98_pl__n__maf", Rows(compare));
        Assert.Equal($"All {compare.Result!.Items.Count}", compare.Chips[0].Label);
        Assert.Equal("CAR.bin", compare.FileA);
        Assert.Equal("MS43X001_release_E46_M54B25.bin", compare.FileB);
        Assert.Contains("18 ranges", compare.UnmappedSummary);
        Assert.Contains("11,907 bytes", compare.UnmappedSummary);
        Assert.Contains("11,436 differ", compare.UnmappedSummary);
        Assert.Equal(compare.Result.Items.Count, Rows(compare).Count());
        Screens.Save(window, "compare-list");
    }

    [AvaloniaFact]
    public async Task KindChipFilters()
    {
        if (!Available)
        {
            return;
        }

        var (_, _, compare) = await Open();

        compare.Kind = "value";

        Assert.All(compare.Entries.OfType<CompareRow>(), row => Assert.Contains(row.Difference.Kind, new[] { ItemKind.Value, ItemKind.Switch, ItemKind.Flag }));
        Assert.Contains("c_inj_flow", Rows(compare));
        Assert.DoesNotContain("ip_iga_ron98_pl__n__maf", Rows(compare));
        Assert.True(compare.Chips.Single(chip => chip.Key == "value").IsOn);
    }

    [AvaloniaFact]
    public async Task SelectingTableShowsDeltaWithAxisBanner()
    {
        if (!Available)
        {
            return;
        }

        var (window, shell, compare) = await Open();

        compare.Selected = Item(compare, "ip_iga_ron98_pl__n__maf");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(compare.Grid!.IsDelta);
        Assert.Contains("row axis differs", compare.AxisBanner);
        Assert.EndsWith("B higher", compare.LegendMax);
        Assert.Contains(compare.Entries.OfType<CompareRow>(), row => row.IsCurrent && row.Difference.Item.Title == "ip_iga_ron98_pl__n__maf");
        Screens.Save(window, "compare-delta-night");
        shell.IsNight = false;
        Screens.Save(window, "compare-delta-day");
        shell.IsNight = true;

        compare.View = "a";
        var table = (XdfTable)compare.Selected!.Item;
        Assert.False(compare.Grid!.IsDelta);
        Assert.Equal(shell.Tune.Document!.Reader.ReadTable(table).Physical, compare.Grid.Values.Physical);
        compare.View = "b";
        Assert.NotEqual(shell.Tune.Document!.Reader.ReadTable(table).Physical, compare.Grid!.Values.Physical);
    }

    [AvaloniaFact]
    public async Task ValueShowsBothSides()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();

        compare.Selected = Item(compare, "c_lc_n_max");

        Assert.Null(compare.Grid);
        Assert.StartsWith("4512", compare.Selected!.ValueA);
        Assert.Equal("rpm", compare.Units);
        Screens.Save(window, "compare-value");
    }

    [AvaloniaFact]
    public async Task CopyFromBIsUndoableAndRefreshes()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, compare) = await Open();
        var count = compare.Result!.Items.Count;
        compare.Selected = Item(compare, "c_inj_flow");

        Assert.True(compare.CopySelectedFromB());

        Assert.DoesNotContain("c_inj_flow", Rows(compare));
        Assert.Equal(count - 1, compare.Result!.Items.Count);
        Assert.Equal("1 unsaved change", shell.Tune.DirtyText);
        Assert.Contains("c_inj_flow", compare.Message);
        Assert.NotNull(compare.Selected);

        shell.Tune.Undo();

        Assert.Contains("c_inj_flow", Rows(compare));
        Assert.Equal("Saved", shell.Tune.DirtyText);
    }

    [AvaloniaFact]
    public async Task EditOnTuneTabRefreshesCompare()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, compare) = await Open();
        var listed = compare.Result!.Items.Select(item => item.Item).ToHashSet();
        var table = shell.Tune.Document!.Xdf.Tables.First(t => !listed.Contains(t) && t.Z.Data.IsSupported && t.Z.Data.Count > 4 && shell.Tune.Catalog!.Kind(t) == ItemKind.Map);

        shell.SelectedTab = ShellViewModel.TuneTab;
        shell.Tune.SelectedItem = table;
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        if (!shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1)))
        {
            Assert.True(shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, -1)));
        }

        shell.SelectedTab = ShellViewModel.CompareTab;

        Assert.Contains(table.Title, Rows(compare));
    }

    [AvaloniaFact]
    public async Task WrongSizeBIsRefused()
    {
        if (!Available)
        {
            return;
        }

        var (_, _, compare) = await Open();
        var small = Path.Combine(_folder, "small.bin");
        File.WriteAllBytes(small, new byte[1024]);

        Assert.False(compare.OpenB(small));

        Assert.Contains("512 KiB", compare.ErrorMessage);
        Assert.Equal("MS43X001_release_E46_M54B25.bin", compare.FileB);
        Assert.NotNull(compare.Result);
    }

    [AvaloniaFact]
    public async Task StockBWarnsAboutFirmware()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open(Stock);

        Assert.Contains("430069", compare.FirmwareWarning);
        Screens.Save(window, "compare-stock");
    }

    [AvaloniaFact]
    public async Task IdenticalBShowsNoDifferences()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open(TuneScreen.Car);

        Assert.True(compare.IsIdentical);
        Assert.Empty(compare.Entries);
        Assert.True(Find.Named<TextBlock>(window, "IdenticalText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task LastCompareBinReopens()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, _) = await Open();
        var folder = shell.Tune.Settings.Folder;

        var again = new ShellViewModel(new MainWindowViewModel(), new TuneViewModel(AppSettings.Load(folder)));
        await again.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        again.SelectedTab = ShellViewModel.CompareTab;

        Assert.Equal("MS43X001_release_E46_M54B25.bin", again.Compare.FileB);
        Assert.NotNull(again.Compare.Result);
    }

    [AvaloniaFact]
    public void CompareWithoutABinAsksForOne()
    {
        var window = new MainWindow();
        window.Show();
        var shell = (ShellViewModel)window.DataContext!;

        shell.SelectedTab = ShellViewModel.CompareTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(shell.Compare.HasA);
        Assert.True(Find.Named<TextBlock>(window, "NoAText").IsEffectivelyVisible);
    }
}
