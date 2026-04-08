using Avalonia;
using Avalonia.Headless;
using MS43Logger.AvaloniaApp;

[assembly: AvaloniaTestApplication(typeof(MS43Logger.AvaloniaApp.Tests.TestAppBuilder))]

namespace MS43Logger.AvaloniaApp.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
