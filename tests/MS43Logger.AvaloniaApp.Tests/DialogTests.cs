using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Shell;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The save prompt and the connection log in the window's dialog host.</summary>
public sealed class DialogTests : IDisposable
{
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

    private static T Open<T>(MainWindow window)
        where T : Control
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(window.GetVisualDescendants().OfType<T>());
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
    public void DialogScreensForReview()
    {
        var (window, shell) = TestShell.Create(AppSettings.Load(_folder));
        window.Width = 1024;
        window.Height = 680;
        window.Show();

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
    }
}
