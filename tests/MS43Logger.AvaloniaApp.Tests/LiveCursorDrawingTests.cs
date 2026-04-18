using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Tests;

public class LiveCursorDrawingTests
{
    /// <summary>Orange blended over the map: red well above green, green above blue. Text's sub-pixel fringes can match too, so use it on spots away from text.</summary>
    private static bool Orangeish(byte r, byte g, byte b) => r > 150 && r - g > 50 && g > b;

    /// <summary>The Night needle colour #FF6A1A itself.</summary>
    private static bool Needle(byte r, byte g, byte b) => Math.Abs(r - 0xFF) <= 30 && Math.Abs(g - 0x6A) <= 30 && Math.Abs(b - 0x1A) <= 30;

    private sealed class Frame : IDisposable
    {
        private readonly WriteableBitmap _bitmap;

        public Frame(TopLevel window)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            _bitmap = window.CaptureRenderedFrame()!;
        }

        public bool AnyNeedle(Rect area) => Any(area, Needle);

        public bool AnyOrangeNear(Point point, double radius = 3) => Any(new Rect(point.X - radius, point.Y - radius, 2 * radius, 2 * radius), Orangeish);

        private bool Any(Rect area, Func<byte, byte, byte, bool> match)
        {

            using var buffer = _bitmap.Lock();
            var size = _bitmap.PixelSize;
            for (var y = Math.Max(0, (int)area.Top); y <= Math.Min(size.Height - 1, (int)area.Bottom); y++)
            {
                for (var x = Math.Max(0, (int)area.Left); x <= Math.Min(size.Width - 1, (int)area.Right); x++)
                {
                    var pixel = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                    var (r, g, bl) = buffer.Format == PixelFormat.Rgba8888
                        ? ((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
                        : ((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
                    if (match(r, g, bl))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Dispose() => _bitmap.Dispose();
    }

    private static Point InWindow(Visual control, MainWindow window, Point point) => control.TranslatePoint(point, window)!.Value;

    private static Rect BoundsInWindow(Control control, MainWindow window) =>
        new(InWindow(control, window, default), control.Bounds.Size);
}
