using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace MS43Logger.AvaloniaApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static readonly Uri MotionSource = new("avares://MS43Logger.AvaloniaApp/Theme/Motion.axaml");

    /// <summary>True while the motion layer (Theme/Motion.axaml) is loaded.</summary>
    public static bool MotionIsOn => Current?.Styles.OfType<StyleInclude>().Any(IsMotion) == true;

    /// <summary>
    /// Loads or drops the motion layer. At startup it follows Windows' "Animation effects" setting
    /// (<see cref="MotionSettings"/>); with it off, every state is reached at once.
    /// </summary>
    public static void ApplyMotion(bool on)
    {
        if (Current is not Application app || on == MotionIsOn)
        {
            return;
        }

        if (on)
        {
            app.Styles.Add(NewMotionLayer());
        }
        else
        {
            app.Styles.Remove(app.Styles.OfType<StyleInclude>().First(IsMotion));
        }
    }

    /// <summary>The motion layer as a style include, for the app or for one window's own styles.</summary>
    public static StyleInclude NewMotionLayer() => new(MotionSource) { Source = MotionSource };

    private static bool IsMotion(StyleInclude include) => include.Source == MotionSource;

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyMotion(MotionSettings.AnimationsEnabled);
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
