using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Interpolate and Smooth along X, Y or both, as in TunerPro.</summary>
public sealed class EditAxisTests
{
    [AvaloniaFact]
    public async Task AMapOffersXYAndXYWithXYChosen()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();

        Assert.True(Find.Named<Border>(window, "EditAxisSeg").IsEffectivelyVisible);
        Assert.Equal("X", Find.Named<ToggleButton>(window, "EditAxisX").Content);
        Assert.Equal("Y", Find.Named<ToggleButton>(window, "EditAxisY").Content);
        Assert.Equal("XY", Find.Named<ToggleButton>(window, "EditAxisXY").Content);
        Assert.True(Find.Named<ToggleButton>(window, "EditAxisXY").IsChecked);
    }

    [AvaloniaFact]
    public async Task InterpolateXFillsEachRowAndSaysSo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;
        var editor = tune.Editor!;
        var range = new CellRange(3, 1, 6, 5);
        var expected = TableEdits.Interpolate(tune.Document!, editor.Table, range, EditAxis.X).Changes;

        Find.Named<ToggleButton>(window, "EditAxisX").IsChecked = true;
        editor.Selection = range;
        Dispatcher.UIThread.RunJobs();
        Assert.True(tune.RunTool(EditKind.Interpolate));

        Assert.Equal(EditAxis.X, tune.EditAxis);
        Assert.Equal(expected.Select(change => (change.Row, change.Col, change.NewRaw)),
            editor.ChangedCells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Col).Select(cell => (cell.Row, cell.Col, editor.Values.Raw[cell.Row, cell.Col])));
        Assert.Equal($"Undo: Interpolate {expected.Count} cells of ip_iga_ron98_pl__n__maf along X", tune.UndoTip);
    }

    [AvaloniaFact]
    public async Task InterpolateYNeedsMoreThanOneRow()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var interpolate = Find.Named<Button>(window, "InterpolateTool");

        shell.Tune.EditAxis = EditAxis.Y;
        shell.Tune.Editor!.Selection = new CellRange(2, 0, 2, 6);
        Dispatcher.UIThread.RunJobs();

        Assert.False(interpolate.IsEffectivelyEnabled);
        Assert.Equal("Select more than one row: Interpolate Y fills each column between its top and bottom cell.", ToolTip.GetTip(interpolate));

        shell.Tune.Editor.Selection = new CellRange(2, 0, 5, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.True(interpolate.IsEffectivelyEnabled);
        Assert.Equal("Fill each column between its top and bottom cell", ToolTip.GetTip(interpolate));
    }

    [AvaloniaFact]
    public async Task SmoothYSaysWhatItAverages()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        var smooth = Find.Named<Button>(window, "SmoothTool");

        shell.Tune.EditAxis = EditAxis.Y;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Average each cell with the cells above and below it", ToolTip.GetTip(smooth));
        shell.Tune.Editor!.Selection = new CellRange(5, 5, 8, 8);
        Assert.True(shell.Tune.RunTool(EditKind.Smooth));
        Assert.EndsWith(" along Y", shell.Tune.UndoTip);
    }

    [AvaloniaFact]
    public async Task TheChoiceIsKeptForTheNextMapAndTheNextStart()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync();
        var tune = shell.Tune;

        tune.EditAxis = EditAxis.Y;
        tune.SelectedItem = tune.Document!.Xdf.FindByTitle("ip_iga_ron91_pl__n__maf");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(EditAxis.Y, tune.Editor!.Axis);
        Assert.Equal(EditAxis.Y, tune.Settings.EditAxis);
        Assert.Equal(EditAxis.Y, AppSettings.Load(tune.Settings.Folder).EditAxis);
    }

    // A curve has one row or one column: the choice means nothing there, so it is hidden and the tools work along the curve.
    [AvaloniaFact]
    public async Task ACurveHidesTheChoiceAndWorksAlongTheCurve()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync("id_n_max_mt__gear");
        var editor = shell.Tune.Editor!;
        shell.Tune.EditAxis = EditAxis.Y;
        var line = editor.Values.Rows == 1 ? new CellRange(0, 0, 0, editor.Values.Cols - 1) : new CellRange(0, 0, editor.Values.Rows - 1, 0);
        editor.Selection = line;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Find.Named<Border>(window, "EditAxisSeg").IsEffectivelyVisible);
        Assert.True(Find.Named<Button>(window, "InterpolateTool").IsEffectivelyEnabled);
        Assert.Equal("Fill between the corners of the selection", ToolTip.GetTip(Find.Named<Button>(window, "InterpolateTool")));
    }
}
