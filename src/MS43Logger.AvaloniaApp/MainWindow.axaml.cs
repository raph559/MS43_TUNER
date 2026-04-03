using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp;

public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;

        viewModel.ConnectionFailed += (s, errorLog) =>
        {
            var errorWindow = new ErrorWindow(errorLog);
            errorWindow.ShowDialog(this);
        };
        {
            if (ViewModel?.IsConnected == true)
            {
                ViewModel.Disconnect();
            }
        };
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext!;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && Shell.Tune.Document is { IsModified: true })
        {
            // Ask first; close again once the edits are saved or knowingly dropped.
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await Shell.Tune.ConfirmDiscardAsync())
                {
                    _closeConfirmed = true;
                    Close();
                }
            });
            return;
        }

        // Leave the ECU's logger session cleanly when the window closes.
        if (Shell.Live.IsConnected)
        {
            Shell.Live.Disconnect();
        }
    }

    private async System.Threading.Tasks.Task SaveAsAsync()
    {
        if (Shell.Tune.Document is not { } document)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save bin as",
            SuggestedFileName = System.IO.Path.GetFileName(document.Path ?? "tune.bin"),
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("MS43 bin") { Patterns = ["*.bin"] }],
        });

        if (file?.Path is { IsAbsoluteUri: true } path)
        {
            Shell.Tune.Save(path.LocalPath);
        }
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => Shell.Live.RefreshPorts();

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e) => await Shell.Live.ToggleConnectionAsync();

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

    private void OpenPerformanceWindow_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var perfWindow = new PerformanceWindow
        {
            DataContext = ViewModel
        };
        perfWindow.Show(this);
    }
}
