using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>A map, curve or axis in a window of its own, owned by the main window: it minimizes and closes with it.</summary>
public partial class MapWindow : Window
{
    private readonly MainWindow? _main;
    private bool _closed;

    public MapWindow()
    {
        InitializeComponent();
    }

    public MapWindow(MapWindowViewModel map, MainWindow main)
        : this()
    {
        DataContext = map ?? throw new ArgumentNullException(nameof(map));
        _main = main;
        map.Closed += OnMapClosed;
        map.ActivateRequested += OnActivateRequested;
        Closed += (_, _) =>
        {
            _closed = true;
            map.Closed -= OnMapClosed;
            map.ActivateRequested -= OnActivateRequested;
            map.Close();
        };
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);

        // The window is there to edit its grid: the grid has the keys from the start.
        Opened += (_, _) => this.FindControl<Controls.MapGrid>("MapGrid")?.Focus();
        UpdateWindowButtons();
    }

    private MapWindowViewModel? Map => DataContext as MapWindowViewModel;

    // The focused map window is the one the V9 watch follows.
    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Map?.LiveCursor.Focused();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateWindowButtons();
        }
    }

    // The bin went away (another bin, another XDF, or the app closing): the window goes with it.
    private void OnMapClosed(object? sender, EventArgs e)
    {
        if (!_closed)
        {
            Close();
        }
    }

    private void OnActivateRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    // Save, Save as, Undo and Redo work here as in the main window; a plain field keeps Ctrl Z for its own text.
    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || Map is not { IsHeld: false } map)
        {
            return;
        }

        var typing = FocusManager?.GetFocusedElement() is TextBox box && !box.Classes.Contains("commits");
        switch (e.Key)
        {
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                if (_main is not null)
                {
                    await _main.SaveAsAsync();
                }

                break;
            case Key.S:
                e.Handled = true;
                if (_main is not null)
                {
                    await _main.SaveAsync();
                }

                break;
            case Key.Z when !typing && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y when !typing:
                e.Handled = true;
                map.Redo();
                break;
            case Key.Z when !typing:
                e.Handled = true;
                map.Undo();
                break;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !MainWindow.StartsWindowDrag(e.Source as Visual))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
        }
        else
        {
            BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    private void Tool_Click(object? sender, RoutedEventArgs e)
    {
        if (Map is { } map && sender is Button { Tag: string kind } && Enum.TryParse<EditKind>(kind, out var edit))
        {
            map.RunTool(edit);
        }
    }

    private void ToolValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Map?.RunTool(EditKind.Set);
            e.Handled = true;
        }
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => Map?.Undo();

    private void Redo_Click(object? sender, RoutedEventArgs e) => Map?.Redo();

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        Classes.Set("maximized", maximized);
        var label = maximized ? "Restore down" : "Maximize";
        ToolTip.SetTip(MaximizeButton, label);
        Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, label);
    }
}
