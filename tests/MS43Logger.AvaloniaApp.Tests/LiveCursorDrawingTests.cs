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

        public Frame(TopLevel window) => _bitmap = Screens.Capture(window)!;

        public bool AnyNeedle(Rect area) => Any(area, Needle);

        /// <summary>Pixels within 12 of <paramref name="colour"/> on each channel, such as the theme's Ink3.</summary>
        public bool AnyNear(Rect area, Avalonia.Media.Color colour) =>
            Any(area, (r, g, b) => Math.Abs(r - colour.R) <= 12 && Math.Abs(g - colour.G) <= 12 && Math.Abs(b - colour.B) <= 12);

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
    public async Task CursorDotAndTicksAreTheOnlyOrange()
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
        var at = grid.CursorPoint(fix.Row!.Value.Position, fix.Col!.Value.Position);
        var top = grid.CellBounds(0, 0).Top;
        var left = grid.CellBounds(0, 0).Left;
        using var frame = new Frame(window);

        Assert.True(frame.AnyNeedle(new Rect(InWindow(grid, window, at), new Size(1, 1))), "no dot at the fix");
        Assert.True(frame.AnyNeedle(new Rect(InWindow(grid, window, new Point(at.X - 1, top - 5)), new Size(3, 4))), "no tick in the column header gutter");
        Assert.True(frame.AnyNeedle(new Rect(InWindow(grid, window, new Point(left - 5, at.Y - 1)), new Size(4, 3))), "no tick in the row header gutter");

        // No outlines around the cells the ECU reads, and no crosshair through far cells.
        var heavy = grid.CellBounds(6, 1);
        Assert.False(frame.AnyOrangeNear(InWindow(grid, window, new Point(heavy.Left - 1, heavy.Center.Y)), 1), "an outline around cell (6,1)");
        var far = grid.CellBounds(15, 12);
        Assert.False(frame.AnyOrangeNear(InWindow(grid, window, far.Center), 1), "orange at cell (15,12)");
        Screens.Save(window, "live-cursor-map-night");

        shell.IsNight = false;
        Screens.Save(window, "live-cursor-map-day");
        shell.IsNight = true;
    }

    [AvaloniaFact]
    public async Task UnusedMapGreysTheCursor()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        LiveFeed.Push(shell, new (int, byte)[] { (0x00, 0x02) }, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        var fix = shell.Tune.Editor!.Live!.Fix;
        var at = InWindow(grid, window, grid.CursorPoint(fix.Row!.Value.Position, fix.Col!.Value.Position));

        using (var frame = new Frame(window))
        {
            Assert.True(shell.Tune.Editor.Live.IsUnused);
            Assert.False(frame.AnyNeedle(BoundsInWindow(grid, window)), "orange on a map the ECU is not using");
            Assert.True(frame.AnyNear(new Rect(at.X - 1, at.Y - 1, 3, 3), ThemeBrushes.Color(grid, "Ink3Brush")), "no Ink3 dot at the fix");
            Screens.Save(window, "live-cursor-not-in-use-night");
        }

        var app = Avalonia.Application.Current!;
        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        try
        {
            using var day = new Frame(window);
            var dayInk3 = ThemeBrushes.Color(grid, "Ink3Brush");
            Assert.Equal(Avalonia.Media.Color.Parse("#5B6576"), dayInk3);
            Assert.True(day.AnyNear(new Rect(at.X - 1, at.Y - 1, 3, 3), dayInk3), "no Day Ink3 dot at the fix");
            Screens.Save(window, "live-cursor-not-in-use-day");
        }
        finally
        {
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }

        LiveFeed.Push(shell, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        using var live = new Frame(window);
        Assert.True(live.AnyNeedle(new Rect(at, new Size(1, 1))), "no orange dot once the map is in use");
        Screens.Save(window, "live-cursor-in-use-night");
    }

    [AvaloniaFact]
    public async Task UnusedMapOffTheGridIsAFilledGreyDotNotTheStaleRing()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, _) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        LiveFeed.Push(shell, new (int, byte)[] { (0x00, 0x02) }, ("N", 7200), ("MAFIGA", 20000));
        shell.Tune.LiveCursor.Tick();
        Assert.True(shell.Tune.Editor!.Live!.IsUnused);
        var last = grid.CellBounds(19, 15);
        var edge = InWindow(grid, window, new Point(last.Right, last.Bottom));

        using var frame = new Frame(window);
        // The mark is centred on the grid's corner and clipped there; one pixel inside is in a filled dot but in the hole of the stale ring.
        Assert.True(frame.AnyNear(new Rect(edge.X - 1.5, edge.Y - 1.5, 1, 1), ThemeBrushes.Color(grid, "Ink3Brush")), "the centre of the mark is not filled: it reads as the stale ring");
    }

    [AvaloniaFact]
    public async Task UnusedCursorTurnsIntoTheStaleRing()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell, time) = await LiveFeed.OpenAsync();
        var grid = Find.Named<MapGrid>(window, "MapGrid");
        LiveFeed.Push(shell, new (int, byte)[] { (0x00, 0x02) }, ("N", 2480), ("MAFIGA", 1415));
        shell.Tune.LiveCursor.Tick();
        time.Advance(TimeSpan.FromSeconds(2));
        shell.Tune.LiveCursor.Tick();
        var live = shell.Tune.Editor!.Live!;
        var at = InWindow(grid, window, grid.CursorPoint(live.Fix.Row!.Value.Position, live.Fix.Col!.Value.Position));

        using var frame = new Frame(window);
        Assert.True(live.IsStale && live.IsUnused);
        Assert.False(frame.AnyNear(new Rect(at.X - 0.5, at.Y - 0.5, 1, 1), ThemeBrushes.Color(grid, "Ink3Brush")), "a filled dot: the stale ring was not drawn");
        Assert.True(frame.AnyNear(new Rect(at.X - 4, at.Y - 4, 8, 8), ThemeBrushes.Color(grid, "Ink3Brush")), "no Ink3 ring");
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
