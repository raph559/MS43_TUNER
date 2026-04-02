using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp.Views;

public partial class ConfirmDialog : Window
{

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(SaveChoice.Save);

    private void DontSave_Click(object? sender, RoutedEventArgs e) => Close(SaveChoice.DontSave);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(SaveChoice.Cancel);
}
