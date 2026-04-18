using Avalonia.Controls;
using Avalonia.Headless;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>Saves headless renders to artifacts/screens/ (git-ignored) for visual review.</summary>
internal static class Screens
{
    public static void Save(TopLevel window, string name)
    {
        var folder = Folder();
        if (folder is null)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(folder, name + ".png"));
    }

    private static string? Folder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MS43Logger.sln")))
            {
                var folder = Path.Combine(directory.FullName, "artifacts", "screens");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        return null;
    }
}
