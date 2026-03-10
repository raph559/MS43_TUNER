using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp;

public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();
    }

    public ErrorWindow(string errorLog) : this()
    {
        ErrorTextBox.Text = errorLog;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}