using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TuneCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-cmd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void EditOneCell(ShellViewModel shell)
    {
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));
    }
}

public sealed class ReviewFixTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-fix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
