using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

public static class SurfaceMath
{
    /// <summary>
    /// Projects a point of the unit cube (u = column, w = row, h = height, all 0..1) into <paramref name="size"/>.
    /// The centre of the floor stays at (width / 2, height × 0.58) whatever the rotation. Depth grows towards the viewer.
    /// </summary>
    public static (Point Point, double Depth) Project(double u, double w, double h, double yaw, double pitch, Size size)
    {
        double x = u - 0.5, y = w - 0.5, z = h * 0.5;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        var xr = x * cy - y * sy;
        var yr = x * sy + y * cy;
        var scale = Math.Min(size.Width * 0.86, size.Height * 1.18);
        return (new Point(size.Width / 2 + xr * scale, size.Height * 0.58 + (yr * cp - z * sp) * scale * 0.78), yr * sp + z * cp);
    }
}

/// <summary>The map as a shaded surface on a floor grid; drag to turn it.</summary>
public sealed class Surface3D : Control
{

    private double _yaw = -0.72;
    private double _pitch = 0.6;
    private Point? _dragFrom;
    private (double Yaw, double Pitch) _dragStart;

    public Surface3D()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragFrom = e.GetPosition(this);
        _dragStart = (_yaw, _pitch);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is Point from)
        {
            var now = e.GetPosition(this);
            _yaw = _dragStart.Yaw + (now.X - from.X) * 0.01;
            _pitch = Math.Clamp(_dragStart.Pitch + (now.Y - from.Y) * 0.01, 0.15, 1.35);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    private static byte Shade(byte value, double factor) => (byte)Math.Clamp(Math.Round(value * factor), 0, 255);

    private static void Label(DrawingContext context, string text, Typeface font, IBrush brush, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
