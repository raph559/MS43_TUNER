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

    [AvaloniaFact]
    public async Task CursorPointIsBetweenCellCentres()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _, _) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        var a = grid.CellBounds(5, 1).Center;
        var b = grid.CellBounds(6, 2).Center;

        Assert.Equal(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), grid.CursorPoint(5.5, 1.5));
        Assert.Equal(a, grid.CursorPoint(5, 1));
    }

    [AvaloniaFact]
    public async Task OrangeOutlinesTheCellsAroundTheEngine()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        using (var before = new Frame(window))
        {
            Assert.False(before.AnyNeedle(BoundsInWindow(grid, window)));
        }

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        var fix = shell.Tune.Editor!.Live!.Fix;
        using var frame = new Frame(window);

        Assert.True(frame.AnyNeedle(new Rect(InWindow(grid, window, grid.CursorPoint(fix.Row!.Value.Position, fix.Col!.Value.Position)), new Size(1, 1))));
        var heavy = grid.CellBounds(6, 1);
        Assert.True(frame.AnyOrangeNear(InWindow(grid, window, new Point(heavy.Left - 1, heavy.Center.Y))));
        var far = grid.CellBounds(15, 12);
        Assert.False(frame.AnyOrangeNear(InWindow(grid, window, new Point(far.Left - 1, far.Center.Y))));
        Screens.Save(window, "live-cursor-map-night");

        shell.IsNight = false;
        Screens.Save(window, "live-cursor-map-day");
    }

    [AvaloniaFact]
    public async Task StaleCursorIsNotOrange()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, time) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        time.Advance(TimeSpan.FromSeconds(2));
        shell.Tune.LiveCursor.Tick();
        var fix = shell.Tune.Editor!.Live!.Fix;

        using var frame = new Frame(window);
        Assert.True(shell.Tune.Editor.Live.IsStale);
        Assert.False(frame.AnyNeedle(BoundsInWindow(grid, window)));
        Assert.False(frame.AnyOrangeNear(InWindow(grid, window, grid.CursorPoint(fix.Row!.Value.Position, fix.Col!.Value.Position)), 1));
    }

    [AvaloniaFact]
    public async Task CurveAndSurfaceDrawTheirMarker()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var surface = Find.Named<Surface3D>(window, "Surface");
        using (var before = new Frame(window))
        {
            Assert.False(before.AnyNeedle(BoundsInWindow(surface, window)));
        }

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        using (var frame = new Frame(window))
        {
            Assert.True(frame.AnyNeedle(BoundsInWindow(surface, window)));
        }

        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("id_t_tco_puc_deacc__tco");
        var curve = Find.Named<CurveChart>(window, "Curve");
        LiveFeed.Push(shell, ("TCO", 124));
        shell.Tune.LiveCursor.Tick();
        using (var frame = new Frame(window))
        {
            Assert.True(frame.AnyNeedle(BoundsInWindow(curve, window)));
        }

        Screens.Save(window, "live-cursor-curve");
    }

    [AvaloniaFact]
    public async Task ScreensForReview()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, time) = await LiveFeed.OpenAsync("ip_map_ve_1__map__n");
        foreach (var (map, n) in new[] { (2400L, 1500L), (2700L, 1800L), (3000L, 2000L), (3300L, 2300L) })
        {
            LiveFeed.Push(shell, ("MAP", map), ("N", n));
            shell.Tune.LiveCursor.Tick();
            time.Advance(TimeSpan.FromMilliseconds(120));
        }

        Screens.Save(window, "live-cursor-ve");
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("id_lam_tco_min__tco_st");
        Screens.Save(window, "live-cursor-no-input");
    }
}
