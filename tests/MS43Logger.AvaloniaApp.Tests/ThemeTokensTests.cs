using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class ThemeTokensTests
{
    private static object? Resource(string key, ThemeVariant theme) =>
        Application.Current!.TryGetResource(key, theme, out var value) ? value : null;

    [AvaloniaTheory]
    [InlineData("Frame", "#F2F4F7", "#101215")]
    [InlineData("Pane", "#FFFFFF", "#0A0B0D")]
    [InlineData("Fill", "#E9ECF1", "#1F2328")]
    [InlineData("Track", "#F2F4F7", "#1F2328")]
    [InlineData("Hairline", "#E1E5EA", "#23272D")]
    [InlineData("Edge", "#727C8C", "#6E7681")]
    [InlineData("Ink1", "#121826", "#F3F5F7")]
    [InlineData("Ink2", "#3D4656", "#AEB4BD")]
    [InlineData("Ink3", "#5B6576", "#858C97")]
    [InlineData("Now", "#EA5B0C", "#FF7A1A")]
    [InlineData("NowText", "#B4470A", "#FF7A1A")]
    [InlineData("Warn", "#B42318", "#FF6259")]
    [InlineData("Map1", "#CCF1EA", "#02423A")]
    [InlineData("Map2", "#9EDFE0", "#005C5E")]
    [InlineData("Map3", "#6DC7DB", "#077688")]
    [InlineData("Map4", "#4FAAD3", "#368EB3")]
    [InlineData("Map5", "#3D89C6", "#68A4D9")]
    [InlineData("Map6", "#3568B2", "#97BDF4")]
    [InlineData("Map7", "#324496", "#C8D6FF")]
    [InlineData("DivBelow", "#1F53B8", "#6CA7F5")]
    [InlineData("DivAbove", "#CE2854", "#F87F99")]
    [InlineData("Popup", "#FFFFFF", "#1F2328")]
    [InlineData("PopupEdge", "#E1E5EA", "#6E7681")]
    [InlineData("DialogSegTrack", "#E9ECF1", "#101215")]
    public void TokensMatchTheSystem(string name, string day, string night)
    {
        foreach (var (theme, hex) in new[] { (ThemeVariant.Light, day), (ThemeVariant.Dark, night) })
        {
            Assert.Equal(Color.Parse(hex), Assert.IsType<Color>(Resource(name + "Color", theme)));
            Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(name + "Brush", theme)).Color);
        }
    }

    [AvaloniaTheory]
    [InlineData("HoverOverlayBrush", "#0F121826", "#14F3F5F7")]
    [InlineData("PressedOverlayBrush", "#1F121826", "#24F3F5F7")]
    [InlineData("ScrimBrush", "#4D121826", "#80000000")]
    public void OverlaysMatchTheSystem(string key, string day, string night)
    {
        Assert.Equal(Color.Parse(day), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(key, ThemeVariant.Light)).Color);
        Assert.Equal(Color.Parse(night), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(key, ThemeVariant.Dark)).Color);
    }

    [AvaloniaFact]
    public void UiFontHasARealSemiBold()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A real SemiBold face sets wider glyphs; a variable font stuck on its Regular instance does not.
        var semiBold = Width(new Typeface(Font("UiFont"), FontStyle.Normal, FontWeight.SemiBold), "Settings", 40);
        var regular = Width(new Typeface(Font("UiFont")), "Settings", 40);

        Assert.True(semiBold > regular * 1.01, $"SemiBold {semiBold} against Regular {regular}");
    }

    [AvaloniaFact]
    public void CustomControlsRepaintOnThemeChange()
    {
        var history = new SampleHistory(TimeSpan.FromSeconds(10));
        var start = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 60; i++)
        {
            history.Add(start + TimeSpan.FromSeconds(i / 6.0), Math.Sin(i / 6.0));
        }

        var controls = new (string Name, Control Control)[]
        {
            ("ItemGlyph", new ItemGlyph { Kind = ItemKind.Map, Width = 16, Height = 16 }),
            ("Sparkline", new Sparkline { History = history, Width = 80, Height = 24 }),
            ("MapGrid", new MapGrid { Editor = TableEditorViewModel.Snapshot(CompareGridTests.Row(1, 2, 3), new HashSet<(int, int)>()), Width = 220, Height = 70 }),
            ("Gauge", new Gauge { Minimum = 0, Maximum = 8000, Value = 2480, Width = 160, Height = 160 }),
        };

        var unchanged = new List<string>();
        foreach (var (name, control) in controls)
        {
            control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            var window = new Window { Width = 300, Height = 200, Background = Brushes.Magenta, RequestedThemeVariant = ThemeVariant.Dark, Content = control };
            window.Show();
            var night = Pixels(window, control);
            window.RequestedThemeVariant = ThemeVariant.Light;
            var day = Pixels(window, control);
            if (night.SequenceEqual(day))
            {
                unchanged.Add(name);
            }

            window.Close();
        }

        Assert.Empty(unchanged);
    }

    private static FontFamily Font(string key) => (FontFamily)Application.Current!.FindResource(key)!;

    private static double Width(Typeface typeface, string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black).WidthIncludingTrailingWhitespace;

    /// <summary>The rendered pixels of <paramref name="control"/>'s bounds, as BGRA or RGBA bytes.</summary>
    private static byte[] Pixels(Window window, Control control)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        var origin = control.TranslatePoint(default, window)!.Value;
        var rect = new PixelRect((int)origin.X, (int)origin.Y, (int)control.Bounds.Width, (int)control.Bounds.Height);
        return Read(frame, rect);
    }

    private static byte[] Read(Bitmap bitmap, PixelRect rect)
    {
        var stride = rect.Width * 4;
        var bytes = new byte[stride * rect.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(rect, handle.AddrOfPinnedObject(), bytes.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return bytes;
    }
}
