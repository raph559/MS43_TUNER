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
    /// <summary>Below this height a plot is a preview (the inspector's): first and last values only, and less room kept for them.</summary>
    public const double CompactHeight = 260;

    /// <summary>Room kept on each side for the scales: values and units beside the floor, the height scale beside the volume.</summary>
    public static double MarginX(Size size) => size.Height < CompactHeight ? 24 : 36;

    public static double MarginY(Size size) => size.Height < CompactHeight ? 12 : 18;

    /// <summary>
    /// Projects a point of the unit cube (u = column, w = row, h = height, all 0..1) into <paramref name="size"/>. The
    /// picture turns about the centre of the whole volume (floor and full height), which stays at the plot's centre at any
    /// turn and tilt; zoom scales about that point and <paramref name="pan"/> then moves the picture. Depth grows towards
    /// the viewer.
    /// </summary>
    public static (Point Point, double Depth) Project(double u, double w, double h, double yaw, double pitch, Size size, double zoom = 1, Vector pan = default)
    {
        double x = u - 0.5, y = w - 0.5, z = h * 0.5 - 0.25;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        var xr = x * cy - y * sy;
        var yr = x * sy + y * cy;
        var scale = Scale(size) * zoom;
        var centre = Centre(size);
        return (new Point(centre.X + xr * scale + pan.X, centre.Y + (yr * cp - z * sp) * scale * 0.78 + pan.Y), yr * sp + z * cp);
    }

    /// <summary>The volume's centre on screen, unzoomed and unmoved: the plot's centre.</summary>
    public static Point Centre(Size size) => new(size.Width / 2, size.Height / 2);

    // Sized for the widest the volume ever gets: its floor diagonal (√2) across, and (√2·cos + ½·sin) × 0.78 of the tilt
    // at most 1.17 down, so turning or tilting never changes its size or pushes it out of the plot.
    public static double Scale(Size size) => Math.Max(1, Math.Min((size.Width - 2 * MarginX(size)) / Math.Sqrt(2), (size.Height - 2 * MarginY(size)) / 1.17));
}

/// <summary>
/// The map as a shaded surface in a Pane box: heights and colours over the working range, cells at the storage
/// limit left out. It is handled like an object in the hand: a drag moves its near side with the pointer (the
/// arrow keys do the same), the wheel or Ctrl + and Ctrl - zoom towards the pointer, Shift-drag or a right-button drag moves
/// it, and Home puts it back. It also tunes: a click on a point selects that cell (Shift-click a range, the grid follows),
/// + and - step the selection as the grid does, and Ctrl-drag raises or lowers it in one undo step on release.
/// </summary>
public sealed class Surface3D : Control
{
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<Surface3D, TableEditorViewModel?>(nameof(Editor));

    /// <summary>What the surface is showing a hand on: the selected points (where, and their values), or a Ctrl-drag's preview.</summary>
    public static readonly DirectProperty<Surface3D, string?> InfoTextProperty =
        AvaloniaProperty.RegisterDirect<Surface3D, string?>(nameof(InfoText), surface => surface.InfoText);

    private string? _infoText;

    public string? InfoText { get => _infoText; private set => SetAndRaise(InfoTextProperty, ref _infoText, value); }

    public const double MinZoom = 0.5;
    public const double MaxZoom = 4;

    private const double StartYaw = -0.72;
    private const double StartPitch = 0.6;
    private const double MinPitch = 0.15;
    private const double MaxPitch = 1.35;
    private const double TurnPerPixel = 0.01;
    private const double ZoomStep = 1.2;

    /// <summary>How far a Ctrl-drag moves for one step of the selected cells.</summary>
    public const double PixelsPerStep = 6;

    private const double PickRadius = 10;
    private const double ClickSlop = 3;

    private double _yaw = StartYaw;
    private double _pitch = StartPitch;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragFrom;
    private bool _dragMoves;
    private bool _dragEdits;
    private bool _dragTravelled;
    private int _pendingSteps;
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
        var ratio = SurfaceMath.Scale(other.Bounds.Size) is > 1 and var from ? SurfaceMath.Scale(size) / from : 1;
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

    public void TurnBy(double yaw)
    {
        _yaw += yaw;
        InvalidateVisual();
    }

    /// <summary>The steps a Ctrl-drag would apply if released now (up is positive); 0 when none is under way.</summary>
    public int PendingSteps => _pendingSteps;

