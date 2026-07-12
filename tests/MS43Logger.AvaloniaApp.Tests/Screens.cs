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

        using var frame = Capture(window);
        frame?.Save(Path.Combine(folder, name + ".png"));
    }

    /// <summary>
    /// The first frame after a layout change can still hold the previous layout (cropped to the new size), so one frame
    /// is rendered and dropped before the frame that is returned.
    /// </summary>
    public static Avalonia.Media.Imaging.WriteableBitmap? Capture(TopLevel window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window.CaptureRenderedFrame();
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
