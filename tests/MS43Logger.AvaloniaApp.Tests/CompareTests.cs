using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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

    // As on the Tune rail: hovering a differing item shows its name, description and facts.
    [AvaloniaFact]
    public async Task HoveringADifferingItemShowsItsDescription()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        var entry = compare.Entries.OfType<CompareRow>().Single(row => row.Title == "ip_iga_ron98_pl__n__maf");
        Assert.Equal("Target ignition angle for RON98 during part and full load", entry.Description);
        Assert.Equal("Map, 20 × 16, °CRK, at 0x77315", entry.Detail);

        Find.Named<ListBox>(window, "DiffList").ScrollIntoView(entry);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var row = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("crow") && border.DataContext is CompareRow { Title: "ip_iga_ron98_pl__n__maf" });
        ToolTip.SetIsOpen(row, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var texts = ((Control)ToolTip.GetTip(row)!).GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();

        Assert.Contains("ip_iga_ron98_pl__n__maf", texts);
        Assert.Contains(entry.Description, texts);
        Assert.Contains(entry.Detail, texts);
    }

    [AvaloniaFact]
    public async Task TheDifferenceReadsInUnitsCountsOrPercent()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        compare.Selected = Item(compare, "ip_iga_ron98_pl__n__maf");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var grid = Find.Named<Controls.MapGrid>(window, "CompareGrid");
        Assert.Equal(DeltaMode.Units, compare.DeltaMode);
        Assert.Equal("A − B: -61.1 to +21.0 °CRK, 293 of 320 cells differ", compare.ScaleCaption);
        Assert.True(Find.Named<ToggleButton>(window, "DeltaRaw").IsEffectivelyVisible);

        compare.DeltaMode = DeltaMode.Raw;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var raw = compare.Grid!.Values.Raw;
        var (row, col) = Changed(compare);
        Assert.Equal((double)raw[row, col], compare.Grid.Values.Physical[row, col]);
        Assert.Equal((raw[row, col] > 0 ? "+" : "-") + Math.Abs(raw[row, col]), grid.CellText(row, col));
        Assert.StartsWith("A − B in counts: ", compare.ScaleCaption);
        Assert.StartsWith("largest change ", compare.LargestText);
        Assert.DoesNotContain("°CRK", compare.LargestText);

        compare.DeltaMode = DeltaMode.Percent;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.EndsWith(" %", grid.CellText(row, col));
        Assert.StartsWith("A − B as % of B: ", compare.ScaleCaption);
        Assert.EndsWith(" %", compare.LargestText);
        Assert.Equal(DeltaMode.Percent, compare.Settings.CompareDeltaMode);
        Screens.Save(window, "compare-percent");

        // The choice is about A − B only: on A or B it steps aside.
        compare.View = "a";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(Find.Named<ToggleButton>(window, "DeltaRaw").IsEffectivelyVisible);
    }

    private static (int Row, int Col) Changed(CompareViewModel compare)
    {
        var values = compare.Grid!.Values;
        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                if (values.Raw[r, c] != 0)
                {
                    return (r, c);
                }
            }
        }

        throw new InvalidOperationException("no cell differs");
    }

    [AvaloniaFact]
    public async Task CompareSearchLooksEverywhereLikeTheRail()
    {
        if (!Available)
        {
            return;
        }

        var (_, _, compare) = await Open();

        compare.SearchText = "0x77315";
        Assert.Equal(new[] { "ip_iga_ron98_pl__n__maf" }, Rows(compare));

        compare.SearchText = "ron98 part";
        Assert.Contains("ip_iga_ron98_pl__n__maf", Rows(compare));
        Assert.DoesNotContain("ip_iga_ron91_pl__n__maf", Rows(compare));
    }

    // Same crash as the Tune rail's category headers: selecting a row rebuilds the list inside the ListBox's own selection change.
    [AvaloniaFact]
    public async Task ClickingRowsAndTheBytesRowInTheListDoesNotCrash()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        var list = Find.Named<ListBox>(window, "DiffList");

        foreach (var title in Rows(compare).Take(12).ToList())
        {
            list.SelectedItem = compare.Entries.OfType<CompareRow>().Single(row => row.Difference.Item.Title == title);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(title, compare.Selected?.Item.Title);
        }

        list.SelectedItem = compare.Entries.OfType<CompareBytesRow>().Single();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(compare.ShowsBytes);
    }

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
        Assert.Equal("A − B: -61.1 to +21.0 °CRK, 293 of 320 cells differ", compare.ScaleCaption);
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
        var (window, shell) = TestShell.Create();
        window.Show();

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

    // A switch's choice ("Electrical cooling fan present, function and diagnosis active") was set in the 56 px
    // number style and cut off at the pane's edge. A choice is text: it wraps, at a size that reads.
    [AvaloniaFact]
    public async Task SwitchChoicesWrapAsText()
    {
        if (!Available)
        {
            return;
        }

        // B: the car bin with the cooling-fan switch flipped, so one switch differs.
        var xdf = XdfDefinition.LoadFromFile(TuneScreen.Xdf!);
        var fan = (XdfConstant)xdf.FindByTitle("c_conf_ecf")!;
        var image = BinImage.Load(TuneScreen.Car!);
        image.WriteElement(fan.Data, 0, image.ReadElement(fan.Data, 0) == 0 ? 1 : 0);
        var b = Path.Combine(_folder, "fan.bin");
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(b, image.ToArray());
        var (window, _, compare) = await Open(b);
        var row = Item(compare, "c_conf_ecf");
        Assert.Equal(MS43Logger.Core.Tuning.ItemKind.Switch, row.Kind);

        compare.Selected = row;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var a = Find.Named<TextBlock>(window, "ChoiceAText");
        var choiceB = Find.Named<TextBlock>(window, "ChoiceBText");
        Assert.True(a.IsEffectivelyVisible);
        Assert.True(choiceB.IsEffectivelyVisible);
        Assert.False(Find.Named<Control>(window, "ValueAText").IsEffectivelyVisible);
        Assert.Equal(row.ValueA, a.Text);
        Assert.Equal(row.ValueB, choiceB.Text);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, a.TextWrapping);
        Assert.True(a.FontSize <= 20, $"choice set at {a.FontSize} px");
        var right = a.TranslatePoint(new Point(a.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= window.Bounds.Width - 16, $"the choice ends at {right} in a {window.Bounds.Width} px window");
        Screens.Save(window, "compare-switch-night-1440");
    }

    // As on the Tune rail: the list is rebuilt on a click (the current row moves), and the clicked row stays put.
    [AvaloniaFact]
    public async Task TheClickedRowStaysWhereItWas()
    {
        if (!Available)
        {
            return;
        }

        var (window, _, compare) = await Open();
        var list = Find.Named<ListBox>(window, "DiffList");
        var rows = compare.Entries.OfType<CompareRow>().ToList();
        var title = rows[rows.Count / 2].Difference.Item.Title;
        int Index() => compare.Entries.Select((entry, i) => (entry, i)).Single(pair => pair.entry is CompareRow row && row.Difference.Item.Title == title).i;
        double? Top() => list.ContainerFromIndex(Index()) is Control row && row.TranslatePoint(default, list) is { } at ? at.Y : null;
        list.ScrollIntoView(Index());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var before = Top();
        Assert.NotNull(before);

        list.SelectedItem = compare.Entries[Index()];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(title, compare.Selected?.Item.Title);
        Assert.Equal(before!.Value, Assert.NotNull(Top()), 1);
    }

    // An arrow reads as "was, now": B (the bin compared with) then A (the bin tuned), as A − B reads.
    [AvaloniaFact]
    public async Task ValueRowsReadFromBToA()
    {
        if (!Available)
        {
            return;
        }

        var (_, _, compare) = await Open();

        var row = compare.Entries.OfType<CompareRow>().Single(entry => entry.Difference.Item.Title == "c_lc_n_max");

        Assert.Equal("3520 → 4512", row.Summary);
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

        Assert.Equal("A − B: +992 rpm", compare.ValueDeltaText);
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
