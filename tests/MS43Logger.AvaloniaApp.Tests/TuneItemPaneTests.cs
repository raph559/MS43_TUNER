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
}
