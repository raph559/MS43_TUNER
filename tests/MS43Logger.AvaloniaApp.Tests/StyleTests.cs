using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

public class StyleTests
{
    private static T Show<T>(T control, ThemeVariant? theme = null) where T : Control
    {
        var window = new Window
        {
            Width = 400,
            Height = 200,
            RequestedThemeVariant = theme ?? ThemeVariant.Dark,
            Content = new StackPanel { Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, Children = { control } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void PrimaryButtonIsInkOnPane()
    {
        var button = Show(new Button { Classes = { "primary" }, Content = "Save" });

        Assert.Equal(Color.Parse("#F3F5F7"), Solid(button.Background));
        Assert.Equal(Color.Parse("#0A0B0D"), Solid(button.Foreground));
    }

    // The Edge outline on every button was too bright at Night: a secondary button is a filled surface with
    // a dim edge; its label names it.
    [AvaloniaTheory]
    [InlineData("Dark", "#1F2328", "#30363D")]
    [InlineData("Light", "#FFFFFF", "#CDD3DB")]
    public void SecondaryButtonIsAFilledSurfaceWithASoftEdge(string theme, string fill, string edge)
    {
        var button = Show(new Button { Content = "Browse…" }, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.Equal(Color.Parse(fill), Solid(button.Background));
        Assert.Equal(Color.Parse(edge), Solid(button.BorderBrush));
        Assert.Equal(new Thickness(1), button.BorderThickness);
    }

    // A picker reads as a button (its label and chevron say what it is), so it takes the button look, not the field edge.
    [AvaloniaFact]
    public void PickerHasTheButtonLook()
    {
        var picker = Show(new ComboBox { ItemsSource = new[] { "COM1" }, SelectedIndex = 0 });

        var surface = picker.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Background");
        Assert.Equal(Color.Parse("#1F2328"), Solid(surface.Background));
        Assert.Equal(Color.Parse("#30363D"), Solid(surface.BorderBrush));
    }

    [AvaloniaFact]
    public void TextFieldKeepsTheEdge()
    {
        var field = Show(new TextBox { Width = 120 });

        Assert.Equal(Color.Parse("#6E7681"), Solid(field.BorderBrush));
    }

    private static ScrollBar HorizontalBar()
    {
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Width = 2000, Height = 2000 } };
        var window = new Window { Width = 400, Height = 300, RequestedThemeVariant = ThemeVariant.Dark, Content = scroller };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return scroller.GetVisualDescendants().OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Horizontal);
    }

    // Fluent's scroll bar was heavy: 16 px, arrow buttons, a square grey thumb over the grid's last row.
    [AvaloniaFact]
    public void ScrollBarIsSlimWithoutArrows()
    {
        var bar = HorizontalBar();

        Assert.Equal(10, bar.Bounds.Height);
        foreach (var name in new[] { "PART_LineUpButton", "PART_LineDownButton" })
        {
            Assert.False(bar.GetVisualDescendants().OfType<RepeatButton>().Single(button => button.Name == name).IsVisible, name);
        }
    }

    [AvaloniaFact]
    public void ScrollBarThumbIsAPillThatSpansTheTrack()
    {
        var bar = HorizontalBar();

        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var track = bar.GetVisualDescendants().OfType<Track>().Single();
        var drawn = thumb.GetVisualDescendants().OfType<Border>().First();
        Assert.Equal(new CornerRadius(5), drawn.CornerRadius);
        Assert.Equal(bar.Bounds.Width, track.Bounds.Width);
    }

    [AvaloniaFact]
    public void SelectedSegmentIsRaisedWithASoftEdge()
    {
        var segment = Show(new ToggleButton { Classes = { "seg" }, Content = "Units", IsChecked = true });

        Assert.Equal(Color.Parse("#2D333B"), Solid(segment.Background));
        Assert.Equal(Color.Parse("#30363D"), Solid(segment.BorderBrush));
        Assert.Equal(Color.Parse("#F3F5F7"), Solid(segment.Foreground));
    }

    [AvaloniaFact]
    public void ToolGroupHasASoftEdge()
    {
        var group = Show(new Border { Classes = { "toolgroup" }, Child = new Button { Content = "Undo" } });

        Assert.Equal(Color.Parse("#30363D"), Solid(group.BorderBrush));
        Assert.Equal(Color.Parse("#1F2328"), Solid(group.Background));
    }

    // A Night dialog card is #1F2328, the button colour: its buttons are raised one step so they still stand out.
    [AvaloniaFact]
    public void ButtonsInADialogAreRaised()
    {
        var button = new Button { Content = "Cancel" };
        Show(new Border { Classes = { "dialog" }, Child = button });

        Assert.Equal(Color.Parse("#2D333B"), Solid(button.Background));
    }

    [AvaloniaFact]
    public void QuietButtonHasNoBorderAndAnInk2Label()
    {
        var button = Show(new Button { Classes = { "quiet" }, Content = "Pin" });

        Assert.Equal(new Thickness(0), button.BorderThickness);
        Assert.Equal(Color.Parse("#AEB4BD"), Solid(button.Foreground));
    }

    [AvaloniaFact]
    public void DisabledSecondaryButtonLosesItsEdgeButKeepsAReadableLabel()
    {
        var button = Show(new Button { Content = "Undo", IsEnabled = false });

        Assert.Equal(Color.Parse("#23272D"), Solid(button.BorderBrush));
        Assert.Equal(Color.Parse("#858C97"), Solid(button.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(":pointerover", null)]
    [InlineData(":pressed", null)]
    [InlineData(":disabled", null)]
    [InlineData(":pointerover", "quiet")]
    [InlineData(":pressed", "primary")]
    public void ButtonStatesUseTheOverlayNotFluentColours(string state, string? kind)
    {
        var button = Show(new Button { Content = "Redo" });
        if (kind is not null)
        {
            button.Classes.Add(kind);
        }

        ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(state, true);
        if (state == ":disabled")
        {
            button.IsEnabled = false;
        }

        Dispatcher.UIThread.RunJobs();

        var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");
        Assert.True(presenter.Background is null || Solid(presenter.Background).A == 0, $"the label's box is filled with {presenter.Background}");
        Assert.Equal(Solid(button.Foreground), Solid(presenter.Foreground));
        Assert.True(NotScaled(button), $"the button is transformed: {button.RenderTransform}");
    }

    [AvaloniaTheory]
    [InlineData(":pointerover")]
    [InlineData(":pressed")]
    [InlineData(":selected")]
    public void ListRowStatesUseTheOverlayNotFluentColours(string state)
    {
        var list = Show(new ListBox { ItemsSource = new[] { "Air Conditioner", "Alternator" }, Width = 200 });
        var row = list.GetVisualDescendants().OfType<ListBoxItem>().First();

        ((Avalonia.Controls.IPseudoClasses)row.Classes).Set(state, true);
        Dispatcher.UIThread.RunJobs();

        var presenter = row.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");
        Assert.True(presenter.Background is null || Solid(presenter.Background).A == 0, $"the row's label box is filled with {presenter.Background}");
        Assert.Equal(Solid(row.Foreground), Solid(presenter.Foreground));
    }

    [AvaloniaFact]
    public void PressedTogglesDoNotShrink()
    {
        var toggle = Show(new ToggleButton { Classes = { "seg" }, Content = "Night" });

        ((Avalonia.Controls.IPseudoClasses)toggle.Classes).Set(":pressed", true);
        Dispatcher.UIThread.RunJobs();

        Assert.True(NotScaled(toggle), $"the toggle is transformed: {toggle.RenderTransform}");
    }

    // Fluent's base is an empty "none" transform; its press state adds a 0.98 scale.
    private static bool NotScaled(Visual visual) =>
        visual.RenderTransform is null || visual.RenderTransform is Avalonia.Media.Transformation.TransformOperations { Operations.Count: 0 };

    [AvaloniaFact]
    public void FocusRingIsInkNeverOrange()
    {
        var button = Show(new Button { Content = "Save" });

        button.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        var ring = AdornerLayer.GetAdornerLayer(button)!.GetVisualDescendants().OfType<Border>()
            .Single(border => border.BorderThickness == new Thickness(2));
        Assert.Equal(Color.Parse("#F3F5F7"), Solid(ring.BorderBrush));
    }

    [AvaloniaTheory]
    [InlineData(false, "#F3F5F7", "#AEB4BD")]
    [InlineData(true, "#121826", "#3D4656")]
    public void FocusedPickerKeepsItsSurfaceAndInk(bool day, string ink1, string ink2)
    {
        var picker = Show(new ComboBox { ItemsSource = new[] { "COM1", "COM3" }, SelectedIndex = 0, MinWidth = 112 }, day ? ThemeVariant.Light : ThemeVariant.Dark);

        picker.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(":focus-visible", picker.Classes);
        var highlight = picker.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "HighlightBackground");
        var content = picker.GetVisualDescendants().OfType<TemplatedControl>().Single(control => control.Name == "ContentPresenter");
        var glyph = picker.GetVisualDescendants().OfType<PathIcon>().Single(icon => icon.Name == "DropDownGlyph");
        Assert.Equal(0, Solid(highlight.Background).A);
        Assert.Equal(Color.Parse(ink1), Solid(content.Foreground));
        Assert.Equal(Color.Parse(ink2), Solid(glyph.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(false, "#AEB4BD", "#23272D")]
    [InlineData(true, "#3D4656", "#E1E5EA")]
    public void LoadingBarIsTwoPixelsOfInk2(bool day, string ink2, string hairline)
    {
        var bar = Show(new ProgressBar { IsIndeterminate = true, Width = 200 }, day ? ThemeVariant.Light : ThemeVariant.Dark);

        Assert.Equal(2, bar.Bounds.Height);
        Assert.Equal(Color.Parse(ink2), Solid(bar.Foreground));
        Assert.Equal(Color.Parse(hairline), Solid(bar.Background));
    }

    [AvaloniaTheory]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Map, "Kind.Map", null)]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Curve, "Kind.Curve", null)]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Axis, "Kind.Axis", null)]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Value, "Kind.Value", null)]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Switch, "Kind.Switch", "Kind.Switch.Fill")]
    [InlineData(MS43Logger.Core.Tuning.ItemKind.Flag, "Kind.Flag", null)]
    public void ItemGlyphIsTheIconOfItsKind(MS43Logger.Core.Tuning.ItemKind kind, string data, string? fill)
    {
        var ink = new SolidColorBrush(Color.Parse("#AEB4BD"));
        var glyph = new ItemGlyph { Kind = kind, Foreground = ink };
        var icon = new StrokeIcon
        {
            Data = (Geometry)Application.Current!.FindResource(data)!,
            FillData = fill is null ? null : (Geometry)Application.Current.FindResource(fill)!,
            Foreground = ink,
        };

        Assert.Equal(Pixels(icon), Pixels(glyph));
    }

    private static byte[] Pixels(Control control)
    {
        var window = new Window { Width = 32, Height = 32, RequestedThemeVariant = ThemeVariant.Dark, Content = control };
        window.Show();
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        window.Close();
        return bytes;
    }

    // The amber banner's stripe and icon are non-text cues: 3:1 on both surfaces a banner sits on.
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void CautionReadsOnFillAndPane(string theme)
    {
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Color Token(string key) => Assert.IsType<Color>(Application.Current!.TryGetResource(key, variant, out var value) ? value : null);

        var caution = Token("CautionColor");
        foreach (var surface in new[] { "FillColor", "PaneColor" })
        {
            var ratio = SettingsTests.Contrast(caution, Token(surface));
            Assert.True(ratio >= 3, $"Caution {caution} is {ratio:0.00}:1 on {surface} ({theme})");
        }
    }

    [AvaloniaFact]
    public void CautionBannerHasACautionStripe()
    {
        var banner = Show(new Border { Classes = { "banner", "caution" }, Child = new TextBlock { Text = "Oil 71 °C: not at working temperature yet (80 °C)." } });

        Assert.Equal(Color.Parse("#E8A33D"), Solid(banner.BorderBrush));
        Assert.Equal(new Thickness(3, 0, 0, 0), banner.BorderThickness);
    }

    [AvaloniaFact]
    public void DisabledFieldsLoseTheirEdgeButKeepReadableText()
    {
        var field = Show(new TextBox { Text = "60", IsEnabled = false });
        var picker = Show(new ComboBox { ItemsSource = new[] { "COM1" }, SelectedIndex = 0, IsEnabled = false });

        var fieldBorder = field.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
        Assert.Equal(Color.Parse("#23272D"), Solid(fieldBorder.BorderBrush));
        Assert.Equal(Color.Parse("#0A0B0D"), Solid(fieldBorder.Background));
        Assert.Equal(Color.Parse("#858C97"), Solid(field.Foreground));

        var pickerBorder = picker.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Background");
        var content = picker.GetVisualDescendants().OfType<TemplatedControl>().Single(control => control.Name == "ContentPresenter");
        Assert.Equal(Color.Parse("#23272D"), Solid(pickerBorder.BorderBrush));
        Assert.Equal(Color.Parse("#0A0B0D"), Solid(pickerBorder.Background));
        Assert.Equal(Color.Parse("#858C97"), Solid(content.Foreground));
    }

    [AvaloniaFact]
    public void SwitchOffTrackHasAnEdge()
    {
        var toggle = Show(new ToggleSwitch());

        var track = toggle.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "OuterBorder");
        var knob = toggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(ellipse => ellipse.Name == "SwitchKnobOff");
        Assert.Equal(Color.Parse("#6E7681"), Solid(track.BorderBrush));
        Assert.Equal(Color.Parse("#0A0B0D"), Solid(track.Background));
        Assert.Equal(Color.Parse("#858C97"), Solid(knob.Fill));
        Assert.Equal(36, track.Bounds.Width);
        Assert.Equal(14, knob.Bounds.Width);
    }

    [AvaloniaFact]
    public void SwitchOnKnobSitsInsideTheTrack()
    {
        var toggle = Show(new ToggleSwitch { IsChecked = true });

        var track = toggle.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "SwitchKnobBounds");
        var knob = toggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(ellipse => ellipse.Name == "SwitchKnobOn");
        var trackLeft = track.TranslatePoint(default, toggle)!.Value.X;
        var knobLeft = knob.TranslatePoint(default, toggle)!.Value.X;

        // 3 px from the track's right end, as the off knob sits 3 px from its left end.
        Assert.Equal(trackLeft + 36 - 3 - 14, knobLeft, 1);
    }

    [AvaloniaFact]
    public void EveryIconKeyResolves()
    {
        var keys = new[]
        {
            "Icon.Search", "Icon.Clear", "Icon.Pin", "Icon.PinFilled", "Icon.ChevronDown", "Icon.ChevronRight", "Icon.Rescan",
            "Icon.Warn", "Icon.Info", "Icon.Check", "Icon.Gear", "Icon.Close", "Icon.Minus", "Icon.Plus",
            "Kind.Map", "Kind.Curve", "Kind.Value", "Kind.Switch", "Kind.Flag", "Kind.Axis", "Mark.Changed", "Mark.Differs",
        };

        Assert.Empty(keys.Where(key => Application.Current!.FindResource(key) is not Geometry));
    }

    [AvaloniaFact]
    public void StrokeIconUsesFlatCapsAndMiterJoins()
    {
        var pen = new StrokeIcon().CreatePen(Brushes.Black);

        Assert.Equal(1.5, pen.Thickness);
        Assert.Equal(PenLineCap.Flat, pen.LineCap);
        Assert.Equal(PenLineJoin.Miter, pen.LineJoin);
    }

    [AvaloniaFact]
    public void DataTextUsesTabularFigures()
    {
        var ones = new TextBlock { Classes = { "data16" }, Text = "1111" };
        var zeros = new TextBlock { Classes = { "data16" }, Text = "0000" };
        Show(new StackPanel { Children = { ones, zeros } });

        // Bahnschrift's default figures are proportional ("1" is 681 units, "0" is 1130): only +tnum makes these equal.
        if (OperatingSystem.IsWindows())
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(ones.FontFamily), out var face));
            Assert.Equal("Bahnschrift", face.FamilyName);
        }

        // DesiredSize, not Bounds: the StackPanel stretches both blocks to its own width.
        Assert.Equal(zeros.DesiredSize.Width, ones.DesiredSize.Width, 1);
    }

    [AvaloniaFact]
    public void NumberRunsUseTabularFigures()
    {
        // In UI text that sets no figures of its own, as in the Settings segments ("10 s").
        var run = new Avalonia.Controls.Documents.Run("1111") { Classes = { "num" } };
        var ones = new TextBlock { Inlines = { run } };
        var zeros = new TextBlock { Inlines = { new Avalonia.Controls.Documents.Run("0000") { Classes = { "num" } } } };
        Show(new StackPanel { Children = { ones, zeros } });

        if (OperatingSystem.IsWindows())
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(run.FontFamily), out var face));
            Assert.Equal("Bahnschrift", face.FamilyName);
        }

        // DesiredSize, not Bounds: the StackPanel stretches both blocks to its own width.
        Assert.Equal(zeros.DesiredSize.Width, ones.DesiredSize.Width, 1);
    }
}
