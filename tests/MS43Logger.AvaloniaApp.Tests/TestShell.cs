namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// A main window over throw-away settings. <c>new MainWindow()</c> reads and writes the user's real
/// <c>%AppData%/MS43Tuner/settings.json</c>, which tests must never touch.
/// </summary>
internal static class TestShell
{
    public static string NewFolder() => Path.Combine(Path.GetTempPath(), "ms43-ui-" + Guid.NewGuid().ToString("N"));

    // The Live screen gets a fixed port list: tests never list or open the machine's real COM ports.
    public static (MainWindow Window, ShellViewModel Shell) Create(AppSettings? settings = null, MainWindowViewModel? live = null)
    {
        var shell = new ShellViewModel(live ?? new MainWindowViewModel(ports: () => ["COM1"]), new TuneViewModel(settings ?? AppSettings.Load(NewFolder())));
        return (new MainWindow(shell), shell);
    }
}
