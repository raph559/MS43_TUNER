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
    /// Unzoomed and unmoved, the centre of the floor stays at (width / 2, height × 0.58) whatever the rotation; zoom
    /// scales about that point and <paramref name="pan"/> then moves the picture. Depth grows towards the viewer.
    /// </summary>
    public static (Point Point, double Depth) Project(double u, double w, double h, double yaw, double pitch, Size size, double zoom = 1, Vector pan = default)
    {
        double x = u - 0.5, y = w - 0.5, z = h * 0.5;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        var xr = x * cy - y * sy;
        var yr = x * sy + y * cy;
        var scale = Math.Min(size.Width * 0.86, size.Height * 1.18) * zoom;
        var centre = Centre(size);
        return (new Point(centre.X + xr * scale + pan.X, centre.Y + (yr * cp - z * sp) * scale * 0.78 + pan.Y), yr * sp + z * cp);
    }

    /// <summary>Where the floor's centre sits, unzoomed and unmoved.</summary>
    public static Point Centre(Size size) => new(size.Width / 2, size.Height * 0.58);
}

/// <summary>
/// The map as a shaded surface in a Pane box (system 8): heights and colours over the working range, cells at the storage
/// limit left out (system 2.2). It is handled like an object in the hand: a drag moves its near side with the pointer (the
/// arrow keys do the same), the wheel or + and - zoom towards the pointer, Shift-drag or a right-button drag moves it, and
/// Home puts it back.
/// </summary>
public sealed class Surface3D : Control
{
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<Surface3D, TableEditorViewModel?>(nameof(Editor));

    public const double MinZoom = 0.5;
    public const double MaxZoom = 4;

    private const double StartYaw = -0.72;
    private const double StartPitch = 0.6;
    private const double MinPitch = 0.15;
    private const double MaxPitch = 1.35;
    private const double TurnPerPixel = 0.01;
    private const double ZoomStep = 1.2;

    private double _yaw = StartYaw;
    private double _pitch = StartPitch;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragFrom;
    private bool _dragMoves;
    private (double Yaw, double Pitch, Vector Pan) _dragStart;

    public Surface3D()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public TableEditorViewModel? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    /// <summary>How many cells the plot draws: all but those hatched at the storage limit.</summary>
    public int IncludedCellCount => Editor is { Values: { Rows: > 1, Cols: > 1 } values } editor
        ? values.Rows * values.Cols - editor.ColourRange.Hatched.Count
        : 0;

    public double Yaw => _yaw;

    public double Pitch => _pitch;

    public double Zoom => _zoom;

    /// <summary>How far the picture is moved, in pixels.</summary>
    public Vector Pan => _pan;

    /// <summary>
    /// Takes the turn, zoom and position of <paramref name="other"/> (the enlarged view opens where the small one was).
    /// The position is in pixels, so it grows with the plot: <paramref name="size"/> is this plot's size.
    /// </summary>
    public void CopyView(Surface3D other, Size size)
    {
        ArgumentNullException.ThrowIfNull(other);
        static double Scale(Size s) => Math.Min(s.Width * 0.86, s.Height * 1.18);
        var ratio = Scale(other.Bounds.Size) is > 0 and var from ? Scale(size) / from : 1;
        (_yaw, _pitch, _zoom, _pan) = (other._yaw, other._pitch, other._zoom, other._pan * ratio);
        InvalidateVisual();
    }

    /// <summary>Zooms by <paramref name="factor"/>, keeping the point under <paramref name="at"/> (default: the floor's centre) still.</summary>
    public void ZoomBy(double factor, Point? at = null)
    {
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var anchor = at ?? SurfaceMath.Centre(Bounds.Size) + _pan;
        var centre = SurfaceMath.Centre(Bounds.Size);

        // A point now at p = centre + offset + pan lands at anchor + (p - anchor) * k.
        var k = zoom / _zoom;
        _pan = (Vector)(anchor - centre) + (_pan - (Vector)(anchor - centre)) * k;
        _zoom = zoom;
        InvalidateVisual();
    }

    public void ResetView()
    {
        (_yaw, _pitch, _zoom, _pan) = (StartYaw, StartPitch, 1, default);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is TableEditorViewModel old)
            {
                old.PropertyChanged -= OnEditorChanged;
            }

