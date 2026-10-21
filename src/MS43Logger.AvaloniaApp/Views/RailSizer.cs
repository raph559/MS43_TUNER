using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>
/// The drawing side of an <see cref="ISidePanel"/>: sets the list's width (its own, the user's, or the collapsed strip),
/// lets its edge be dragged (or moved with the arrow keys, or double-clicked back to the default), and opens it on its
/// search field. The Tune rail, the Compare list and the Performance rail share it, so all behave the same; the Performance
/// rail has no search field of its own (its search is the results'), so <c>search</c> may be null.
/// </summary>
internal sealed class RailSizer
{
    private const double KeyStep = 16;

    private readonly Border _host;
    private readonly Border _handle;
    private readonly TextBox? _search;
    private ISidePanel? _panel;
    private double? _dragFrom;
    private double _dragWidth;
    private bool _dragMoved;

    public RailSizer(Border host, Border handle, TextBox? search)
    {
        _host = host;
        _handle = handle;
        _search = search;
        handle.PointerPressed += OnPressed;
        handle.PointerMoved += OnMoved;
        handle.PointerReleased += OnReleased;
        handle.KeyDown += OnKeyDown;
    }

    public void Watch(ISidePanel? panel)
    {
        if (_panel is not null)
        {
            _panel.PropertyChanged -= OnPanelChanged;
        }

        _panel = panel;
        if (panel is not null)
        {
            panel.PropertyChanged += OnPanelChanged;
        }

        Apply();
    }

    /// <summary>Shows the list if it is collapsed, then puts the keyboard in its search field (Ctrl K).</summary>
    public void OpenOnSearch()
    {
        _panel?.SetRailCollapsed(false);
        if (_search is { } search)
        {
            Dispatcher.UIThread.Post(() => search.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
    }

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISidePanel.RailCollapsed))
        {
            // Only showing and hiding slides (Motion.axaml); a drag follows the pointer exactly.
            _host.Classes.Add("toggling");
            Apply();
            DispatcherTimer.RunOnce(() => _host.Classes.Remove("toggling"), TimeSpan.FromMilliseconds(250));
        }
        else if (e.PropertyName == nameof(ISidePanel.RailWidth))
        {
            Apply();
        }
    }

    // Collapsed: the strip. A width the user chose: that width at every window size. Otherwise the styles' own.
    private void Apply()
    {
        if (_panel is { RailCollapsed: true })
        {
            _host.Width = SidePanel.CollapsedWidth;
        }
        else if (_panel?.RailWidth is double width)
        {
            _host.Width = width;
        }
        else
        {
            _host.ClearValue(Layoutable.WidthProperty);
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_panel is not { } panel || !e.GetCurrentPoint(_handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            panel.SetRailWidth(null, save: true);
        }
        else
        {
            _dragFrom = e.GetPosition(_host.Parent as Control ?? _host).X;
            _dragWidth = _host.Bounds.Width;
            _dragMoved = false;
            e.Pointer.Capture(_handle);
        }

        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not double from || _panel is not { } panel)
        {
            return;
        }

        _dragMoved = true;
        panel.SetRailWidth(Clamp(_dragWidth + e.GetPosition(_host.Parent as Control ?? _host).X - from), save: false);
    }

    // A press without a move leaves the width as it was: only a real drag makes it the user's own.
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragFrom is null || _panel is not { } panel)
        {
            return;
        }

        _dragFrom = null;
        e.Pointer.Capture(null);
        if (_dragMoved)
        {
            panel.SetRailWidth(panel.RailWidth, save: true);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_panel is not { } panel || e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        panel.SetRailWidth(Clamp(_host.Bounds.Width + (e.Key == Key.Right ? KeyStep : -KeyStep)), save: true);
        e.Handled = true;
    }

    private static double Clamp(double width) => Math.Clamp(Math.Round(width), SidePanel.MinWidth, SidePanel.MaxWidth);
}
