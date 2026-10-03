using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The one map colour path: sea teal at the low end to ultramarine at the high end, the same hues in both
/// themes. Day runs light to dark and Night dark to light, so the peak of a map always sits farthest from the pane.
/// </summary>
public static class MapRamp
{
    private static readonly Color[] DayStops = Parse("#CCF1EA", "#9EDFE0", "#6DC7DB", "#4FAAD3", "#3D89C6", "#3568B2", "#324496");
    private static readonly Color[] NightStops = Parse("#02423A", "#005C5E", "#077688", "#368EB3", "#68A4D9", "#97BDF4", "#C8D6FF");

    // Brushes are Avalonia objects bound to the UI thread, so they are built on first use there, never by the
    // static constructor: the colour maths below may run on any thread.
    private static IBrush? _dayGradient;
    private static IBrush? _nightGradient;

    /// <summary>The colour at <paramref name="t"/> from 0 (lowest) to 1, mixed linearly in sRGB between the 7 stops.</summary>
    public static Color Color(double t, bool night)
    {
        var stops = night ? NightStops : DayStops;
        var at = (double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1)) * (stops.Length - 1);
        var i = Math.Min((int)Math.Floor(at), stops.Length - 2);
        return Mix(stops[i], stops[i + 1], at - i);
    }

    /// <summary>The path as a left-to-right gradient, for colour scales.</summary>
    public static IBrush Gradient(bool night) =>
        night ? _nightGradient ??= Build(NightStops) : _dayGradient ??= Build(DayStops);

    /// <summary>
    /// Black on fills of relative luminance 0.1791 or more, white below: either choice then gives 4.58:1 or more on any
    /// fill, including the mixes between stops.
    /// </summary>
    public static Color TextOn(Color fill) => Luminance(fill) >= 0.1791 ? Colors.Black : Colors.White;

    /// <summary>
    /// The A − B scale: <paramref name="pane"/> at 0, <paramref name="above"/> at +1 and
    /// <paramref name="below"/> at -1, mixed by |t|^0.7 so small changes still show.
    /// </summary>
    public static Color Diverging(double t, Color pane, Color below, Color above)
    {
        t = double.IsNaN(t) ? 0 : Math.Clamp(t, -1, 1);
        return Mix(pane, t >= 0 ? above : below, Math.Pow(Math.Abs(t), 0.7));
    }

    private static Color Mix(Color a, Color b, double k) =>
        Avalonia.Media.Color.FromRgb(Channel(a.R, b.R, k), Channel(a.G, b.G, k), Channel(a.B, b.B, k));

    private static byte Channel(byte a, byte b, double k) => (byte)Math.Round(a + (b - a) * k, MidpointRounding.AwayFromZero);

    private static double Luminance(Color colour) => 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);

    private static double Linear(byte channel)
    {
        var s = channel / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static Color[] Parse(params string[] hex) => hex.Select(Avalonia.Media.Color.Parse).ToArray();

    private static IBrush Build(Color[] stops) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = [.. stops.Select((colour, i) => new GradientStop(colour, i / (double)(stops.Length - 1)))],
    };
}
