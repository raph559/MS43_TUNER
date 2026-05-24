using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Theme;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The table editor (system 2.4): cells coloured on the map ramp over the working range, axis headers that stay in
/// place while the grid scrolls, mouse and keyboard selection, typing to set values, + and - to step by one raw count,
/// copy and paste, and the live cursor.
/// </summary>
public sealed class MapGrid : Control
{
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<MapGrid, TableEditorViewModel?>(nameof(Editor));

    public static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(450);

    private const double RowHeaderWidth = 48;
    private const double TitleHeight = 22;
    private const double HeaderHeight = 22;
    private const double Gap = 2;
    private const double Radius = 4;
    private const double MaxRow = 26;
    private const double MinRow = 20;
    private const double MaxCell = 86;
    private const double CurveCell = 72;

    private readonly DispatcherTimer _flashTimer;
    private double _cellWidth = 48;
    private double _rowHeight = MaxRow;
    private (int Row, int Col) _anchor;
    private bool _dragging;
    private string _typed = string.Empty;
    private IReadOnlyList<(int Row, int Col)> _flashCells = Array.Empty<(int, int)>();
    private DateTime _flashStart;
    private ScrollViewer? _viewport;
    private TableLayout? _layout;

    public MapGrid()
    {
        Focusable = true;
        ClipToBounds = true;

        // The focus ring is drawn around the active cell, not around the whole grid.
        FocusAdorner = null;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        _flashTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (_, _) =>
        {
            if (DateTime.UtcNow - _flashStart > FlashTime)
            {
                _flashTimer!.Stop();
                _flashCells = Array.Empty<(int, int)>();
            }

            InvalidateVisual();
        });
    }

    public TableEditorViewModel? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    /// <summary>True while edited cells are fading from their text colour to their new fill.</summary>
    public bool IsFlashing => _flashCells.Count > 0;

    private TableValues? Values => Editor?.Values;

    private Vector Scroll => _viewport?.Offset ?? default;

    public Rect CellBounds(int row, int col) =>
        new(RowHeaderWidth + Gap + col * (_cellWidth + Gap), HeaderHeight + Gap + row * (_rowHeight + Gap), _cellWidth, _rowHeight);

    /// <summary>A row's header; it stays at the visible left edge while the grid scrolls sideways.</summary>
    public Rect RowHeaderBounds(int row) => new(Scroll.X, HeaderHeight + Gap + row * (_rowHeight + Gap), RowHeaderWidth, _rowHeight);

    /// <summary>A column's header; it stays at the visible top while the grid scrolls down.</summary>
    public Rect ColumnHeaderBounds(int col) => new(RowHeaderWidth + Gap + col * (_cellWidth + Gap), Scroll.Y, _cellWidth, HeaderHeight);

    /// <summary>A fractional cell position (row 5.5 = halfway between the centres of rows 5 and 6) in control coordinates.</summary>
    public Point CursorPoint(double row, double col)
    {
        var r = (int)Math.Floor(row);
        var c = (int)Math.Floor(col);
        var centre = CellBounds(r, c).Center;
        return new Point(centre.X + (col - c) * (_cellWidth + Gap), centre.Y + (row - r) * (_rowHeight + Gap));
    }

    public bool TryHitCell(Point point, out int row, out int col)
    {
        row = col = -1;
        var scroll = Scroll;
        if (Values is not TableValues values || point.X < scroll.X + RowHeaderWidth + Gap || point.Y < scroll.Y + HeaderHeight + Gap)
        {
            return false;
        }

        col = (int)((point.X - RowHeaderWidth - Gap) / (_cellWidth + Gap));
        row = (int)((point.Y - HeaderHeight - Gap) / (_rowHeight + Gap));
        return row >= 0 && row < values.Rows && col >= 0 && col < values.Cols;
    }

    /// <summary>The exact text drawn in a cell: the raw value, the B - A difference, or the value with an ASCII minus.</summary>
    public string CellText(int row, int col)
    {
        if (Editor is not TableEditorViewModel editor)
        {
            return string.Empty;
        }

        var values = editor.Values;
        var z = values.Table.Z;
        var physical = values.Physical[row, col];
        return editor.IsDelta ? BinComparer.FormatDelta(physical, values.Raw[row, col], z, editor.ShowRaw)
            : editor.ShowRaw ? values.Raw[row, col].ToString(CultureInfo.InvariantCulture)
            : ValueCodec.Format(physical, values.Raw[row, col], z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
    }

    /// <summary>
    /// The fill of a just-edited cell (system 10.3): frame 0 is the cell's final text colour, so start and end differ by
    /// 4.58:1 or more, easing out (cubic) to the new fill.
    /// </summary>
    public static Color FlashColour(Color textColour, Color finalFill, double t)
    {
        var eased = 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
        byte Mix(byte from, byte to) => (byte)Math.Round(from + eased * (to - from), MidpointRounding.AwayFromZero);
        return Color.FromRgb(Mix(textColour.R, finalFill.R), Mix(textColour.G, finalFill.G), Mix(textColour.B, finalFill.B));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is TableEditorViewModel old)
            {
                old.PropertyChanged -= OnEditorChanged;
                old.Edited -= OnEdited;
                old.TypedText = string.Empty;
            }

            if (change.NewValue is TableEditorViewModel editor)
            {
                editor.PropertyChanged += OnEditorChanged;
                editor.Edited += OnEdited;
            }

            _flashCells = Array.Empty<(int, int)>();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Values is not TableValues values)
        {
            return default;
        }

        // Inside a scroll viewer the space is unbounded: fit the viewport. Columns never get narrower than their
        // widest number plus 8 px (the grid scrolls sideways instead); rows shrink from 26 to 20 px, then it scrolls down.
        var (width, height) = Viewport(availableSize);
        var narrowest = WidestText(values) + 8;
        _cellWidth = values.Cols == 1
            ? Math.Max(CurveCell, narrowest)
            : Math.Max(narrowest, Math.Min(MaxCell, (width - RowHeaderWidth - Gap) / values.Cols - Gap));
        _rowHeight = double.IsFinite(height) && height > 0
            ? Math.Clamp((height - HeaderHeight - Gap) / values.Rows - Gap, MinRow, MaxRow)
            : MaxRow;
        return new Size(RowHeaderWidth + values.Cols * (_cellWidth + Gap), HeaderHeight + values.Rows * (_rowHeight + Gap));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewport = this.FindAncestorOfType<ScrollViewer>();
        _layout = this.FindAncestorOfType<TableLayout>();
        if (_viewport is not null)
        {
            _viewport.SizeChanged += OnViewportSizeChanged;
            _viewport.ScrollChanged += OnViewportScrolled;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewport is not null)
        {
            _viewport.SizeChanged -= OnViewportSizeChanged;
            _viewport.ScrollChanged -= OnViewportScrolled;
            _viewport = null;
        }

        _layout = null;
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e) => InvalidateMeasure();

    // The headers are drawn at the scroll offset, so they move back into view on every scroll.
    private void OnViewportScrolled(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private (double Width, double Height) Viewport(Size available)
    {
        // In the Tune pane the layout says how much room the scroll viewer may take: the grid fits that, not its own
        // content-sized viewport, which would shrink the rows a little more on every pass.
        if (_layout is { Fit: var fit } && double.IsFinite(fit.Width) && double.IsFinite(fit.Height))
        {
            var padding = _viewport?.Padding ?? default;
            var width = fit.Width - padding.Left - padding.Right;
            return (double.IsInfinity(MaxWidth) ? width : Math.Min(width, MaxWidth), fit.Height - padding.Top - padding.Bottom);
        }

        if (_viewport is { } viewport && viewport.Bounds.Width > 0)
        {
            var width = viewport.Bounds.Width - viewport.Padding.Left - viewport.Padding.Right;
            var height = viewport.Bounds.Height - viewport.Padding.Top - viewport.Padding.Bottom;
            return (double.IsInfinity(MaxWidth) ? width : Math.Min(width, MaxWidth), height);
        }

        return (double.IsInfinity(available.Width) ? 0 : available.Width, available.Height);
    }

    private double WidestText(TableValues values)
    {
        var face = new Typeface(ThemeBrushes.Font(this, "DataFont"));
        var widest = 0.0;
        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                widest = Math.Max(widest, Measure(CellText(r, c), face).Width);
            }
        }

        for (var c = 0; c < values.Cols; c++)
        {
            widest = Math.Max(widest, Measure(ColumnLabel(values, c), face).Width);
        }

        return widest;
    }

    public override void Render(DrawingContext context)
    {
        if (Values is not TableValues values || Editor is not TableEditorViewModel editor)
        {
            return;
        }

        var display = ThemeBrushes.Font(this, "DisplayFont");
        var ui = ThemeBrushes.Font(this, "UiFont");
        var ink = ThemeBrushes.Get(this, "InkBrush");
        var ink3 = ThemeBrushes.Get(this, "Ink3Brush");
        var tickPen = new Pen(ThemeBrushes.Get(this, "Bezel2Brush"), 1);

        // Axis titles: the column axis above the grid, the row axis in the corner.
        // A one-column curve has no column axis: its values carry the item's units instead.
        var curve = values.Cols == 1;
        Text(context, curve ? CurveUnits(values) : $"{Axis(values.X)} →", ui, 12.5, ink3, new Point(RowHeaderWidth, 2), 0);
        Text(context, values.Y.Units.Length > 0 ? values.Y.Units : "row", ui, 11.5, ink3, new Point(RowHeaderWidth - 10, TitleHeight + HeaderHeight - 17), 1);

        for (var c = 0; c < values.Cols; c++)
        {
            var cell = CellBounds(0, c);
            if (!curve)
            {
                Text(context, Label(values.X, c), display, 12, ink3, new Point(cell.Center.X, TitleHeight + 4), 0.5);
                context.DrawLine(tickPen, new Point(cell.Center.X, TitleHeight + HeaderHeight - 6), new Point(cell.Center.X, TitleHeight + HeaderHeight - 2));
            }
        }

        for (var r = 0; r < values.Rows; r++)
        {
            var cell = CellBounds(r, 0);
            Text(context, Label(values.Y, r), display, 12, ink3, new Point(RowHeaderWidth - 12, cell.Center.Y - 8), 1);
            context.DrawLine(tickPen, new Point(RowHeaderWidth - 6, cell.Center.Y), new Point(RowHeaderWidth - 2, cell.Center.Y));
        }

        var finite = values.Physical.Cast<double>().Where(double.IsFinite).ToList();
        var min = finite.Count > 0 ? finite.Min() : 0;
        var max = finite.Count > 0 ? finite.Max() : 1;
        var largest = finite.Count > 0 ? finite.Max(Math.Abs) : 0;
        var (middle, down, up) = (ThemeBrushes.Color(this, "PaneBrush"), ThemeBrushes.Color(this, "DownBrush"), ThemeBrushes.Color(this, "UpBrush"));
        var flash = _flashCells.Count > 0 ? Math.Max(0, 1 - (DateTime.UtcNow - _flashStart).TotalMilliseconds / FlashTime.TotalMilliseconds) : 0;
        var z = values.Table.Z;

        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                var bounds = CellBounds(r, c);
                var physical = values.Physical[r, c];
                var colour = !double.IsFinite(physical) ? ThemeBrushes.Color(this, "RaisedBrush")
                    : editor.IsDelta ? HeatRamp.Diverging(largest > 0 ? physical / largest : 0, middle, down, up)
                    : HeatRamp.Color(max > min ? (physical - min) / (max - min) : 0.5);
                var text = new SolidColorBrush(HeatRamp.TextOn(colour));
                context.DrawRectangle(new SolidColorBrush(colour), null, bounds, 3, 3);
                if (flash > 0 && _flashCells.Contains((r, c)))
                {
                    context.DrawRectangle(new SolidColorBrush(Colors.White, 0.5 * flash), null, bounds, 3, 3);
                }

                var shown = editor.IsDelta ? BinComparer.FormatDelta(physical, values.Raw[r, c], z, editor.ShowRaw)
                    : editor.ShowRaw
                    ? values.Raw[r, c].ToString(CultureInfo.InvariantCulture)
                    : ValueCodec.Format(physical, values.Raw[r, c], z.DecimalPlaces, z.OutputType, z.Data.ElementBits).Replace('-', '−');
                Text(context, shown, display, 12.5, text, new Point(bounds.Center.X, bounds.Center.Y - 8.5), 0.5);

                if (editor.ChangedCells.Contains((r, c)))
                {
                    context.DrawEllipse(text, null, new Point(bounds.Right - 5, bounds.Top + 5), 2, 2);
                }
            }
        }

        if (editor.Selection is CellRange selection)
        {
            var first = CellBounds(selection.Row0, selection.Col0);
            var last = CellBounds(selection.Row1, selection.Col1);
            var box = new Rect(first.TopLeft, last.BottomRight).Inflate(2);
            context.DrawRectangle(null, new Pen(ThemeBrushes.Get(this, "DialBrush"), 3), box, 4, 4);
            context.DrawRectangle(null, new Pen(ink, 2), box, 4, 4);

            if (_typed.Length > 0)
            {
                var anchor = CellBounds(_anchor.Row, _anchor.Col);
                context.DrawRectangle(ThemeBrushes.Get(this, "RaisedBrush"), new Pen(ThemeBrushes.Get(this, "NeedleBrush"), 2), anchor, 3, 3);
                Text(context, _typed + "▏", display, 12.5, ink, new Point(anchor.Center.X, anchor.Center.Y - 8.5), 0.5);
            }
        }

        if (editor.Live is { } live)
        {
            DrawLive(context, live, values);
        }
    }

    /// <summary>The engine's position: crosshair, the cells the ECU reads outlined by weight, the fading trail and a dot at the fix.</summary>
    private void DrawLive(DrawingContext context, LiveCursorState live, TableValues values)
    {
        var colour = ThemeBrushes.Cursor(this, live.IsStale);
        Pen PenOf(double thickness, double opacity) => new(new SolidColorBrush(colour, opacity), thickness);
        var fix = live.Fix;
        var at = CursorPoint(fix.Row?.Position ?? 0, fix.Col?.Position ?? 0);
        var first = CellBounds(0, 0);
        var last = CellBounds(values.Rows - 1, values.Cols - 1);
        var hair = PenOf(1, 0.7);
        if (values.Cols > 1)
        {
            context.DrawLine(hair, new Point(at.X, first.Top), new Point(at.X, last.Bottom));
        }

        if (values.Rows > 1)
        {
            context.DrawLine(hair, new Point(first.Left, at.Y), new Point(last.Right, at.Y));
        }

        foreach (var cell in fix.Cells)
        {
            context.DrawRectangle(null, PenOf(1 + 2 * cell.Weight, 0.35 + 0.65 * cell.Weight), CellBounds(cell.Row, cell.Col).Inflate(1), 4, 4);
        }

        Point? previous = null;
        foreach (var point in live.Trail)
        {
            var p = CursorPoint(point.Row, point.Col);
            if (previous is Point from)
            {
                context.DrawLine(PenOf(1, point.Opacity * 0.5), from, p);
            }

            context.DrawEllipse(new SolidColorBrush(colour, point.Opacity), null, p, 2.5, 2.5);
            previous = p;
        }

        context.DrawEllipse(new SolidColorBrush(colour), new Pen(ThemeBrushes.Get(this, "DialBrush"), 1.5), at, 4, 4);
    }

    // 3 px Now bars in the header gutters at the cursor's position, over the sticky headers.
    private void DrawTicks(DrawingContext context, LiveCursorState live, TableValues values)
    {
        var now = new SolidColorBrush(ThemeBrushes.Color(this, "NowBrush"));
        var at = Clamped(live.Fix, values);
        var scroll = Scroll;
        if (values.Cols > 1)
        {
            context.DrawRectangle(now, null, new Rect(at.X - 1, scroll.Y + HeaderHeight - 3, 3, 3 + Gap));
        }

        if (values.Rows > 1)
        {
            context.DrawRectangle(now, null, new Rect(scroll.X + RowHeaderWidth - 3, at.Y - 1, 3 + Gap, 3));
        }
    }

    private Point Clamped(CursorFix fix, TableValues values)
    {
        var at = CursorPoint(fix.Row?.Position ?? 0, fix.Col?.Position ?? 0);
        var first = CellBounds(0, 0);
        var last = CellBounds(values.Rows - 1, values.Cols - 1);
        var row = fix.Row?.Clamped ?? 0;
        var col = fix.Col?.Clamped ?? 0;
        return new Point(col < 0 ? first.Left : col > 0 ? last.Right : at.X, row < 0 ? first.Top : row > 0 ? last.Bottom : at.Y);
    }

    // Storage limit (system 2.2): 1 px Fill lines every 4 px at 45 degrees on Pane, inside the cell's shape.
    private static void Hatch(DrawingContext context, RoundedRect shape, Color fill)
    {
        var bounds = shape.Rect;
        var pen = new Pen(new SolidColorBrush(fill), 1);
        using (context.PushClip(shape))
        {
            var step = 4 * Math.Sqrt(2);
            for (var x = bounds.Left - bounds.Height; x < bounds.Right; x += step)
            {
                context.DrawLine(pen, new Point(x, bounds.Bottom), new Point(x + bounds.Height, bounds.Top));
            }
        }
    }

    // Changed: a 6 px right-angle triangle 2 px inside the top-right corner. Differs: a 6 px ring there (1.5 px).
    private static void Mark(DrawingContext context, CellMark mark, Rect cell, Color colour)
    {
        var brush = new SolidColorBrush(colour);
        var (right, top) = (cell.Right - 2, cell.Top + 2);
        if (mark == CellMark.Differs)
        {
            context.DrawEllipse(null, new Pen(brush, 1.5), new Point(right - 3, top + 3), 2.25, 2.25);
            return;
        }

        var triangle = new StreamGeometry();
        using (var g = triangle.Open())
        {
            g.BeginFigure(new Point(right - 6, top), true);
            g.LineTo(new Point(right, top));
            g.LineTo(new Point(right, top + 6));
            g.EndFigure(true);
        }

        context.DrawGeometry(brush, null, triangle);
    }

    // The ring round the active cell shows and hides with the focus, which changes nothing else the grid renders from.
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(NavigationMethod.Pointer);
        if (Editor is not TableEditorViewModel editor || !TryHitCell(e.GetPosition(this), out var row, out var col))
        {
            return;
        }

        editor.TypedText = string.Empty;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && editor.Selection is not null)
        {
            editor.Selection = new CellRange(_anchor.Row, _anchor.Col, row, col);
        }
        else
        {
            _anchor = (row, col);
            editor.Selection = new CellRange(row, col, row, col);
        }

        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && Editor is TableEditorViewModel editor && TryHitCell(e.GetPosition(this), out var row, out var col))
        {
            editor.Selection = new CellRange(_anchor.Row, _anchor.Col, row, col);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (Editor is not TableEditorViewModel editor || e.Text is not { Length: > 0 } text)
        {
            return;
        }

        if (editor.IsReadOnly)
        {
            editor.Message = TableEditorViewModel.ReadOnlyMessage;
            e.Handled = true;
            return;
        }

        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch is '.' or ',')
            {
                editor.TypedText += ch == ',' ? '.' : ch;
            }
            else if (ch is '+' or '=' && editor.TypedText.Length == 0)
            {
                editor.Apply(new EditRequest(EditKind.Step, 1));
            }
            else if (ch is '-' && editor.TypedText.Length == 0)
            {
                editor.Apply(new EditRequest(EditKind.Step, -1));
            }
        }

        e.Handled = true;
        InvalidateVisual();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Editor is not TableEditorViewModel editor || Values is not TableValues values)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.C && editor.Selection is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } copyTo)
        {
            await copyTo.SetTextAsync(editor.Copy());
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.V && TopLevel.GetTopLevel(this)?.Clipboard is { } pasteFrom)
        {
            if (await pasteFrom.TryGetTextAsync() is { } text)
            {
                editor.Apply(new EditRequest(EditKind.Paste, Text: text));
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when editor.TypedText.Length > 0:
                if (double.TryParse(editor.TypedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    editor.Apply(new EditRequest(EditKind.Set, value));
                }

                editor.TypedText = string.Empty;
                break;
            case Key.Escape when editor.TypedText.Length > 0:
                editor.TypedText = string.Empty;
                break;
            case Key.Back when editor.TypedText.Length > 0:
                editor.TypedText = editor.TypedText[..^1];
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right:
                Move(editor, values, e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            default:
                return;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    private void Move(TableEditorViewModel editor, TableValues values, Key key, bool extend)
    {
        var selection = editor.Selection ?? new CellRange(0, 0, 0, 0);

        // The moving corner is the one away from the anchor.
        var focus = (Row: selection.Row0 == _anchor.Row ? selection.Row1 : selection.Row0, Col: selection.Col0 == _anchor.Col ? selection.Col1 : selection.Col0);
        var (dr, dc) = key switch { Key.Up => (-1, 0), Key.Down => (1, 0), Key.Left => (0, -1), _ => (0, 1) };
        var next = (Row: Math.Clamp(focus.Row + dr, 0, values.Rows - 1), Col: Math.Clamp(focus.Col + dc, 0, values.Cols - 1));
        if (!extend)
        {
            _anchor = next;
        }

        editor.Selection = new CellRange(_anchor.Row, _anchor.Col, next.Row, next.Col);
        editor.TypedText = string.Empty;

        // The moving corner stays in sight, clear of the sticky headers, so a value is never typed into a hidden cell.
        var cell = CellBounds(next.Row, next.Col);
        this.BringIntoView(new Rect(cell.X - RowHeaderWidth - Gap, cell.Y - HeaderHeight - Gap, cell.Width + RowHeaderWidth + Gap, cell.Height + HeaderHeight + Gap));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableEditorViewModel.Values) or nameof(TableEditorViewModel.ShowRaw))
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void OnEdited(object? sender, IReadOnlyList<(int Row, int Col)> cells)
    {
        // With Windows' animation effects off there is no flash: the changed triangle and the key line confirm the edit.
        if (!MotionSettings.AnimationsEnabled)
        {
            return;
        }

        _flashCells = cells;
        _flashStart = DateTime.UtcNow;
        _flashTimer.Start();
        InvalidateVisual();
    }

    private static string CurveUnits(TableValues values) => values.Table.Z.Units.Length > 0 ? values.Table.Z.Units : string.Empty;

    private static string Axis(AxisValues axis) =>
        string.Join(", ", new[] { axis.Name, axis.Units }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Label(AxisValues axis, int index) =>
        index < axis.Labels.Count ? axis.Labels[index].Replace('-', '−') : string.Empty;

    /// <summary>Draws text with its top at <paramref name="at"/>.Y; <paramref name="align"/> 0 = left, 0.5 = centre, 1 = right of <paramref name="at"/>.X.</summary>
    private static void Text(DrawingContext context, string text, FontFamily family, double size, IBrush brush, Point at, double align)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family), size, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width * align, at.Y));
    }

    // A one-column table shows its single column's unit as the header (the value's units, else the axis's).
    private static string ColumnLabel(TableValues values, int col) =>
        values.Cols == 1 ? (values.Table.Z.Units.Length > 0 ? values.Table.Z.Units : values.X.Units)
        : col < values.X.Labels.Count ? values.X.Labels[col] : string.Empty;

    private static FormattedText Formatted(string text, Typeface face, double size, Color colour)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(colour));
        formatted.SetFontFeatures(Typography.Tabular);
        return formatted;
    }

    private static Size Measure(string text, Typeface face)
    {
        var formatted = Formatted(text, face, 12, Colors.Black);
        return new Size(formatted.Width, formatted.Height);
    }
}
