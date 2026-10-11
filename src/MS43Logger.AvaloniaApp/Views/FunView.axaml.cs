using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp.Views;

public partial class FunView : UserControl
{
    public FunView()
    {
        InitializeComponent();
    }

    private FunViewModel? Fun => DataContext as FunViewModel;

    private async void Send_Click(object? sender, RoutedEventArgs e) { if (Fun is { } fun) await fun.SendAsync(); }

    private async void WriteToBin_Click(object? sender, RoutedEventArgs e) { if (Fun is { } fun) await fun.WriteToBinAsync(); }

    private async void SendLimiter_Click(object? sender, RoutedEventArgs e) { if (Fun is { } fun) await fun.SendLimiterAsync(); }

    private void ApplyLimiter_Click(object? sender, RoutedEventArgs e) => Fun?.ApplyOfflineLimiter();

    private void Undo_Click(object? sender, RoutedEventArgs e) => Fun?.Undo();

    private void Redo_Click(object? sender, RoutedEventArgs e) => Fun?.Redo();

    private void ScalarSet_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LiveScalarRow row) row.Editor.Apply();
    }

    private void ScalarValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is LiveScalarRow row)
        {
            row.Editor.Apply();
            e.Handled = true;
        }
    }
}
