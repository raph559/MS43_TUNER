using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The restyle's delivery gate (plan Task 15): checks that run over the sources and over every screen.</summary>
public sealed class GateTests
{
    private const string App = "src/MS43Logger.AvaloniaApp";

    // The keys Task 3 kept so the views could build until they were rebuilt.
    private static readonly string[] LegacyKeys =
    [
        "AppBackgroundBrush", "DialBrush", "SunkenBrush", "RaisedBrush", "FaceBrush", "SurfaceBackgroundBrush", "InputBackgroundBrush",
        "NeutralButtonBackgroundBrush", "BezelBrush", "SurfaceBorderBrush", "Bezel2Brush", "InkBrush", "PrimaryTextBrush",
        "NeutralButtonForegroundBrush", "ConnectButtonBackgroundBrush", "ConnectButtonForegroundBrush", "SecondaryTextBrush",
        "SectionLabelBrush", "RedZoneBrush", "TickBrush", "OkBrush", "MutedTextBrush", "NeedleBrush", "NeedleGlowColor", "DownColor",
        "DownBrush", "UpColor", "UpBrush", "DisplayFont",
    ];

    // The classes Task 4 kept as aliases. "num" stays a Run class (a number inside UI text); only elsewhere is it old.
    private static readonly string[] LegacyClasses = ["display", "muted", "secondary", "heading", "readout", "ghost", "pill"];