    /// <summary>The floor corner (u, w) the height scale stands on: the one furthest left on screen, outside the surface.</summary>
    public (double U, double W) HeightAxisCorner
    {
        get
        {
            (double U, double W)[] corners = [(0, 0), (1, 0), (0, 1), (1, 1)];
            return corners.MinBy(corner => Project(corner.U, corner.W, 0).Point.X);
        }
    }

    /// <summary>Where a cell's point is drawn, or null for a cell the surface leaves out (storage limit, unreadable).</summary>
    public Point? PointOf(int row, int col)
    {
        if (Editor is not { Values: { Rows: > 1, Cols: > 1 } values } editor || !IsDrawn(editor, values, row, col))
        {
            return null;
        }

        return Project((double)col / (values.Cols - 1), (double)row / (values.Rows - 1), HeightOf(editor, values, row, col)).Point;
    }

    private (Point Point, double Depth) Project(double u, double w, double h) => SurfaceMath.Project(u, w, h, _yaw, _pitch, Bounds.Size, _zoom, _pan);

    private static bool IsDrawn(TableEditorViewModel editor, TableValues values, int row, int col) =>
        row >= 0 && col >= 0 && row < values.Rows && col < values.Cols && double.IsFinite(values.Physical[row, col]) && !editor.ColourRange.Hatched.Contains((row, col));

    private static double HeightOf(TableEditorViewModel editor, TableValues values, int row, int col)
    {
        var range = editor.ColourRange;
        return range.IsConstant ? 0.5 : Math.Clamp((values.Physical[row, col] - range.Min) / (range.Max - range.Min), 0, 1);
    }

    // The cell whose point is nearest the pointer, within reach; of two at the same spot, the one in front.
    private (int Row, int Col)? Pick(Point at)
    {
        if (Editor is not { Values: { Rows: > 1, Cols: > 1 } values } editor)
        {
            return null;
        }

        (int Row, int Col)? best = null;
        var bestScore = double.MaxValue;
        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                if (!IsDrawn(editor, values, r, c))
                {
                    continue;
                }

                var (point, depth) = Project((double)c / (values.Cols - 1), (double)r / (values.Rows - 1), HeightOf(editor, values, r, c));
                var distance = Point.Distance(point, at);
                var score = distance - depth * 4;
                if (distance <= PickRadius && score < bestScore)
                {
                    (best, bestScore) = ((r, c), score);
                }
            }
        }

        return best;
    }

    // + and - step the selected cells as the grid's keys do (up raises the shown value); refused on a read-only map.
    private bool Step(int steps)
    {
        if (Editor is not { Selection: not null, IsReadOnly: false } editor || steps == 0)
        {
            return false;
        }

        editor.Apply(new EditRequest(EditKind.Step, steps));
        return true;
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
        _dragTravelled = false;
        _dragEdits = point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control) && Editor is { Selection: not null, IsReadOnly: false };
        _dragMoves = !_dragEdits && (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _pendingSteps = 0;
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

    /// <summary>The scale texts drawn last: breakpoint values, height values and units.</summary>
    public IReadOnlyList<string> ScaleLabels => _scaleLabels;

    private readonly List<string> _scaleLabels = new();

    private readonly List<Rect> _labelBoxes = new();

    // Indices of the breakpoints to label: every step-th from the first, at least 40 px apart, and always the last; a label
    // that would crowd the last one gives way to it.
    public static IReadOnlyList<int> Ticks(int count, double length)
    {
        if (count <= 1)
        {
            return count == 1 ? new[] { 0 } : Array.Empty<int>();
        }

        var fits = Math.Clamp((int)(length / 40) + 1, 2, count);
        var step = (int)Math.Ceiling((count - 1) / (double)(fits - 1));
        var ticks = Enumerable.Range(0, (count - 1) / step + 1).Select(i => i * step).ToList();
        if (ticks[^1] != count - 1)
        {
            if (count - 1 - ticks[^1] <= step / 2.0 && ticks.Count > 1)
            {
                ticks.RemoveAt(ticks.Count - 1);
            }

            ticks.Add(count - 1);
        }

        return ticks;
    }

    private static string WithUnits(string text, string units) => units.Length > 0 && units != "-" ? $"{text} {units}" : text;

    private static byte Shade(byte value, double factor) => (byte)Math.Clamp(Math.Round(value * factor), 0, 255);

    private static void Label(DrawingContext context, string text, Typeface font, IBrush brush, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
