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

    private MapWindowViewModel? Map => DataContext as MapWindowViewModel;

    // The focused map window is the one the V9 watch follows.
    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Map?.LiveCursor.Focused();
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
}
