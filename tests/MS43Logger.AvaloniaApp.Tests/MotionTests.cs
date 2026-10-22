using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The motion layer: loaded only while Windows' animation effects are on. The rest of the
/// test run has it off (TestAppBuilder), so renders and colour checks see finished states.
/// </summary>
// In the app the motion layer is app-wide (App.ApplyMotion). Here it goes into each test window's own styles, so the
// other tests' windows, rendered on the same UI thread, never catch a fade half-way.
public sealed class MotionTests
{

    // Avalonia's animation clocks are internal, so these tests run on real time: the headless render timer ticks with
    // the machine clock. A check made before a fade's length has passed must still see it moving; on a busy run the
    // first look can come later, and then only the finished state is checked.
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(180);

    private static (MainWindow Window, ShellViewModel Shell) Show(bool motion = true)
    {
        var (window, shell) = TestShell.Create();
        if (motion)
        {
            window.Styles.Add(App.NewMotionLayer());
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Settle();
        return (window, shell);
    }

    private static void Tick()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(10);
            Tick();
        }
    }

    private static void AssertMoving(double opacity, System.Diagnostics.Stopwatch since)
    {
        if (since.Elapsed < Fade)
        {
            Assert.True(opacity < 1, $"opacity {opacity} after {since.ElapsedMilliseconds} ms");
        }
    }

    private static Border Part(Visual control, string name) =>
        control.GetVisualDescendants().OfType<Border>().First(border => border.Name == name);

    [AvaloniaFact]
    public void MotionLayerFollowsTheSwitch()
    {
        // Synchronous from on to off: no other test runs on the UI thread in between.
        Assert.False(App.MotionIsOn);
        App.ApplyMotion(true);
        App.ApplyMotion(true);
        Assert.True(App.MotionIsOn);
        Assert.Single(Application.Current!.Styles.OfType<Avalonia.Markup.Xaml.Styling.StyleInclude>(),
            include => include.Source?.ToString().EndsWith("Motion.axaml", StringComparison.Ordinal) == true);
        App.ApplyMotion(false);
        Assert.False(App.MotionIsOn);
    }

    [AvaloniaFact]
    public void HoverOverlaysFade()
    {
        var (window, _) = Show();

        var overlay = Part(Find.Named<Button>(window, "SettingsButton"), "PART_Overlay");
        Assert.Contains(overlay.Transitions!, transition => transition is BrushTransition { Property.Name: "Background" });
        var tab = Part(Find.Named<ToggleButton>(window, "TabTune"), "PART_Overlay");
        Assert.Contains(tab.Transitions!, transition => transition is BrushTransition { Property.Name: "Background" });
        Assert.Contains(Find.Named<Button>(window, "CloseButton").Transitions!, transition => transition is BrushTransition { Property.Name: "Background" });
    }

    [AvaloniaFact]
    public void ANewScreenFadesIn()
    {
        var (window, shell) = Show();
        var tune = window.GetVisualDescendants().OfType<TuneView>().Single();

        var since = System.Diagnostics.Stopwatch.StartNew();
        shell.SelectedTab = ShellViewModel.TuneTab;
        Tick();
        AssertMoving(tune.Opacity, since);

        Settle();
        Assert.Equal(1, tune.Opacity);

        var performance = window.GetVisualDescendants().OfType<PerformanceView>().Single();
        since = System.Diagnostics.Stopwatch.StartNew();
        shell.SelectedTab = ShellViewModel.PerformanceTab;
        Tick();
        AssertMoving(performance.Opacity, since);

        Settle();
        Assert.Equal(1, performance.Opacity);
    }

    [AvaloniaFact]
    public void TheTabUnderlineGrowsIn()
    {
        var (window, _) = Show();
        var underline = Part(Find.Named<ToggleButton>(window, "TabTune"), "Indicator");

        Assert.Contains(underline.Transitions!, transition => transition is TransformOperationsTransition);
    }

    [AvaloniaFact]
    public void DialogsFadeIn()
    {
        var (window, _) = Show();
        var dialogs = Find.Named<Shell.DialogHost>(window, "Dialogs");

        var since = System.Diagnostics.Stopwatch.StartNew();
        window.KeyPress(Avalonia.Input.Key.OemComma, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Comma, ",");
        Tick();
        Assert.True(dialogs.IsOpen);
        AssertMoving(dialogs.Opacity, since);

        Settle();
        Assert.Equal(1, dialogs.Opacity);
    }

    private static (Window Window, ToggleButton A, ToggleButton B, Border Seg) Segments(bool motion)
    {
        var a = new ToggleButton { Classes = { "seg" }, Content = "Manual", IsChecked = true };
        var b = new ToggleButton { Classes = { "seg" }, Content = "Automatic" };
        var seg = new Border { Classes = { "seg" }, Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2, Children = { a, b } } };
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Margin = new Thickness(12), Children = { seg } } };
        if (motion)
        {
            window.Styles.Add(App.NewMotionLayer());
        }

        window.Show();
        Settle();
        return (window, a, b, seg);
    }

    private static Border Pill(Border seg) => seg.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("segpill"));

    private static double Left(Visual control, Visual seg) => control.TranslatePoint(default, seg)!.Value.X;

    // Picking a segment (Manual, Automatic) was not animated. The raised pill now slides to the new choice.
    [AvaloniaFact]
    public void TheSelectedSegmentSlides()
    {
        var (_, a, b, seg) = Segments(motion: true);
        var pill = Pill(seg);
        Assert.Equal(Left(a, seg), Left(pill, seg), 1);
        Assert.Equal(a.Bounds.Width, pill.Bounds.Width, 1);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(a.Background).Color);

        var since = System.Diagnostics.Stopwatch.StartNew();
        a.IsChecked = false;
        b.IsChecked = true;
        Tick();
        if (since.Elapsed < Fade)
        {
            Assert.True(Left(pill, seg) < Left(b, seg) - 1, $"the pill jumped: {Left(pill, seg)} at {since.ElapsedMilliseconds} ms");
        }

        Settle();
        Assert.Equal(Left(b, seg), Left(pill, seg), 1);
        Assert.Equal(b.Bounds.Width, pill.Bounds.Width, 1);
    }

    [AvaloniaFact]
    public void WithMotionOffSegmentsFillAtOnce()
    {
        var (_, a, _, seg) = Segments(motion: false);

        Assert.Empty(seg.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("segpill")));
        Assert.NotEqual(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(a.Background).Color);
    }

    private static (ToggleSwitch Switch, Border Fill) Switch(bool motion)
    {
        var toggle = new ToggleSwitch { Content = "Changed only" };
        var window = new Window { Width = 300, Height = 120, Content = toggle };
        if (motion)
        {
            window.Styles.Add(App.NewMotionLayer());
        }

        window.Show();
        Settle();
        return (toggle, Part(toggle, "SwitchKnobBounds"));
    }

    // Switches (Changed only, Differs from B) were not animated. The 16 px knob slide was lost behind the
    // track turning from outline to fill at once; the fill now fades in with the slide.
    [AvaloniaFact]
    public void ASwitchFillsAsItsKnobSlides()
    {
        var (toggle, fill) = Switch(motion: true);
        Assert.Equal(0, fill.Opacity);

        var since = System.Diagnostics.Stopwatch.StartNew();
        toggle.IsChecked = true;
        Tick();
        AssertMoving(fill.Opacity, since);

        Settle();
        Assert.Equal(1, fill.Opacity);
    }

    [AvaloniaFact]
    public void WithMotionOffASwitchFillsAtOnce()
    {
        var (toggle, fill) = Switch(motion: false);

        toggle.IsChecked = true;
        Tick();

        Assert.Equal(1, fill.Opacity);
    }

    [AvaloniaFact]
    public void WithMotionOffNothingMoves()
    {
        var (window, shell) = Show(motion: false);
        var tune = window.GetVisualDescendants().OfType<TuneView>().Single();

        shell.SelectedTab = ShellViewModel.TuneTab;
        Tick();

        Assert.Equal(1, tune.Opacity);
        Assert.Null(Part(Find.Named<Button>(window, "SettingsButton"), "PART_Overlay").Transitions);
    }

    [AvaloniaFact]
    public void TheUnderlineShowsOnlyOnTheSelectedTabEitherWay()
    {
        var (window, _) = Show(motion: false);
        double ScaleY(string tab) =>
            Part(Find.Named<ToggleButton>(window, tab), "Indicator").RenderTransform is { } transform ? transform.Value.M22 : 1;

        Assert.Equal(1, ScaleY("TabLive"));
        Assert.Equal(0, ScaleY("TabTune"));
    }
}
