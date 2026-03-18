using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp;

public partial class PerformanceWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public PerformanceWindow()
    {
        InitializeComponent();
    }

    private void StartPerformanceTestButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ArmPerformanceTest();
    }
}