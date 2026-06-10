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
        Assert.Equal(("All", compare.Result!.Items.Count), (compare.Chips[0].Name, compare.Chips[0].Count));
        Assert.True(Find.Named<ToggleButton>(window, "KindAll").IsEffectivelyVisible);
        Assert.Equal("CAR.bin", compare.FileA);
        Assert.Equal("MS43X001_release_E46_M54B25.bin", compare.FileB);
        Assert.Equal("18 ranges, 11907 bytes outside the XDF (11436 differ)", compare.UnmappedSummary);
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
        Assert.Equal("B − A: -21.0 to +61.1 °CRK, 293 of 320 cells differ", compare.ScaleCaption);
        Assert.Contains(compare.Entries.OfType<CompareRow>(), row => row.IsCurrent && row.Difference.Item.Title == "ip_iga_ron98_pl__n__maf");
        Screens.Save(window, "compare-delta-night");
        shell.IsNight = false;
        Screens.Save(window, "compare-delta-day");
        shell.IsNight = true;

        compare.View = "a";
        var table = (XdfTable)compare.Selected!.Item;
        Assert.False(compare.Grid!.IsDelta);
        Assert.Equal(CellMark.Differs, compare.Grid.Mark);
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
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);
        Assert.Contains("c_inj_flow", compare.Message);
        Assert.NotNull(compare.Selected);

        shell.Tune.Undo();

        Assert.Contains("c_inj_flow", Rows(compare));
        Assert.Equal("Saved", shell.Tune.SaveStateText);
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

        Assert.StartsWith("Not opened: ", compare.ErrorMessage);
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
    public async Task EditInTuneSelectsTheItem()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, compare) = await Open();
        compare.Selected = Item(compare, "c_inj_flow");

        compare.ShowInTune();

        Assert.True(shell.IsTune);
        Assert.Equal("c_inj_flow", shell.Tune.SelectedItem!.Title);
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

    [AvaloniaFact]
    public async Task TuneRailFiltersItemsThatDifferFromB()
    {
        if (!Available)
        {
            return;
        }

        var (window, shell, compare) = await Open();
        var tune = shell.Tune;
        shell.SelectedTab = ShellViewModel.TuneTab;

        Assert.Equal("MS43X001_release_E46_M54B25.bin", tune.CompareFileB);
        tune.DiffersOnly = true;

        var items = tune.Rail.OfType<RailItem>().ToList();
        Assert.Equal(compare.Result!.Items.Count, items.Count);
        Assert.All(items, item => Assert.True(item.IsDifferent));
        Assert.Contains(tune.Rail.OfType<RailGroup>(), group => group.Title.Contains("MS43X001_release_E46_M54B25.bin"));
        Screens.Save(window, "tune-differs-from-b");

        tune.SearchText = "c_inj";
        Assert.Equal(new[] { "c_inj_flow" }, tune.Rail.OfType<RailItem>().Select(item => item.Title));
    }

    [AvaloniaFact]
    public async Task DiffersFilterFollowsEditsOnTheTuneTab()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, _) = await Open();
        var tune = shell.Tune;
        shell.SelectedTab = ShellViewModel.TuneTab;
        tune.DiffersOnly = true;
        var flow = (XdfConstant)tune.Document!.Xdf.FindByTitle("c_inj_flow")!;

        tune.History!.Apply(TableEdits.CopyFrom(tune.Document, flow, BinImage.Load(Release!)), "Copy");

        Assert.DoesNotContain("c_inj_flow", tune.Rail.OfType<RailItem>().Select(item => item.Title));
    }

    [AvaloniaFact]
    public async Task DiffersFilterIsHiddenWithoutB()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null);

        Assert.Null(shell.Tune.CompareItems);
        Assert.False(Find.Named<ToggleSwitch>(window, "DiffersOnlyBox").IsVisible);
    }

    [AvaloniaFact]
    public async Task CopyOfATableOnAnotherAxisSaysSo()
    {
        if (!Available)
        {
            return;
        }

        var (_, _, compare) = await Open();
        compare.Selected = Item(compare, "ip_iga_ron98_pl__n__maf");

        Assert.True(compare.CopySelectedFromB());

        Assert.Contains("ldpm_n_14", compare.Message);
    }

    [AvaloniaFact]
    public async Task RailRingsFollowEditsWithTheFilterOff()
    {
        if (!Available)
        {
            return;
        }

        var (_, shell, _) = await Open();
        var tune = shell.Tune;
        shell.SelectedTab = ShellViewModel.TuneTab;
        tune.SearchText = "c_inj_flow";
        Assert.True(tune.Rail.OfType<RailItem>().Single(item => item.Title == "c_inj_flow").IsDifferent);
        var flow = (XdfConstant)tune.Document!.Xdf.FindByTitle("c_inj_flow")!;

        tune.History!.Apply(TableEdits.CopyFrom(tune.Document, flow, BinImage.Load(Release!)), "Copy");

        Assert.False(tune.Rail.OfType<RailItem>().Single(item => item.Title == "c_inj_flow").IsDifferent);
    }

    [AvaloniaFact]
    public async Task ClickingTheActiveKindChipKeepsItOn()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        var all = Find.Named<ToggleButton>(window, "KindAll");
        var centre = all.TranslatePoint(new Avalonia.Point(all.Bounds.Width / 2, all.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("all", compare.Kind);
        Assert.True(Find.Named<ToggleButton>(window, "KindAll").IsChecked);
    }

    [AvaloniaFact]
    public async Task BytesViewShowsWholeRanges()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();

        compare.ShowBytes();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("0x03C43 to 0x03C9B", CompareViewModel.RangeText(compare.Unmapped[0]));
        Assert.Contains(Find.Named<ItemsControl>(window, "BytesTable").GetVisualDescendants().OfType<Controls.NumberText>(), text => text.Value == "0x03C43 to 0x03C9B");
        Screens.Save(window, "compare-bytes");
    }

    [AvaloniaFact]
    public async Task SearchFiltersTheCompareList()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();

        compare.SearchText = "c_inj";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "c_inj_flow" }, Rows(compare));
        Assert.True(Find.Named<TextBox>(window, "CompareSearchBox").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task ValueViewShowsTheDifference()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();

        compare.Selected = Item(compare, "c_lc_n_max");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("B − A: -992 rpm", compare.ValueDeltaText);
        Assert.Equal("4512", Find.Named<Controls.ReadingText>(window, "ValueAText").Value);
        Assert.Equal("3520", Find.Named<Controls.ReadingText>(window, "ValueBText").Value);
        Assert.True(Find.Named<Control>(window, "ValueDeltaText").IsEffectivelyVisible);
        Screens.Save(window, "restyle-compare-night-1440-value");
    }

    [AvaloniaFact]
    public async Task NoBSaysWhatAIs()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: null, binPath: TuneScreen.CopyCar(_folder));
        shell.SelectedTab = ShellViewModel.CompareTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = Find.Named<TextBlock>(window, "NoBText");
        Assert.True(text.IsEffectivelyVisible);
        Assert.Equal("A is CAR.bin. B can be any 512 KiB MS43 bin; both are read through A's XDF.", text.Text);
    }

    [AvaloniaFact]
    public async Task KindLabelsFitAt1024()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, _) = await Open();
        window.Width = 1024;
        window.Height = 680;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Each segment is as wide as its label at least, so "Curves" and "Values" are not cut.
        foreach (var name in new[] { "KindAll", "KindMap", "KindCurve", "KindAxis", "KindValue" })
        {
            var segment = Find.Named<ToggleButton>(window, name);
            var label = segment.GetVisualDescendants().OfType<TextBlock>().First();

            // A text measured in too little room reports the room, so its natural width is measured apart.
            var natural = new TextBlock { Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily, FontWeight = label.FontWeight };
            natural.Measure(Size.Infinity);
            Assert.True(natural.DesiredSize.Width <= segment.Bounds.Width - segment.Padding.Left - segment.Padding.Right + 0.5,
                $"{name}: \"{label.Text}\" needs {natural.DesiredSize.Width} px, the segment has {segment.Bounds.Width}");
        }
    }

    [AvaloniaFact]
    public async Task TheCurrentRowIsInView()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        var list = Find.Named<ListBox>(window, "DiffList");

        compare.Selected = compare.Result!.Items[^1];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var row = list.GetVisualDescendants().OfType<ListBoxItem>().SingleOrDefault(item => item.DataContext is CompareRow { IsCurrent: true });
        Assert.NotNull(row);
        var top = row!.TranslatePoint(default, list)!.Value.Y;
        Assert.InRange(top, 0, list.Bounds.Height - row.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task CompareScreensForReview()
    {
        if (!Available)
        {
            return;
        }

        var (window, shell, compare) = await Open();
        void Save(string state)
        {
            foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
            {
                shell.IsNight = night;
                shell.IsDay = !night;
                foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
                {
                    window.Width = width;
                    window.Height = height;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Screens.Save(window, $"restyle-compare-{state}-{theme}-{width}");
                }
            }
        }

        compare.Selected = Item(compare, "ip_iga_ron98_pl__n__maf");
        Save("delta");
        compare.View = "a";
        Save("a");
        compare.View = "delta";
        compare.Selected = Item(compare, "c_lc_n_max");
        Save("value");
        compare.ShowBytes();
        Save("bytes");
    }

    [AvaloniaFact]
    public async Task FileAFollowsSaveAs()
    {
        if (!Available)
        {
            return;
        }

        var (window, shell, _) = await Open();

        Assert.True(shell.Tune.Save(Path.Combine(_folder, "NEW.bin")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("NEW.bin", Find.Named<TextBlock>(window, "FileAText").Text);
    }
}
