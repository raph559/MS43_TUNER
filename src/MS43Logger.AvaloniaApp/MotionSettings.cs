using System;
using System.Runtime.InteropServices;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// Whether the app may animate (system 10.3): Windows' "Animation effects" setting (SPI_GETCLIENTAREAANIMATION), true
/// elsewhere. With it off the edit flash is skipped and the live trail is drawn without its width falloff.
/// </summary>
public static class MotionSettings
{
    private const uint GetClientAreaAnimation = 0x1042;

    public static bool AnimationsEnabled { get; set; } = ReadSystem();

    private static bool ReadSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return !SystemParametersInfo(GetClientAreaAnimation, 0, out var enabled, 0) || enabled;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
