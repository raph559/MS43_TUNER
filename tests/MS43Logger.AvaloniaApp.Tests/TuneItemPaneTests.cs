using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TuneItemPaneTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-items-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<(MainWindow Window, ShellViewModel Shell, ValueEditorViewModel Value)> Open(string item)
    {
        var (window, shell) = await TuneScreen.OpenAsync(item, binPath: TuneScreen.CopyCar(_folder));
        Dispatcher.UIThread.RunJobs();
        return (window, shell, shell.Tune.Value!);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, string name)
    {
        Find.Named<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Color PixelAt(TopLevel window, Point point)
    {
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var pixel = Marshal.ReadInt32(buffer.Address, (int)point.Y * buffer.RowBytes + (int)point.X * 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }

    private static bool Near(Color expected, Color actual, int tolerance = 24) =>
        Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;

    // Where the slider draws a physical value, in window coordinates, on the track's centre line.
    private static Point SliderPoint(Window window, ValueSlider slider, double physical) =>
        slider.TranslatePoint(new Point((physical - slider.Minimum) / (slider.Maximum - slider.Minimum) * slider.Bounds.Width, slider.ThumbCentre.Y), window)!.Value;

    [AvaloniaFact]
    public async Task SliderSpansStorableRange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, value) = await Open("c_lc_n_max");

        Assert.Equal((0, 8160), (value.SpanLow, value.SpanHigh));
        Assert.Null(value.LimitsLegend);
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        Assert.Equal((0, 8160, 4512), (slider.Minimum, slider.Maximum, slider.Value));
        Screens.Save(window, "restyle-tune-night-1440-value");
    }

    [AvaloniaFact]
    public async Task SliderSpansXdfRangeWhenNarrower()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, value) = await Open("c_n_max_er");

        Assert.Equal((480, 8160), (value.SpanLow, value.SpanHigh));
        Assert.Equal("Ticks: the XDF range, 480 to 8160 rpm. Typed values from 0 to 65535 rpm are stored, with a warning outside the ticks.", value.LimitsLegend);
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        Assert.Equal((480.0, 8160.0), (slider.TickLow, slider.TickHigh));
        Screens.Save(window, "restyle-tune-night-1440-range");
    }

    [AvaloniaFact]
    public async Task ValueOutsideXdfRangeWidensSpan()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, _, value) = await Open("t_soi_as");
        var physical = value.Current.Physical;

        Assert.True(physical < (value.Limits.XdfLow ?? double.NegativeInfinity) || physical > (value.Limits.XdfHigh ?? double.PositiveInfinity),
            "t_soi_as should sit outside its XDF range");
        Assert.InRange(physical, value.SpanLow, value.SpanHigh);
    }

    [AvaloniaFact]
    public async Task ArrowKeyIsOneUndoStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        Assert.Equal(141, value.RawValue);
        Find.Named<ValueSlider>(window, "ValueSlider").Focus();

        Press(window, PhysicalKey.ArrowRight);

        Assert.Equal(142, value.RawValue);
        shell.Tune.Undo();
        Assert.Equal(141, value.RawValue);
    }

    [AvaloniaFact]
    public async Task StepButtonsAreOneUndoStepEach()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");

        Click(window, "StepUpButton");
        Click(window, "StepDownButton");

        Assert.Equal(141, value.RawValue);
        Assert.Equal(2, shell.Tune.History!.UndoCount);
        shell.Tune.Undo();
        Assert.Equal(142, value.RawValue);
        shell.Tune.Undo();
        Assert.Equal(141, value.RawValue);
    }

    [AvaloniaFact]
    public async Task PageUpMovesTenSteps()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, value) = await Open("c_lc_n_max");
        Find.Named<ValueSlider>(window, "ValueSlider").Focus();

        Press(window, PhysicalKey.PageUp);

        Assert.Equal(151, value.RawValue);
    }

    [AvaloniaFact]
    public async Task HomeAndEndGoToSpanEnds()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, value) = await Open("c_lc_n_max");
        Find.Named<ValueSlider>(window, "ValueSlider").Focus();

        Press(window, PhysicalKey.Home);
        Assert.Equal(0, value.Current.Physical);

        Press(window, PhysicalKey.End);
        Assert.Equal(8160, value.Current.Physical);
    }

    [AvaloniaFact]
    public async Task DragCommitsOnceAndEscCancels()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, value) = await Open("c_lc_n_max");
        var undo = shell.Tune.History!.UndoCount;

        value.BeginDrag();
        value.DragTo(4003);

        Assert.Equal(4000, value.PendingValue);
        Assert.Equal("Release to set 4000 rpm. Esc cancels.", value.DragText);
        Assert.Equal(141, value.RawValue);

        Assert.True(value.CommitDrag());
        Assert.Equal(125, value.RawValue);
        Assert.Equal(undo + 1, shell.Tune.History.UndoCount);
        Assert.Null(value.PendingValue);

        value.BeginDrag();
        value.DragTo(6000);
        value.CancelDrag();

        Assert.Equal(125, value.RawValue);
        Assert.Null(value.DragText);
        Assert.Equal(undo + 1, shell.Tune.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task StepButtonsShowTheStep()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, value) = await Open("c_lc_n_max");

        Assert.Equal(("-32 rpm", "+32 rpm"), (value.StepDownTip, value.StepUpTip));
        Assert.Equal("-32 rpm", ToolTip.GetTip(Find.Named<Button>(window, "StepDownButton")));
        Assert.Equal("+32 rpm", ToolTip.GetTip(Find.Named<Button>(window, "StepUpButton")));
        Assert.Equal("One step down", Avalonia.Automation.AutomationProperties.GetName(Find.Named<Button>(window, "StepDownButton")));
    }

    [AvaloniaFact]
    public async Task TabOrderRunsValueSetUndoRedoMinusThumbPlus()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // Two edits and one undo leave both Undo and Redo enabled, so every stop is in the order.
        var (window, shell, value) = await Open("c_lc_n_max");
        value.StepBy(1);
        value.StepBy(1);
        shell.Tune.Undo();
        Dispatcher.UIThread.RunJobs();
        Find.Named<TextBox>(window, "ValueInput").Focus();

        var order = new List<string?>();
        for (var i = 0; i < 6; i++)
        {
            Press(window, PhysicalKey.Tab);
            order.Add((window.FocusManager!.GetFocusedElement() as Control)?.Name);
        }

        Assert.Equal(new[] { "SetValueButton", "UndoTool", "RedoTool", "StepDownButton", "ValueSlider", "StepUpButton" }, order);
    }

    [AvaloniaFact]
    public async Task ValueFieldIsNeverPrefilled()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // System 5.3: the Value field is never prefilled; the reading above the slider shows the value.
        var (window, _, value) = await Open("c_lc_n_max");
        Assert.Equal(string.Empty, value.Input);
        Assert.True(string.IsNullOrEmpty(Find.Named<TextBox>(window, "ValueInput").Text));

        value.Input = "4000";
        Assert.True(value.Apply());

        Assert.Equal(125, value.RawValue);
        Assert.Equal(string.Empty, value.Input);
    }

    [AvaloniaFact]
    public async Task DragShowsThePendingValueInTheReading()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, _, value) = await Open("c_lc_n_max");
        Assert.Equal("4512", value.ReadingText);

        value.BeginDrag();
        value.DragTo(4003);
        Assert.Equal("4000", value.ReadingText);

        value.CancelDrag();
        Assert.Equal("4512", value.ReadingText);
    }

    [AvaloniaFact]
    public async Task SliderShowsTheFocusRingFromTheKeyboard()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, _) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var ink1 = ((ISolidColorBrush)ThemeBrushes.Get(slider, "Ink1Brush")).Color;
        var ring = slider.TranslatePoint(slider.ThumbCentre - new Point(0, 13), window)!.Value;
        Find.Named<Button>(window, "StepDownButton").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Near(ink1, PixelAt(window, ring)), "no ring before the slider has focus");

        Press(window, PhysicalKey.Tab);

        Assert.Same(slider, window.FocusManager!.GetFocusedElement());
        Assert.True(Near(ink1, PixelAt(window, ring)), "the ring of system 1.3 round the thumb");
    }

    [AvaloniaFact]
    public async Task DraggingTheThumbWritesOnRelease()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var undo = shell.Tune.History!.UndoCount;

        window.MouseDown(SliderPoint(window, slider, 4512), MouseButton.Left);
        window.MouseMove(SliderPoint(window, slider, 4000));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4000, value.PendingValue);
        Assert.Equal(141, value.RawValue);

        window.MouseUp(SliderPoint(window, slider, 4000), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(125, value.RawValue);
        Assert.Null(value.PendingValue);
        Assert.Equal(undo + 1, shell.Tune.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task EscCancelsADrag()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var undo = shell.Tune.History!.UndoCount;

        window.MouseDown(SliderPoint(window, slider, 4512), MouseButton.Left);
        window.MouseMove(SliderPoint(window, slider, 6000));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(value.PendingValue);

        Press(window, PhysicalKey.Escape);
        Assert.Null(value.PendingValue);
        Assert.Null(value.DragText);

        window.MouseUp(SliderPoint(window, slider, 6000), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(141, value.RawValue);
        Assert.Equal(undo, shell.Tune.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task SwitchLimitsCountItsChoices()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await Open("c_conf_load");

        Assert.Equal(new[] { "Choices", "XDF range", "Value" }, shell.Tune.LimitFacts.Select(fact => fact.Label));
        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "Choices", Text: "3, from the XDF description" });
        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "XDF range", Text: "up to 4" });
        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "Value", Text: "Maf sensor", Detail: "raw 1 (0x1)" });
        Assert.Contains(shell.Tune.DetailFacts, fact => fact is { Label: "Scaling", Text: "1.0*X", TextIsId: true });
    }

    [AvaloniaFact]
    public async Task SwitchValueFactFollowsUndo()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // The facts rebuild when the value changes, so the chosen label must already match the new raw value then.
        var (_, shell, value) = await Open("c_conf_load");
        value.SelectedChoice = value.Choices!.Single(choice => choice.Label == "Alpha-N");
        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "Value", Text: "Alpha-N", Detail: "raw 2 (0x2)" });

        shell.Tune.Undo();

        Assert.Contains(shell.Tune.LimitFacts, fact => fact is { Label: "Value", Text: "Maf sensor", Detail: "raw 1 (0x1)" });
    }

    [AvaloniaFact]
    public async Task FlagDetailsShowSizeAndMask()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell, _) = await Open("lv_mil_ind_knk_lv_1");

        Assert.Equal(new[] { "Address", "Size", "Bit mask" }, shell.Tune.DetailFacts.Select(fact => fact.Label));
        Assert.Contains(shell.Tune.DetailFacts, fact => fact is { Label: "Size", Text: "16 bit" });
        Assert.Contains(shell.Tune.DetailFacts, fact => fact is { Label: "Bit mask", Text: "0x01" });
    }

    [AvaloniaFact]
    public async Task AxisCountsItsBreakpoints()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync("ldpm_n_14");
        Dispatcher.UIThread.RunJobs();
        var tune = shell.Tune;

        Assert.Equal("Breakpoints", tune.ShapeTitle);
        Assert.Contains(tune.LimitFacts, fact => fact is { Label: "Active cell", Text: "Breakpoint 1", Detail: "800 rpm, raw 800 (0x320)" });
        Assert.Equal("Breakpoint 1: 800 rpm, raw 800 (0x320)", tune.Editor!.SelectionText);
        Assert.Equal(new[] { "Address", "Size", "Units", "Scaling" }, tune.DetailFacts.Select(fact => fact.Label));
        Assert.Contains(tune.DetailFacts, fact => fact is { Label: "Scaling", Text: "1.0*X", TextIsId: true });
        var chart = Find.Named<CurveChart>(window, "Curve");
        Assert.Equal(new[] { "1", "5", "10", "15", "20" }, chart.XTickLabels);
        Assert.Equal("breakpoint", chart.XAxisUnits);
    }

    [AvaloniaFact]
    public async Task ItemPanesFitBothThemesAndWidths()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await Open("c_lc_n_max");
        foreach (var item in new[] { "c_lc_n_max", "c_n_max_er", "c_conf_load", "lv_mil_ind_knk_lv_1", "ip_iga_tco_st__tco", "ldpm_n_14" })
        {
            shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle(item);
            foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
            {
                shell.IsNight = night;
                shell.IsDay = !night;
                foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    var pane = Find.Named<Border>(window, "PaneHost");
                    var area = Find.Named<StackPanel>(window, "ValueArea");
                    if (area.IsEffectivelyVisible)
                    {
                        var right = area.TranslatePoint(new Point(area.Bounds.Width, 0), pane)!.Value.X;
                        Assert.True(right <= pane.Bounds.Width - pane.Padding.Right + 0.5, $"{item} at {width}: the item ends at {right}, past the pane");
                    }

                    Screens.Save(window, $"restyle-tune-items-{item}-{theme}-{width}");
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task SwitchListSelectsAndUndoes()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_conf_load");
        var list = Find.Named<ListBox>(window, "ChoiceList");
        Assert.Equal("Maf sensor", ((ChoiceItem)list.SelectedItem!).Label);

        list.SelectedItem = value.Choices!.Single(choice => choice.Label == "Alpha-N");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, value.RawValue);
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);
        shell.Tune.Undo();
        Assert.Equal(1, value.RawValue);
        Screens.Save(window, "restyle-tune-night-1440-switch");
    }

    [AvaloniaFact]
    public async Task FlagSwitchSaysOnOrOff()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("lv_mil_ind_knk_lv_1");
        var flag = Find.Named<ToggleSwitch>(window, "FlagSwitch");
        Assert.Equal(("On", "Off"), (flag.OnContent as string, flag.OffContent as string));
        Assert.False(flag.IsChecked);

        flag.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(value.IsOn);
        Assert.True(shell.Tune.Document!.Reader.ReadFlag((XdfFlag)shell.Tune.SelectedItem!));
        Screens.Save(window, "restyle-tune-night-1440-flag");
    }

    [AvaloniaFact]
    public async Task AxisPaneShowsRuleAndUsers()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync("ldpm_n_14");
        Dispatcher.UIThread.RunJobs();

        var rule = Find.Named<TextBlock>(window, "AxisRuleText");
        Assert.True(rule.IsEffectivelyVisible);
        Assert.Equal("Breakpoints must not go down: an edit that makes one fall below its neighbour is refused.", rule.Text);
        var users = Find.Named<ItemsControl>(window, "UsedByList").Items.OfType<XdfItem>().Select(item => item.Title).ToList();
        Assert.Contains("ip_iga_ron91_pl__n__maf", users);
        Assert.Contains("ip_iga_ron98_pl__n__maf", users);
        Assert.Contains("ip_iga_e85_pl__n__maf", users);
        Screens.Save(window, "restyle-tune-night-1440-axis");
    }

    // System 5.3: number fields set typed digits in Bahnschrift 13; the placeholder and the unit stay UI text.
    [AvaloniaFact]
    public async Task ValueFieldsTypeInBahnschrift()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        void Check(TextBox field)
        {
            var typed = field.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single(part => part.Name == "PART_TextPresenter");
            var placeholder = field.GetVisualDescendants().OfType<TextBlock>().Single(part => part.Name == "PART_Watermark");
            Assert.Equal(window.FindResource("DataFont"), typed.FontFamily);
            Assert.Equal(13, typed.FontSize);
            Assert.Equal(window.FindResource("UiFont"), placeholder.FontFamily);
            if (field.InnerRightContent is Control suffix)
            {
                Assert.All(suffix.GetSelfAndVisualDescendants().OfType<TextBlock>(), unit => Assert.Equal(window.FindResource("UiFont"), unit.FontFamily));
            }
        }

        Check(Find.Named<TextBox>(window, "ToolValue"));
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("c_lc_n_max");
        Dispatcher.UIThread.RunJobs();
        Check(Find.Named<TextBox>(window, "ValueInput"));
    }

    // A press on the thumb only takes hold of it: off its centre it must not move the value (final review, Critical 1).
    [AvaloniaFact]
    public async Task PressingTheThumbWritesNothing()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var undo = shell.Tune.History!.UndoCount;
        var edge = slider.TranslatePoint(slider.ThumbCentre + new Point(8, 0), window)!.Value;

        window.MouseDown(edge, MouseButton.Left);
        window.MouseUp(edge, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(141, value.RawValue);
        Assert.Null(value.PendingValue);
        Assert.Equal(undo, shell.Tune.History.UndoCount);
        Assert.True(slider.IsFocused);
    }

    [AvaloniaFact]
    public async Task DraggingMovesTheValueByWhatThePointerMoved()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var grab = new Point(8, 0);

        window.MouseDown(slider.TranslatePoint(slider.ThumbCentre + grab, window)!.Value, MouseButton.Left);
        window.MouseMove(SliderPoint(window, slider, 4000) + grab);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4000, value.PendingValue);

        window.MouseUp(SliderPoint(window, slider, 4000) + grab, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(125, value.RawValue);
    }

    // A press on the track beside the thumb moves 10 steps toward the press, as Page Up and Page Down do.
    [AvaloniaFact]
    public async Task PressingTheTrackPagesTowardThePress()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, value) = await Open("c_lc_n_max");
        var slider = Find.Named<ValueSlider>(window, "ValueSlider");
        var undo = shell.Tune.History!.UndoCount;

        var right = SliderPoint(window, slider, 7000);
        window.MouseDown(right, MouseButton.Left);
        window.MouseUp(right, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(151, value.RawValue);
        Assert.Equal(undo + 1, shell.Tune.History.UndoCount);

        var left = SliderPoint(window, slider, 1000);
        window.MouseDown(left, MouseButton.Left);
        window.MouseUp(left, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(141, value.RawValue);
        Assert.Equal(undo + 2, shell.Tune.History.UndoCount);
    }
}
