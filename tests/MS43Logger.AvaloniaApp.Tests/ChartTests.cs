using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class ChartTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-chart-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static Color PixelAt(TopLevel window, Point point)
    {
        using var frame = Screens.Capture(window)!;
        using var buffer = frame.Lock();
        var pixel = Marshal.ReadInt32(buffer.Address, (int)point.Y * buffer.RowBytes + (int)point.X * 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromRgb((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }

    private static void Near(Color expected, Color actual, int tolerance = 10) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"expected {expected}, got {actual}");

    [AvaloniaFact]
    public async Task SurfaceLeavesHatchedRowOut()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();

        // 20 × 16 cells, less the 16 of the 6990 rpm row at the storage limit (system 2.2).
        Assert.Equal(304, Find.Named<Surface3D>(window, "Surface").IncludedCellCount);
        Assert.Equal("The 6990 rpm row (storage limit) is not drawn.", shell.Tune.SurfaceNote);
    }

    [AvaloniaFact]
    public async Task SurfaceTurnsWithArrowKeys()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync();
        var surface = Find.Named<Surface3D>(window, "Surface");
        Assert.True(surface.Focusable);
        var (yaw, pitch) = (surface.Yaw, surface.Pitch);
        surface.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);

        Assert.Equal(yaw - 0.1, surface.Yaw, 6);
        Assert.Equal(0.05, Math.Abs(surface.Pitch - pitch), 6);

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);

        Assert.Equal((yaw, pitch), (surface.Yaw, surface.Pitch));
    }

    [AvaloniaFact]
    public async Task CurvePointsAreSquare()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, _) = await TuneScreen.OpenAsync("ip_iga_tco_st__tco");
        var curve = Find.Named<CurveChart>(window, "Curve");
        Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(7, curve.PointRect(i).Width);
            Assert.Equal(7, curve.PointRect(i).Height);
        }
    }

    [AvaloniaFact]
    public async Task CurvePointsClearTheAxes()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        // The y axis is drawn at x 44; the first and last points sit 8 px inside the plot, as in the mockup.
        var (window, _) = await TuneScreen.OpenAsync("ip_iga_tco_st__tco");
        var curve = Find.Named<CurveChart>(window, "Curve");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(52, curve.PointRect(0).Center.X, 3);
        Assert.Equal(curve.Bounds.Width - 24, curve.PointRect(7).Center.X, 3);
    }

    [AvaloniaFact]
    public async Task CurvePointsTakeTheirCellColour()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync("ip_iga_tco_st__tco");
        var curve = Find.Named<CurveChart>(window, "Curve");
        var editor = shell.Tune.Editor!;
        var range = editor.ColourRange;

        // Point 3 is not selected (a new item selects its first cell), so it shows its fill, framed by a 1 px Ink2 edge.
        var expected = MapRamp.Color((editor.Values.Physical[3, 0] - range.Min) / (range.Max - range.Min), night: true);
        Near(expected, PixelAt(window, curve.TranslatePoint(curve.PointRect(3).Center, window)!.Value));
    }

    [AvaloniaFact]
    public async Task ScaleCaptionNamesTheLimit()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();

        Assert.Equal("Colour: 3.0 to 39.0 °CRK. Hatched cells are at the storage limit (-23.6).", shell.Tune.ScaleCaption);
        var bar = Find.Named<ColourScale>(window, "ScaleBar");
        Assert.Equal(("3.0", "21.0", "39.0"), (bar.MinLabel, bar.MidLabel, bar.MaxLabel));
        Assert.False(bar.Diverging);
    }

    [AvaloniaFact]
    public async Task ConstantMapCaption()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(binPath: TuneScreen.CopyCar(_folder));
        var editor = shell.Tune.Editor!;
        editor.Selection = new CellRange(0, 0, 19, 15);
        editor.Apply(new EditRequest(EditKind.Set, 60));

        var values = editor.Values;
        var z = values.Table.Z;
        var value = ValueCodec.Format(values.Physical[0, 0], values.Raw[0, 0], z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
        Assert.Equal($"All {values.Rows * values.Cols} cells are {value} °CRK", shell.Tune.ScaleCaption);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColourScaleDrawsItsEnds(bool diverging)
    {
        var scale = new ColourScale { Width = 200, Height = 40, Diverging = diverging, MinLabel = "3.0", MidLabel = "21.0", MaxLabel = "39.0" };
        var window = new Window { Width = 240, Height = 80, RequestedThemeVariant = ThemeVariant.Dark, Content = scale };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Point At(double x) => scale.TranslatePoint(new Point(x, 4), window)!.Value;

        if (diverging)
        {
            Near(Color.Parse("#6CA7F5"), PixelAt(window, At(2)), 24);
            Near(Color.Parse("#0A0B0D"), PixelAt(window, At(100)), 24);
            Near(Color.Parse("#F87F99"), PixelAt(window, At(198)), 24);
        }
        else
        {
            Near(MapRamp.Color(0, night: true), PixelAt(window, At(2)), 16);
            Near(MapRamp.Color(1, night: true), PixelAt(window, At(198)), 16);
        }
    }
}
