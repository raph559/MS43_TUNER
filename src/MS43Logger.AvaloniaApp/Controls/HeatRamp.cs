using System;
using System.Linq;
using Avalonia.Media;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>The deep-sea data ramp from the design: brighter means higher. Data colours are the same in both themes.</summary>
public static class HeatRamp
{
    private static readonly (double At, Color Colour)[] Stops =
    [
        (0.00, Avalonia.Media.Color.Parse("#0B1E33")),
        (0.18, Avalonia.Media.Color.Parse("#0F3657")),
        (0.38, Avalonia.Media.Color.Parse("#11587A")),
        (0.58, Avalonia.Media.Color.Parse("#1A8191")),
        (0.76, Avalonia.Media.Color.Parse("#4FB3A4")),
        (0.90, Avalonia.Media.Color.Parse("#A6DDC3")),
        (1.00, Avalonia.Media.Color.Parse("#F2F7EC")),
    ];

    /// <summary>The ramp as a left-to-right gradient, for legends.</summary>
    public static IBrush Brush { get; } = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
        GradientStops = [.. Stops.Select(stop => new GradientStop(stop.Colour, stop.At))],
    };

    private static readonly Color DarkText = Avalonia.Media.Color.Parse("#0B1620");
    private static readonly Color LightText = Avalonia.Media.Color.Parse("#EAF1F4");

    public static Color Color(double t)
    {
        t = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
        for (var i = 1; i < Stops.Length; i++)
        {
            if (t <= Stops[i].At)
            {
                var (a, ca) = Stops[i - 1];
                var (b, cb) = Stops[i];
                var k = (t - a) / (b - a);
                return Avalonia.Media.Color.FromRgb(Mix(ca.R, cb.R, k), Mix(ca.G, cb.G, k), Mix(ca.B, cb.B, k));
            }
        }

        return Stops[^1].Colour;
    }

    /// <summary>
    /// The B − A scale: <paramref name="middle"/> (the pane colour) at 0, <paramref name="up"/> at +1, <paramref name="down"/> at −1,
    /// mixed by |t|^0.7 so small changes still show.
    /// </summary>
    public static Color Diverging(double t, Color middle, Color down, Color up)
    {
        t = double.IsNaN(t) ? 0 : Math.Clamp(t, -1, 1);
        var end = t >= 0 ? up : down;
        var k = Math.Pow(Math.Abs(t), 0.7);
        return Avalonia.Media.Color.FromRgb(Mix(middle.R, end.R, k), Mix(middle.G, end.G, k), Mix(middle.B, end.B, k));
    }

    /// <summary>Dark text on light cells, light text on dark cells.</summary>
    public static Color TextOn(Color background) =>
        0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B > 135 ? DarkText : LightText;

    private static byte Mix(byte a, byte b, double k) => (byte)Math.Round(a + (b - a) * k);
}
