using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS43Logger.AvaloniaApp;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        
        // Ensure graceful disconnect when closing window
        Closing += (s, e) => 
        {
            if (ViewModel?.IsConnected == true)
            {
                ViewModel.Disconnect();
            }
        };
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RefreshPorts();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.ConnectAsync();
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.Disconnect();
    }
}