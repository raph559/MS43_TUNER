using Avalonia;
using Avalonia.Headless;
using MS43Logger.AvaloniaApp;

[assembly: AvaloniaTestApplication(typeof(MS43Logger.AvaloniaApp.Tests.TestAppBuilder))]

namespace MS43Logger.AvaloniaApp.Tests;

public static class TestAppBuilder
{
    // Renders and colour checks read finished states, so the run starts with the motion layer off; MotionTests turns
    // it on for itself.
    public static AppBuilder BuildAvaloniaApp()
    {
        MotionSettings.AnimationsEnabled = false;
        return Configure();
    }

    private static AppBuilder Configure() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
