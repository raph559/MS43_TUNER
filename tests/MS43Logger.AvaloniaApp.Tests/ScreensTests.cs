using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace MS43Logger.AvaloniaApp.Tests;

public class ScreensTests
{
    private static byte[] Bytes(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    [AvaloniaFact]
    public async Task CaptureShowsTheLayoutAfterAResize()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // The reviewer's steps: a bin open at 1440 x 900, then the window resized to the minimum.
        var (window, _) = await TuneScreen.OpenAsync();
        window.Width = 1024;
        window.Height = 680;
        Dispatcher.UIThread.RunJobs();

        // A capture that still holds the previous layout differs from the next one, which shows the new layout.
        using var first = Screens.Capture(window)!;
        using var second = Screens.Capture(window)!;
        Assert.Equal(Bytes(second), Bytes(first));
    }
}
