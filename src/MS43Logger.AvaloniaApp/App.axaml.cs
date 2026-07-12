using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace MS43Logger.AvaloniaApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Night and Day fix the theme; Follow Windows tracks the Windows app theme while the app runs.</summary>
    public static void SetTheme(ThemeMode mode)
    {
        if (Current is not Application app)
        {
            return;
        }

        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Day => ThemeVariant.Light,
            ThemeMode.FollowWindows => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
