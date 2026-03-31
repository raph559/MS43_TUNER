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
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Controls;

/// <summary>
/// The table editor: a heatmap mosaic with axis headers, mouse and keyboard selection, typing to set values,
/// + and − to step by one raw count, and copy/paste.
/// </summary>
public sealed class MapGrid : Control
{
    public static readonly StyledProperty<TableEditorViewModel?> EditorProperty = AvaloniaProperty.Register<MapGrid, TableEditorViewModel?>(nameof(Editor));

    private const double RowHeaderWidth = 56;
    private const double TitleHeight = 22;
    private const double HeaderHeight = 26;
    private const double CellHeight = 28;
    private const double Gap = 2;
    private const double MinCell = 42;
    private const double MaxCell = 86;
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(450);

    private readonly DispatcherTimer _flashTimer;
    private double _cellWidth = MinCell;
    private (int Row, int Col) _anchor;
    private bool _dragging;
    private string _typed = string.Empty;
    private IReadOnlyList<(int Row, int Col)> _flashCells = Array.Empty<(int, int)>();
    private DateTime _flashStart;

    public MapGrid()
    {
        Focusable = true;
        ClipToBounds = true;
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

    private TableValues? Values => Editor?.Values;

    public Rect CellBounds(int row, int col) =>
        new(RowHeaderWidth + col * (_cellWidth + Gap), TitleHeight + HeaderHeight + row * (CellHeight + Gap), _cellWidth, CellHeight);

    public bool TryHitCell(Point point, out int row, out int col)
    {
        row = col = -1;
        if (Values is not TableValues values || point.X < RowHeaderWidth || point.Y < TitleHeight + HeaderHeight)
        {
            return false;
        }

        col = (int)((point.X - RowHeaderWidth) / (_cellWidth + Gap));
        row = (int)((point.Y - TitleHeight - HeaderHeight) / (CellHeight + Gap));
        return row >= 0 && row < values.Rows && col >= 0 && col < values.Cols;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Values is not TableValues values)
        {
            return default;
        }

        // Inside a scroll viewer the width is unbounded: fit the viewport, and scroll only below the minimum cell width.
        var viewport = ViewportWidth();
        var width = viewport > 0 ? Math.Min(viewport, availableSize.Width) : double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        _cellWidth = Math.Clamp((width - RowHeaderWidth) / values.Cols - Gap, MinCell, MaxCell);
        return new Size(RowHeaderWidth + values.Cols * (_cellWidth + Gap), TitleHeight + HeaderHeight + values.Rows * (CellHeight + Gap));
    }

    private ScrollViewer? _viewport;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewport = this.FindAncestorOfType<ScrollViewer>();
        if (_viewport is not null)
        {
            _viewport.SizeChanged += OnViewportSizeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewport is not null)
        {
            _viewport.SizeChanged -= OnViewportSizeChanged;
        }
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e) => InvalidateMeasure();

    private double ViewportWidth() =>
        _viewport is { } viewport && viewport.Bounds.Width > 0
            ? Math.Min(viewport.Bounds.Width - viewport.Padding.Left - viewport.Padding.Right, double.IsInfinity(MaxWidth) ? double.MaxValue : MaxWidth)
            : 0;

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
        var flash = _flashCells.Count > 0 ? Math.Max(0, 1 - (DateTime.UtcNow - _flashStart).TotalMilliseconds / FlashTime.TotalMilliseconds) : 0;
        var z = values.Table.Z;

        for (var r = 0; r < values.Rows; r++)
        {
            for (var c = 0; c < values.Cols; c++)
            {
                var bounds = CellBounds(r, c);
                var physical = values.Physical[r, c];
                var colour = double.IsFinite(physical) ? HeatRamp.Color(max > min ? (physical - min) / (max - min) : 0.5) : ThemeBrushes.Color(this, "RaisedColor");
                var text = new SolidColorBrush(HeatRamp.TextOn(colour));
                context.DrawRectangle(new SolidColorBrush(colour), null, bounds, 3, 3);
                if (flash > 0 && _flashCells.Contains((r, c)))
                {
                    context.DrawRectangle(new SolidColorBrush(Colors.White, 0.5 * flash), null, bounds, 3, 3);
                }

                var shown = editor.ShowRaw
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
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Editor is not TableEditorViewModel editor || !TryHitCell(e.GetPosition(this), out var row, out var col))
        {
            return;
        }

        _typed = string.Empty;
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

        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch is '.' or ',')
            {
                _typed += ch == ',' ? '.' : ch;
            }
            else if (ch is '+' or '=' && _typed.Length == 0)
            {
                editor.Apply(new EditRequest(EditKind.Step, 1));
            }
            else if (ch is '-' && _typed.Length == 0)
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
            case Key.Enter when _typed.Length > 0:
                if (double.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    editor.Apply(new EditRequest(EditKind.Set, value));
                }

                _typed = string.Empty;
                break;
            case Key.Escape:
                _typed = string.Empty;
                break;
            case Key.Back when _typed.Length > 0:
                _typed = _typed[..^1];
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
        _typed = string.Empty;
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableEditorViewModel.Values))
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void OnEdited(object? sender, IReadOnlyList<(int Row, int Col)> cells)
    {
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
}
