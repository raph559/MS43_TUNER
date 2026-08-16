using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TunePaneTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-tunepane-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static List<string?> KeyLineTexts(Window window) =>
        Find.Named<Control>(window, "KeyLine").GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).Select(text => text is Controls.NumberText number ? number.Value : text.Text).ToList();

    [AvaloniaFact]
    public async Task KeyLineDescribesARange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();

        // Rows 9 to 12 hold 3750 to 4500 rpm on ldpm_n_14; columns 4 to 6 hold 130.0 to 200.0 mg/stk.
        shell.Tune.Editor!.Selection = new CellRange(9, 4, 12, 6);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("4 rows × 3 columns: 3750 to 4500 rpm, 130.0 to 200.0 mg/stk", shell.Tune.Editor.SelectionText);
        Assert.Contains(shell.Tune.Editor.SelectionText, KeyLineTexts(window));
    }

    [AvaloniaFact]
    public async Task KeyLineDescribesOneCell()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync();

        shell.Tune.Editor!.Selection = new CellRange(10, 5, 10, 5);

        Assert.Equal("4000 rpm × 165.0 mg/stk: 33.4 °CRK, raw 152 (0x98)", shell.Tune.Editor.SelectionText);
    }

    [AvaloniaFact]
    public async Task KeyLineShowsChangesAndTheUndoLabel()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var editor = shell.Tune.Editor!;
        Assert.Null(editor.ChangedText);

        editor.Selection = new CellRange(0, 0, 0, 1);
        editor.Apply(new EditRequest(EditKind.Step, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("2 cells changed since the last save", editor.ChangedText);
        var texts = KeyLineTexts(window);
        Assert.Contains("2 cells changed since the last save", texts);
        Assert.Contains("Undo: Step 2 cells of ip_iga_ron98_pl__n__maf up", texts);
    }

    // The horizontal bar was drawn over the grid's last row.
    [AvaloniaFact]
    public async Task TheScrollBarDoesNotCoverTheLastRow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        window.Width = 1024;
        window.Height = 900;
        shell.Tune.SetRailWidth(SidePanel.MaxWidth, save: false);
        Dispatcher.UIThread.RunJobs();

        var grid = Find.Named<MS43Logger.AvaloniaApp.Controls.MapGrid>(window, "MapGrid");
        var scroller = grid.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width + 1, "the grid should scroll sideways at 1024 px with a wide rail");
        var bar = scroller.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .Single(scrollBar => scrollBar.TemplatedParent == scroller && scrollBar.Orientation == Avalonia.Layout.Orientation.Horizontal);
        var gridBottom = grid.TranslatePoint(new Avalonia.Point(0, grid.Bounds.Height - grid.BarRoom), window)!.Value.Y;
        Assert.Equal(MS43Logger.AvaloniaApp.Controls.MapGrid.ScrollBarRoom, grid.BarRoom);
        var barTop = bar.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.Y;
        Assert.True(barTop >= gridBottom, $"the bar starts at {barTop}, the grid ends at {gridBottom}");
        Screens.Save(window, "tune-scrollbar-room");
    }

    [AvaloniaFact]
    public async Task InterpolateNeedsARange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var interpolate = Find.Named<Button>(window, "InterpolateTool");

        Assert.False(interpolate.IsEffectivelyEnabled);
        Assert.Equal("Select a range first: Interpolate fills between its corners.", ToolTip.GetTip(interpolate));

        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 3);
        Dispatcher.UIThread.RunJobs();

        Assert.True(interpolate.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task UndoAndRedoSayWhyTheyAreOff()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();

        var undo = Find.Named<Button>(window, "UndoTool");
        var redo = Find.Named<Button>(window, "RedoTool");
        Assert.False(undo.IsEffectivelyEnabled);
        Assert.False(redo.IsEffectivelyEnabled);
        Assert.Equal("Nothing to undo", ToolTip.GetTip(undo));
        Assert.Equal("Nothing to redo", ToolTip.GetTip(redo));
    }

    [AvaloniaFact]
    public async Task EditLimitsShowStorableRangeAndStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync();
        shell.Tune.Editor!.Selection = new CellRange(10, 5, 10, 5);

        var limits = shell.Tune.LimitFacts.ToDictionary(fact => fact.Label);
        Assert.Equal("-23.6 to 72.0 °CRK", limits["Storable"].Text);
        Assert.Equal("0.375 °CRK", limits["Step"].Text);
        Assert.Equal("4000 rpm × 165.0 mg/stk", limits["Active cell"].Text);
        Assert.Equal("33.4 °CRK, raw 152 (0x98)", limits["Active cell"].Detail);
        Assert.False(limits.ContainsKey("XDF range"));
        Assert.DoesNotContain(shell.Tune.DetailFacts, fact => fact.Label is "Raw" or "Storable");
    }

    [AvaloniaFact]
    public async Task RailShowsMarkersForChangedPinnedAndDiffering()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var item = shell.Tune.SelectedItem!;
        shell.Tune.Editor!.Apply(new EditRequest(EditKind.Step, 1));
        shell.Tune.TogglePin(item);
        shell.Tune.CompareWith("stock.bin", new HashSet<XdfItem> { item });
        Dispatcher.UIThread.RunJobs();

        var row = shell.Tune.Rail.OfType<RailItem>().First(entry => entry.Item == item);
        Assert.True(row.IsModified && row.IsPinned && row.IsDifferent);
        var shown = Find.Named<Control>(window, "RailList").GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible).Select(control => control.Name).ToHashSet();
        Assert.Contains("ChangedMark", shown);
        Assert.Contains("PinnedMark", shown);
        Assert.Contains("DiffersMark", shown);
    }

    [AvaloniaFact]
    public async Task PressedCategoryCountRisesToInk2()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // Ink3 on the pressed overlay is 3.79:1 Night and 4.20:1 Day, under 4.5:1: the count rises to Ink2.
        var (window, _) = await TuneScreen.OpenAsync();
        var row = Find.Named<ListBox>(window, "RailList").GetVisualDescendants().OfType<ListBoxItem>().First(item => item.Content is RailCategory);
        var count = row.GetVisualDescendants().OfType<TextBlock>().Last();
        Assert.Equal(Avalonia.Media.Color.Parse("#858C97"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(count.Foreground).Color);

        ((IPseudoClasses)row.Classes).Set(":pressed", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Avalonia.Media.Color.Parse("#AEB4BD"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(count.Foreground).Color);
    }

    [AvaloniaFact]
    public async Task PinButtonSaysPinOrUnpin()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        Assert.Equal("Pin", shell.Tune.PinText);

        Find.Named<Button>(window, "PinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("Unpin", shell.Tune.PinText);
        Assert.True(shell.Tune.Settings.IsPinned("ip_iga_ron98_pl__n__maf"));
    }

    [AvaloniaFact]
    public async Task CompactTuneHidesInspectorUntilDetails()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();

        var inspector = Find.Named<Control>(window, "InspectorHost");
        var details = Find.Named<Button>(window, "DetailsToggle");
        Assert.False(inspector.IsEffectivelyVisible);
        Assert.True(details.IsEffectivelyVisible);
        Assert.Equal(224, Find.Named<Control>(window, "RailHost").Bounds.Width);

        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.Tune.InspectorOpen);
        Assert.True(inspector.IsEffectivelyVisible);
        Assert.Equal(312, inspector.Bounds.Width);
        Screens.Save(window, "restyle-tune-night-1024-details");

        // Details stays reachable over the open overlay, and Esc closes it.
        var centre = details.TranslatePoint(new Avalonia.Point(details.Bounds.Width / 2, details.Bounds.Height / 2), window)!.Value;
        Assert.True(window.InputHitTest(centre) is Avalonia.Visual hit && (hit == details || details.IsVisualAncestorOf(hit)), "the overlay covers Details");
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(shell.Tune.InspectorOpen);
        Assert.False(inspector.IsEffectivelyVisible);
        shell.Tune.InspectorOpen = true;

        window.Width = 1440;
        window.Height = 900;
        Dispatcher.UIThread.RunJobs();

        Assert.False(details.IsEffectivelyVisible);
        Assert.True(inspector.IsEffectivelyVisible);
        Assert.Equal(256, Find.Named<Control>(window, "RailHost").Bounds.Width);
    }

    [AvaloniaFact]
    public async Task SearchWithNoMatchSaysSo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        Assert.Equal("Search items", Find.Named<TextBox>(window, "SearchBox").Watermark);

        shell.Tune.SearchText = "zz_no_such_item";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("No map or value matches \"zz_no_such_item\".", Find.Named<TextBlock>(window, "NoMatchText").Text);
        var clear = Find.Named<Button>(window, "ClearSearchButton");
        Assert.True(clear.IsEffectivelyVisible);

        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(string.Empty, shell.Tune.SearchText);
        Assert.False(Find.Named<TextBlock>(window, "NoMatchText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task TuneScreensForReview()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // The mockup's state: two cells stepped up at 3750 rpm, the map pinned, a 4 × 3 range with 4000 × 165.0 active.
        var (window, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(9, 5, 9, 6);
        editor.Apply(new EditRequest(EditKind.Step, 1));
        shell.Tune.TogglePin(shell.Tune.SelectedItem!);
        editor.Selection = new CellRange(9, 4, 12, 6);
        editor.ActiveCell = (10, 5);
        foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
        {
            if (night)
            {
                shell.IsNight = true;
            }
            else
            {
                shell.IsDay = true;
            }

            foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
            {
                window.Width = width;
                window.Height = height;
                Dispatcher.UIThread.RunJobs();
                Screens.Save(window, $"restyle-tune-{theme}-{width}");
            }
        }

        shell.IsNight = true;
        Assert.Equal("4000 rpm × 165.0 mg/stk", shell.Tune.LimitFacts.Single(fact => fact.Label == "Active cell").Text);
    }

    [AvaloniaFact]
    public async Task OpenMapsRawSettingShowsRaw()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var settings = AppSettings.Load(Path.Combine(_folder, "settings"));
        settings.OpenMapsRaw = true;
        var (window, shell) = TestShell.Create(settings);
        window.Show();
        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("ip_iga_ron98_pl__n__maf");

        Assert.True(shell.Tune.Editor!.ShowRaw);
    }
}
