using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;
using MS43Logger.AvaloniaApp.Views;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The save prompt and the connection log in the window's dialog host, and the Performance tests window.</summary>
public sealed class DialogTests : IDisposable
{
    private const string NoSpeed = "This definition has no vehicle speed channel";

    private static readonly IReadOnlyList<AdxValueDefinition> Ms43xChannels = BundledDefinitions.Load("ms43x").MatchChannels(Array.Empty<string>());

    private readonly string _folder = TestShell.NewFolder();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Visual root, string name)
    {
        Find.Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string? Text(Visual root, string name) => Find.Named<TextBlock>(root, name).Text;

    // What a reading shows, its unit run included: "7.3 s" with the en space before the unit.
    private static string Shown(Visual root, string name) =>
        string.Concat(Find.Named<TextBlock>(root, name).Inlines!.OfType<Run>().Select(run => run.Text));

    private static T Open<T>(MainWindow window)
        where T : Control
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(window.GetVisualDescendants().OfType<T>());
    }

    // One sample that holds only the vehicle speed, at the time the test's clock shows.
    private static void Speed(MainWindowViewModel live, double kmh) =>
        live.ShowSample([new DecodedValue(Ms43xChannels.Single(channel => channel.Id == "VS"), kmh)]);

    private static (PerformanceWindow Window, MainWindowViewModel Live, ManualTime Time) Performance()
    {
        var time = new ManualTime();
        var live = new MainWindowViewModel(time, () => ["COM1"]) { SelectedVariant = "ms43x" };
        var window = new PerformanceWindow { DataContext = live };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, live, time);
    }

    [AvaloniaFact]
    public void VsChannelDrivesThePerformanceTimer()
    {
        var (window, live, time) = Performance();
        Assert.Equal("n/a", Shown(window, "Time100Text"));
        Assert.Equal("n/a", Shown(window, "Time200Text"));

        Click(window, "StartTestButton");
        Assert.Equal("Stop the car to start", Text(window, "PerformanceStatus"));
        Speed(live, 0);
        Assert.Equal("Ready: drive off", Text(window, "PerformanceStatus"));
        time.Advance(TimeSpan.FromSeconds(0.5));
        Speed(live, 2);
        Assert.Equal("Timing…", Text(window, "PerformanceStatus"));
        time.Advance(TimeSpan.FromSeconds(3.5));
        Speed(live, 60);
        time.Advance(TimeSpan.FromSeconds(3.8));
        Speed(live, 100);

        Assert.Equal("7.3\u2002s", Shown(window, "Time100Text"));

        // Past 100 km/h the first time holds and the second keeps counting.
        time.Advance(TimeSpan.FromSeconds(2));
        Speed(live, 130);
        Assert.Equal("7.3\u2002s", Shown(window, "Time100Text"));
        Assert.Equal("9.3\u2002s", Shown(window, "Time200Text"));
    }

    [AvaloniaFact]
    public void SaveDetailFollowsTheSettings()
    {
        Assert.Equal("Saving keeps a .bak of the file as it was and adds the changes to its edit log.", ConfirmDialog.SaveDetail(keepBackup: true, appendEditLog: true));
        Assert.Equal("Saving keeps a .bak of the file as it was.", ConfirmDialog.SaveDetail(keepBackup: true, appendEditLog: false));
        Assert.Equal("Saving adds the changes to its edit log.", ConfirmDialog.SaveDetail(keepBackup: false, appendEditLog: true));
        Assert.Null(ConfirmDialog.SaveDetail(keepBackup: false, appendEditLog: false));

        // The prompt reads the settings as it opens, so it never promises a .bak that Save will not write.
        var settings = AppSettings.Load(_folder);
        settings.KeepBackup = false;
        var (window, shell) = TestShell.Create(settings);
        window.Show();
        _ = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
        var detail = Find.Named<TextBlock>(Open<ConfirmDialog>(window), "DetailText");
        Assert.Equal("Saving adds the changes to its edit log.", detail.Text);
        Assert.True(detail.IsVisible);
        Press(window, PhysicalKey.Escape);

        settings.AppendEditLog = false;
        _ = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
        Assert.False(Find.Named<TextBlock>(Open<ConfirmDialog>(window), "DetailText").IsVisible);
    }

    [AvaloniaFact]
    public async Task ConfirmDialogEscCancelsAndEnterSaves()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder));
        window.Show();
        var host = Find.Named<DialogHost>(window, "Dialogs");

        var cancelled = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
        var dialog = Open<ConfirmDialog>(window);
        Assert.Equal("Save the changes to CAR.bin?", Text(dialog, "MessageText"));
        var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["Cancel", "Don't save", "Save"], buttons.Select(button => button.Content as string));
        Assert.Contains("primary", buttons[^1].Classes);
        Press(window, PhysicalKey.Escape);
        Assert.Equal(SaveChoice.Cancel, await cancelled);
        Assert.False(host.IsOpen);

        var saved = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
        Open<ConfirmDialog>(window);
        Press(window, PhysicalKey.Enter);
        Assert.Equal(SaveChoice.Save, await saved);
        Assert.False(host.IsOpen);

        var discarded = shell.Tune.AskToSave!("Save the changes to CAR.bin?");
        Click(Open<ConfirmDialog>(window), "DontSaveButton");
        Assert.Equal(SaveChoice.DontSave, await discarded);
    }

    [AvaloniaFact]
    public void ConnectionLogShowsReasonAndLog()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder));
        window.Show();
        var host = Find.Named<DialogHost>(window, "Dialogs");
        shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));

        shell.Live.ShowConnectionLog();
        var view = Open<ConnectionLogView>(window);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Connection failed");
        Assert.Equal(shell.Live.Banner, Text(view, "ReasonText"));
        var log = Find.Named<TextBox>(view, "LogText");
        Assert.Equal(shell.Live.ConnectionLog, log.Text);
        Assert.Contains("Access to the port 'COM1' is denied.", log.Text);
        Assert.True(log.IsReadOnly);
        Assert.Equal(window.FindResource("MonoFont"), log.FontFamily);
        Press(window, PhysicalKey.Escape);
        Assert.False(host.IsOpen);

        shell.Live.ShowConnectionLog();
        Click(Open<ConnectionLogView>(window), "CloseButton");
        Assert.False(host.IsOpen);
    }

    [AvaloniaFact]
    public void StartTestIsDisabledWithoutASpeedChannel()
    {
        var (window, live, _) = Performance();
        var start = Find.Named<Button>(window, "StartTestButton");
        Assert.True(start.IsEnabled);

        live.PreviewChannels(Ms43xChannels.Where(channel => channel.Id != "VS").ToList());
        Dispatcher.UIThread.RunJobs();
        Assert.False(start.IsEnabled);
        Assert.Equal(NoSpeed, ToolTip.GetTip(start));
        Assert.True(ToolTip.GetShowOnDisabled(start));
        // A tooltip is never the only copy (system 5.9): the status line says it too.
        Assert.Equal(NoSpeed, Text(window, "PerformanceStatus"));

        // The ids match whatever their case; the stock definitions name the channel VEHICLESPEED.
        live.PreviewChannels(Ms43xChannels.Select(channel => channel.Id == "VS" ? channel with { Id = "vs" } : channel).ToList());
        Dispatcher.UIThread.RunJobs();
        Assert.True(start.IsEnabled);
        live.SelectedVariant = "b30";
        Dispatcher.UIThread.RunJobs();
        Assert.True(start.IsEnabled);
    }

    [AvaloniaFact]
    public void DialogScreensForReview()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder));
        window.Width = 1024;
        window.Height = 680;
        window.Show();
        var (performance, live, time) = Performance();

        void Save(TopLevel target, string state)
        {
            foreach (var (theme, night) in new[] { ("night", true), ("day", false) })
            {
                shell.IsNight = night;
                shell.IsDay = !night;
                Dispatcher.UIThread.RunJobs();
                Screens.Save(target, $"restyle-dialog-{state}-{theme}");
            }
        }

        _ = shell.Tune.AskToSave!("Save the changes to CAR_current_43X001_SP95_STAGE1.bin?");
        Save(window, "save");
        Press(window, PhysicalKey.Escape);
        shell.Live.ReportConnectFailure(new UnauthorizedAccessException("Access to the port 'COM1' is denied."));
        shell.Live.ShowConnectionLog();
        Save(window, "log");
        Press(window, PhysicalKey.Escape);

        Save(performance, "performance-idle");
        Click(performance, "StartTestButton");
        Speed(live, 0);
        time.Advance(TimeSpan.FromSeconds(0.5));
        Speed(live, 2);
        time.Advance(TimeSpan.FromSeconds(7.3));
        Speed(live, 100);
        time.Advance(TimeSpan.FromSeconds(4.4));
        Speed(live, 148);
        Save(performance, "performance-timing");
        live.PreviewChannels(Ms43xChannels.Where(channel => channel.Id != "VS").ToList());
        Save(performance, "performance-nospeed");
    }
}
