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

    public Rect CellBounds(int row, int col) =>
        new(RowHeaderWidth + col * (_cellWidth + Gap), TitleHeight + HeaderHeight + row * (CellHeight + Gap), _cellWidth, CellHeight);

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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
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
