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
}
