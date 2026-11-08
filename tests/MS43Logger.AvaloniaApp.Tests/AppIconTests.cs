using System.Xml.Linq;
using Avalonia.Headless.XUnit;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>The owner: the taskbar showed Windows' blank icon. The app's mark is the exe's icon and the window's.</summary>
public sealed class AppIconTests
{
    private const string Icon = "src/MS43Logger.AvaloniaApp/Assets/ms43-tuner.ico";

    [Fact]
    public void TheExeCarriesTheIcon()
    {
        var project = XDocument.Load(Path.Combine(SourceText.RepoRoot, "src/MS43Logger.AvaloniaApp/MS43Logger.AvaloniaApp.csproj"));

        Assert.Equal(@"Assets\ms43-tuner.ico", project.Descendants("ApplicationIcon").Single().Value);
    }

    // An ICO header (reserved 0, type 1) with the sizes Windows picks from: 16 to 48 for the taskbar and title, 256 for tiles.
    [Fact]
    public void TheIconHoldsEverySizeWindowsAsksFor()
    {
        var bytes = File.ReadAllBytes(Path.Combine(SourceText.RepoRoot, Icon));

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + 16 * i] == 0 ? 256 : bytes[6 + 16 * i]).ToHashSet();
        Assert.Superset(new HashSet<int> { 16, 20, 24, 32, 40, 48, 64, 256 }, sizes);
    }

    [AvaloniaFact]
    public void TheWindowShowsTheIcon()
    {
        var window = new MainWindow(new ShellViewModel(new MainWindowViewModel(ports: () => ["COM1"]),
            new TuneViewModel(AppSettings.Load(Path.Combine(Path.GetTempPath(), "ms43-icon-" + Guid.NewGuid().ToString("N"))))));

        Assert.NotNull(window.Icon);
    }
}