            if (change.NewValue is TableEditorViewModel editor)
            {
                editor.PropertyChanged += OnEditorChanged;
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Left and Right turn the near side that way, as a drag would; Up and Down tip it.
        switch (e.Key)
        {
            case Key.Left:
                _yaw += 0.1;
                break;
            case Key.Right:
                _yaw -= 0.1;
                break;
            case Key.Up:
                _pitch = Math.Clamp(_pitch + 0.05, MinPitch, MaxPitch);
                break;
            case Key.Down:
                _pitch = Math.Clamp(_pitch - 0.05, MinPitch, MaxPitch);
                break;
            case Key.OemPlus or Key.Add:
                ZoomBy(ZoomStep);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(1 / ZoomStep);
                break;
            case Key.Home:
                ResetView();
                break;
            default:
                return;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    // The wheel zooms here instead of scrolling the inspector: the pointer is on the plot, so that is what it is about.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y != 0)
        {
            ZoomBy(Math.Pow(ZoomStep, e.Delta.Y), e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(NavigationMethod.Pointer);
        var point = e.GetCurrentPoint(this);
        _dragFrom = point.Position;
        _dragMoves = point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _dragStart = (_yaw, _pitch, _pan);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    // Grab and turn: the near side follows the pointer sideways, and down tips the near side down.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is Point from)
        {
            var moved = e.GetPosition(this) - from;
            if (_dragMoves)
            {
                _pan = _dragStart.Pan + moved;
            }
            else
            {
                _yaw = _dragStart.Yaw - moved.X * TurnPerPixel;
                _pitch = Math.Clamp(_dragStart.Pitch - moved.Y * TurnPerPixel, MinPitch, MaxPitch);
            }

            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var night = ThemeBrushes.IsNight(this);
        var pane = ThemeBrushes.Color(this, "PaneBrush");
        context.DrawRectangle(new SolidColorBrush(pane), new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1), new RoundedRect(new Rect(size).Deflate(0.5), 8));
        if (Editor is not { Values: { Rows: > 1, Cols: > 1 } values } editor)
        {
            return;
        }

        var range = editor.ColourRange;
        var hatched = range.Hatched;
        bool Drawn(int r, int c) => double.IsFinite(values.Physical[r, c]) && !hatched.Contains((r, c));
        double T(double value) => range.IsConstant ? 0.5 : Math.Clamp((value - range.Min) / (range.Max - range.Min), 0, 1);
        double H(int r, int c) => Drawn(r, c) ? T(values.Physical[r, c]) : 0;
        (Point Point, double Depth) P(double u, double w, double h) => SurfaceMath.Project(u, w, h, _yaw, _pitch, size, _zoom, _pan);

        var floor = new Pen(ThemeBrushes.Get(this, "HairlineBrush"), 1);
        for (var i = 0; i <= 8; i++)
        {
            var t = i / 8.0;
            context.DrawLine(floor, P(t, 0, 0).Point, P(t, 1, 0).Point);
            context.DrawLine(floor, P(0, t, 0).Point, P(1, t, 0).Point);
        }

        var quads = new List<(Point[] Corners, double Depth, Color Colour)>();
        for (var r = 0; r < values.Rows - 1; r++)
        {
            for (var c = 0; c < values.Cols - 1; c++)
            {
                if (!Drawn(r, c) || !Drawn(r, c + 1) || !Drawn(r + 1, c) || !Drawn(r + 1, c + 1))
                {
                    continue;
                }

                var corners = new[] { (c, r), (c + 1, r), (c + 1, r + 1), (c, r + 1) }
                    .Select(p => P((double)p.Item1 / (values.Cols - 1), (double)p.Item2 / (values.Rows - 1), H(p.Item2, p.Item1)))
                    .ToArray();

                // Light from the upper left: shade each cell by its slope.
                var dx = (H(r, c + 1) + H(r + 1, c + 1) - H(r, c) - H(r + 1, c)) / 2;
                var dy = (H(r + 1, c) + H(r + 1, c + 1) - H(r, c) - H(r, c + 1)) / 2;
                var shade = Math.Clamp(1 - dx * 2.2 + dy * 1.4, 0.55, 1.15);
                var colour = MapRamp.Color((H(r, c) + H(r, c + 1) + H(r + 1, c) + H(r + 1, c + 1)) / 4, night);
                colour = Color.FromRgb(Shade(colour.R, shade), Shade(colour.G, shade), Shade(colour.B, shade));
                quads.Add((corners.Select(p => p.Point).ToArray(), corners.Average(p => p.Depth), colour));
            }
        }

        var seam = new Pen(new SolidColorBrush(pane, 0.55), 0.5, lineJoin: PenLineJoin.Round);
        foreach (var quad in quads.OrderBy(q => q.Depth))
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(quad.Corners[0], true);
                g.LineTo(quad.Corners[1]);
                g.LineTo(quad.Corners[2]);
                g.LineTo(quad.Corners[3]);
                g.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(quad.Colour), seam, geometry);
        }

        var font = new Typeface(ThemeBrushes.Font(this, "UiFont"));
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        Label(context, values.Y.Units.Length > 0 ? values.Y.Units : "rows", font, ink3, P(-0.14, 0.5, 0).Point);
        Label(context, values.X.Units.Length > 0 ? values.X.Units : "columns", font, ink3, P(0.5, 1.13, 0).Point);

        if (editor.Live is { } live)
        {
            // The engine's position: a Now dot with a Pane halo on the surface; an Ink3 ring once samples stop.
            var fix = live.Fix;
            double fixRow = fix.Row?.Position ?? 0, fixCol = fix.Col?.Position ?? 0;
            var at = P(fixCol / (values.Cols - 1), fixRow / (values.Rows - 1), double.IsFinite(fix.Value) ? T(fix.Value) : 0).Point;
            var halo = new SolidColorBrush(pane);
            if (live.IsStale)
            {
                context.DrawEllipse(null, new Pen(halo, 5), at, 3.25, 3.25);
                context.DrawEllipse(null, new Pen(ThemeBrushes.Get(this, "Ink3Brush"), 1.5), at, 3.25, 3.25);
            }
            else
            {
                context.DrawEllipse(halo, null, at, 6, 6);
                context.DrawEllipse(ThemeBrushes.Get(this, live.IsUnused ? "Ink3Brush" : "NowBrush"), null, at, 4, 4);
            }
        }
    }

    private static byte Shade(byte value, double factor) => (byte)Math.Clamp(Math.Round(value * factor), 0, 255);

    private static void Label(DrawingContext context, string text, Typeface font, IBrush brush, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
