using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp;

/// <summary>Times 0 to 100 and 0 to 200 km/h from the logged vehicle speed, beside the Live screen.</summary>
public partial class PerformanceWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public PerformanceWindow()
    {
        InitializeComponent();
    }

    private void StartTest_Click(object? sender, RoutedEventArgs e) => ViewModel?.ArmPerformanceTest();
}
