using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class DialogHostTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-dialog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void Press(MainWindow window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool FocusIsInside(MainWindow window, Control dialog) =>
        window.FocusManager?.GetFocusedElement() is Visual focused && (focused == dialog || dialog.IsVisualAncestorOf(focused));

    [AvaloniaFact]
    public void DialogCoversTheStatusBar()
    {
        var (window, shell) = TestShell.Create();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var day = Find.Named<Control>(window, "DayButton");
        var centre = day.TranslatePoint(new Point(day.Bounds.Width / 2, day.Bounds.Height / 2), window)!.Value;
        Assert.True(Hits(window, centre, day));

        Press(window, PhysicalKey.Comma, RawInputModifiers.Control);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Hits(window, centre, day));
        Assert.True(shell.IsNight);
    }

    private static bool Hits(Window window, Point point, Visual target) =>
        window.InputHitTest(point) is Visual hit && (hit == target || target.IsVisualAncestorOf(hit));

    [AvaloniaFact]
    public async Task ShortcutsDoNothingBehindADialog()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var bin = TuneScreen.CopyCar(_folder);
        var (window, shell) = await TuneScreen.OpenAsync(binPath: bin);
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
        shell.SelectedTab = ShellViewModel.LiveTab;
        var onDisk = File.ReadAllBytes(bin);

        Press(window, PhysicalKey.Comma, RawInputModifiers.Control);
        var dialog = window.GetVisualDescendants().OfType<SettingsView>().Single();
        Press(window, PhysicalKey.S, RawInputModifiers.Control);
        Press(window, PhysicalKey.Z, RawInputModifiers.Control);
        Press(window, PhysicalKey.O, RawInputModifiers.Control);
        Press(window, PhysicalKey.K, RawInputModifiers.Control);

        Assert.Equal(onDisk, File.ReadAllBytes(bin));
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);
        Assert.Equal(ShellViewModel.LiveTab, shell.SelectedTab);
        Assert.True(FocusIsInside(window, dialog));

        Press(window, PhysicalKey.Escape);

        Assert.Empty(window.GetVisualDescendants().OfType<SettingsView>());
        Assert.Equal("1 item not saved", shell.Tune.SaveStateText);
    }

    /// <summary>A bare host in a window, and a dialog with a field and Cancel / Save buttons (Save is the primary).</summary>
    private static (Window Window, DialogHost Host, StackPanel Dialog, List<string> Clicks) BareDialog()
    {
        var host = new DialogHost();
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Show();
        var clicks = new List<string>();
        var cancel = new Button { Name = "Cancel", Content = "Cancel" };
        var save = new Button { Name = "Save", Content = "Save", Classes = { "primary" } };
        cancel.Click += (_, _) => clicks.Add("Cancel");
        save.Click += (_, _) => clicks.Add("Save");
        var dialog = new StackPanel { Children = { new TextBox { Name = "Field" }, cancel, save } };
        return (window, host, dialog, clicks);
    }

    [AvaloniaFact]
    public void EnterPressesThePrimaryButton()
    {
        var (window, host, dialog, clicks) = BareDialog();
        _ = host.ShowAsync(dialog);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "Save" }, clicks);

        // A focused button takes Enter itself.
        Find.Named<Button>(window, "Cancel").Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "Save", "Cancel" }, clicks);
    }

    [AvaloniaFact]
    public async Task SecondDialogReplacesTheFirst()
    {
        var (_, host, first, _) = BareDialog();
        var firstResult = host.ShowAsync(first);
        Dispatcher.UIThread.RunJobs();
        var second = new TextBlock { Text = "Save changes to CAR.bin?" };

        var secondResult = host.ShowAsync(second);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(await firstResult);
        Assert.True(host.IsOpen);
        Assert.Same(host, second.FindAncestorOfType<DialogHost>());
        Assert.Null(first.GetVisualParent());

        host.Close("saved");

        Assert.Equal("saved", await secondResult);
    }

    [AvaloniaFact]
    public async Task CallerResumesAfterCloseReturns()
    {
        var (_, host, dialog, _) = BareDialog();
        var order = new List<string>();

        async Task Caller()
        {
            await host.ShowAsync(dialog);
            order.Add("caller resumed");
        }

        var caller = Caller();
        Dispatcher.UIThread.RunJobs();
        host.Close();
        order.Add("Close returned");
        Dispatcher.UIThread.RunJobs();
        await caller;

        Assert.Equal(new[] { "Close returned", "caller resumed" }, order);
    }

    [AvaloniaFact]
    public void ShowAsyncDoesNotRunOtherQueuedWork()
    {
        var (window, host, dialog, _) = BareDialog();
        var ran = false;
        Dispatcher.UIThread.Post(() => ran = true, DispatcherPriority.Normal);

        _ = host.ShowAsync(dialog);

        Assert.False(ran);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ran);
        Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
    }
}