    [Fact]
    public void NoLegacyKeysOrClassesRemain()
    {
        var found = new List<string>();
        var keys = new Regex($@"\b({string.Join('|', LegacyKeys)})\b");
        var classes = new Regex($@"\.({string.Join('|', LegacyClasses)})(?![\w-])|(?<!Run)\.num(?![\w-])");
        foreach (var path in SourceText.Files(App, ".axaml", ".cs"))
        {
            var name = SourceText.Relative(path);
            found.AddRange(keys.Matches(File.ReadAllText(path)).Select(match => $"{name}: key {match.Value}"));
            if (path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var element in XDocument.Load(path).Descendants())
                {
                    foreach (var attribute in element.Attributes())
                    {
                        if (attribute.Name.LocalName == "Selector")
                        {
                            found.AddRange(classes.Matches(attribute.Value).Select(match => $"{name}: selector {attribute.Value}"));
                        }
                        else if (attribute.Name.LocalName == "Classes")
                        {
                            found.AddRange(Old(element, attribute.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).Select(old => $"{name}: {element.Name.LocalName} class {old}"));
                        }
                        else if (attribute.Name.LocalName.StartsWith("Classes.", StringComparison.Ordinal))
                        {
                            found.AddRange(Old(element, [attribute.Name.LocalName["Classes.".Length..]]).Select(old => $"{name}: {element.Name.LocalName} class {old}"));
                        }
                    }
                }
            }
            else
            {
                found.AddRange(SourceText.CSharpLiterals(File.ReadAllText(path)).Where(LegacyClasses.Contains).Select(old => $"{name}: class \"{old}\""));
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    private static IEnumerable<string> Old(XElement element, IEnumerable<string> names) =>
        names.Where(name => LegacyClasses.Contains(name) || (name == "num" && element.Name.LocalName != "Run"));

    [Fact]
    public void UiTextHasNoDashesOrOldGlyphs()
    {
        var found = new List<string>();
        foreach (var path in SourceText.Files(App, ".axaml", ".cs").Concat(SourceText.Files("src/MS43Logger.Core/Tuning", ".cs")))
        {
            var name = SourceText.Relative(path);
            var texts = path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                ? SourceText.XamlTexts(path)
                : SourceText.CSharpLiterals(File.ReadAllText(path));
            foreach (var text in texts)
            {
                foreach (var glyph in new[] { "—", "–", "★", "☆", "↻" }.Where(text.Contains))
                {
                    found.Add($"{name}: U+{(int)glyph[0]:X4} in \"{text}\"");
                }

                for (var at = text.IndexOf('−'); at >= 0; at = text.IndexOf('−', at + 1))
                {
                    if (at < 2 || at + 3 > text.Length || text.Substring(at - 2, 5) != "B − A")
                    {
                        found.Add($"{name}: U+2212 outside \"B − A\" in \"{text}\"");
                    }
                }

                if (name.StartsWith(App, StringComparison.Ordinal) && text.Contains("...", StringComparison.Ordinal))
                {
                    found.Add($"{name}: \"...\" in \"{text}\"");
                }
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    // What a screen reader says for each control a person can press: never nothing, never a type name.
    [AvaloniaFact]
    public async Task PressableControlsHaveNames()
    {
        var found = new List<string>();
        await ForEachScreen(opened =>
        {
            foreach (var control in GateClicks.Pressable(opened.Window))
            {
                var name = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control).GetName();
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("Avalonia.", StringComparison.Ordinal) || name.Contains(" { ", StringComparison.Ordinal))
                {
                    found.Add($"{opened.Window.Tag}: {GateClicks.Label(control)} is announced as \"{name}\"");
                }
            }
        }, bothThemes: false);

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Distinct()));
    }

    private static bool IsIcon(XElement element) => element.Name.LocalName == "StrokeIcon";

    private static bool Empty(XElement element, string attribute) => string.IsNullOrWhiteSpace(element.Attribute(attribute)?.Value);

    private static bool IsIconButton(Button button) =>
        button.Content is StrokeIcon || (button.Content is Panel { Children.Count: > 0 } panel && panel.Children.All(child => child is StrokeIcon));

    // Every visible control with its area in window coordinates; what a scroll viewer holds scrolls, so it is left out.
    private static IEnumerable<(Control Control, Rect Area)> Outermost(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is AdornerLayer || child is Control { IsVisible: false })
            {
                continue;
            }

            if (child is Control control && control.Bounds.Width > 0 && control.Bounds.Height > 0
                && control.TranslatePoint(default, (Visual)TopLevel.GetTopLevel(control)!) is { } origin)
            {
                yield return (control, new Rect(origin, control.Bounds.Size));
            }

            if (child is ScrollContentPresenter)
            {
                continue;
            }

            foreach (var inner in Outermost(child))
            {
                yield return inner;
            }
        }
    }

    private static string Describe(Control control) =>
        $"{control.GetType().Name}{(string.IsNullOrEmpty(control.Name) ? string.Empty : " " + control.Name)} in {control.GetVisualAncestors().OfType<Control>().FirstOrDefault(parent => !string.IsNullOrEmpty(parent.Name))?.Name}";

    private static IEnumerable<Control> TabStops(Visual scope) => scope.GetVisualDescendants().OfType<Control>()
        .Where(control => control.TemplatedParent is null && control.Focusable && control.IsTabStop && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);

    // A list that Tab enters once counts as one stop, whichever of its rows takes the focus.
    private static Visual Stop(Control control) =>
        control.GetVisualAncestors().OfType<ItemsControl>().FirstOrDefault(list => KeyboardNavigation.GetTabNavigation(list) == KeyboardNavigationMode.Once) ?? (Visual)control;

    // The system's ring (5.1: 2 px of Ink1 outside the control, never Fluent's default adorner), or the grid's and the
    // slider's own ring, drawn on :focus-visible.
    private static bool ShowsRing(Control focused) =>
        focused.Classes.Contains(":focus-visible")
        && (focused is MapGrid or ValueSlider
            || AdornerLayer.GetAdornerLayer(focused)?.Children.Any(adorner => AdornerLayer.GetAdornedElement(adorner) is { } target
                                                                               && (target == focused || focused.IsVisualAncestorOf(target))
                                                                               && adorner is Border { BorderThickness.Left: 2, Margin.Left: -4, BorderBrush: ISolidColorBrush ring }
                                                                               && ring.Color == ThemeBrushes.Color(focused, "Ink1Color")) == true);

    [AvaloniaFact]
    public async Task ClickThrough()
    {
        var folder = Path.Combine(SourceText.RepoRoot, "artifacts", "gate");
        Directory.CreateDirectory(folder);
        var gaps = new List<string>();
        foreach (var (screen, run) in GateClickThrough.Screens)
        {
            var lines = new Dictionary<bool, IReadOnlyList<string>>();
            foreach (var night in new[] { true, false })
            {
                if (await run(night) is not { } clicks)
                {
                    break;
                }

                lines[night] = clicks.Lines;
                gaps.AddRange(clicks.Gaps.Select(name => $"{screen} ({(night ? "Night" : "Day")}): nothing presses {name}"));
                clicks.Window.Close();
            }

            if (lines.Count == 2)
            {
                Assert.Equal(lines[true], lines[false]);
                File.WriteAllLines(Path.Combine(folder, $"click-through-{screen}.md"),
                    new[] { $"# Click-through: {screen} (headless, Night and Day, each line asserted)", string.Empty }.Concat(lines[true]));
            }
        }

        Assert.True(gaps.Count == 0, string.Join(Environment.NewLine, gaps));
    }

    // Names cut short keep their full text in a tooltip (system 8; final review, Important 3).
    [AvaloniaFact]
    public async Task TrimmedNamesKeepTheirFullText()
    {
        var (live, shell) = TestShell.Create(AppSettings.Load(TestShell.NewFolder()), new MainWindowViewModel(new ManualTime(), () => ["COM1"]));
        shell.Live.SelectedVariant = "ms43x";
        live.Width = 1024;
        live.Height = 680;
        live.Show();
        shell.Live.SearchText = "TIADFAC1";
        Dispatcher.UIThread.RunJobs();
        var row = Assert.Single(live.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("row-name") && block.DataContext is DecodedValueViewModel { Id: "TIADFAC1" }));
        Assert.Equal("Long Term Fuel Trim Multiplicative Bank 1 TIADFAC1", ToolTip.GetTip(row));
        var cell = live.GetVisualDescendants().OfType<TextBlock>().First(block => block.Classes.Contains("cell-title") && block.IsEffectivelyVisible);
        Assert.Equal("Engine Speed N", ToolTip.GetTip(cell));
        live.Close();

        if (!TuneScreen.Available || TuneRailTests.Local("MS43X001_release_E46_M54B25.bin") is not { } release)
        {
            return;
        }

        var (window, tune) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(TestShell.NewFolder()));
        window.Width = 1024;
        window.Height = 680;
        tune.Tune.SearchText = "c_acr_diag_iv_t_per_m";
        Dispatcher.UIThread.RunJobs();
        var ids = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("id") && block.Text?.StartsWith("c_acr_diag_iv_t_per_m", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, block => Assert.Equal(block.Text, ToolTip.GetTip(block)));

        tune.SelectedTab = ShellViewModel.CompareTab;
        Assert.True(tune.Compare.OpenB(release));
        Dispatcher.UIThread.RunJobs();
        var compared = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("crow-id") && block.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(compared);
        Assert.All(compared, block => Assert.Equal(block.Text, ToolTip.GetTip(block)));
        window.Close();
    }

    // The gate's renders (plan Task 15, step 3): every state in both themes, at 1440 x 900 and 1024 x 680.
    [AvaloniaFact]
    public async Task GateScreensForReview()
    {
        foreach (var state in GateScreens.All())
        {
            if (await state.Open() is not { } opened)
            {
                continue;
            }

            foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
            {
                GateScreens.Theme(opened, night);
                if (opened.Shell is null)
                {
                    Screens.Save(opened.Window, $"restyle-{state.Name}-{theme}");
                    continue;
                }

                foreach (var (width, height) in new[] { (1440, 900), (1024, 680) })
                {
                    opened.Window.Width = width;
                    opened.Window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    Screens.Save(opened.Window, $"restyle-{state.Name}-{theme}-{width}");
                }
            }

            opened.Window.Close();
        }
    }

    // Opens every gate state, in Night then Day when asked, and closes it after the check.
    private static async Task ForEachScreen(Action<GateWindow> check, bool bothThemes)
    {
        foreach (var state in GateScreens.All())
        {
            if (await state.Open() is not { } opened)
            {
                continue;
            }

            foreach (var night in bothThemes ? new[] { true, false } : [true])
            {
                GateScreens.Theme(opened, night);
                opened.Window.Tag = $"{state.Name} {(night ? "Night" : "Day")}";
                check(opened);
            }

            opened.Window.Close();
        }
    }
}
