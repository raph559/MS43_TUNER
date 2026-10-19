using Avalonia.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Opens the real car file in a window with throw-away settings, on the Tune tab.</summary>
internal static class TuneScreen
{
    public static readonly string? Car = TuneRailTests.Local("CAR_current_43X001_SP95_STAGE1.bin");
    public static readonly string? Xdf = TuneRailTests.Local("Siemens_MS43_MS43X001_512K.xdf");

    public static bool Available => Car is not null && Xdf is not null;

    public static async Task<(MainWindow Window, ShellViewModel Shell)> OpenAsync(string? select = "ip_iga_ron98_pl__n__maf", string? binPath = null, TimeProvider? time = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ms43-ui-" + Guid.NewGuid().ToString("N"));
        var shell = new ShellViewModel(new MainWindowViewModel(), new TuneViewModel(AppSettings.Load(folder), time));
        var window = new MainWindow(shell);
        shell.SelectedTab = ShellViewModel.TuneTab;
        window.Show();
        await shell.Tune.OpenAsync(binPath ?? Car!, Xdf);
        if (select is not null)
        {
            shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle(select);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    public static string CopyCar(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "CAR.bin");
        File.Copy(Car!, path, overwrite: true);
        return path;
    }
}
