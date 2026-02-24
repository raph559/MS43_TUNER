using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MS43Logger.AvaloniaApp;

public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

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
            await ViewModel.ToggleConnectionAsync();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (sender is Button { CommandParameter: DecodedValueViewModel value })
        {
            ViewModel.TogglePinned(value);
        }
    }

    private async void BrowseCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var defaultFileName = Path.GetFileName(ViewModel.CsvPath);
        var selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose CSV log file",
            SuggestedFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "ms43-log.csv" : defaultFileName,
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV files")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });

        if (selectedFile?.Path is { IsAbsoluteUri: true } selectedPath)
        {
            ViewModel.CsvPath = selectedPath.LocalPath;
        }
    }
}
