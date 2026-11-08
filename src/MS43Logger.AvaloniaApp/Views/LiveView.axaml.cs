using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Views;

public partial class LiveView : UserControl
{
    public LiveView()
    {
        InitializeComponent();

        // Avalonia skips layout inside a hidden subtree and keeps its ancestors' measures, so a tile reading that changed
        // while the window was too small for the tiles would come back arranged at its old width. Showing the section
        // again lays out everything in it afresh.
        var pins = this.FindControl<StackPanel>("PinsSection")!;
        pins.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && e.NewValue is true)
            {
                foreach (var part in pins.GetVisualDescendants().OfType<Layoutable>())
                {
                    part.InvalidateMeasure();
                }
            }
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void PinButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is Button { CommandParameter: DecodedValueViewModel value })
        {
            ViewModel.TogglePinned(value);
        }
    }

    private async void BrowseCsvButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var defaultFileName = Path.GetFileName(ViewModel.CsvPath);
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose CSV log file",
            SuggestedFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "ms43-log.csv" : defaultFileName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }],
        });

        if (file?.Path is { IsAbsoluteUri: true } path)
        {
            ViewModel.CsvPath = path.LocalPath;
        }
    }

    // The banner's way out: another file for a CSV that cannot be written, or the port list again.
    private void BannerAction_Click(object? sender, RoutedEventArgs e)
    {
        switch (ViewModel?.BannerAction)
        {
            case LiveAction.Browse:
                BrowseCsvButton_Click(sender, e);
                break;
            case LiveAction.RescanPorts:
                ViewModel.RefreshPorts();
                break;
        }
    }

    private void ShowDetails_Click(object? sender, RoutedEventArgs e) => ViewModel?.ShowConnectionLog();

    private void FaultMemory_Click(object? sender, RoutedEventArgs e) => ViewModel?.ShowFaultMemory();

    private void ClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } live)
        {
            live.SearchText = string.Empty;
            this.FindControl<TextBox>("ChannelFilter")?.Focus();
        }
    }

    // The tests live on their own tab now; Live's button is the way there.
    private void OpenPerformance_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: ShellViewModel shell })
        {
            shell.SelectedTab = ShellViewModel.PerformanceTab;
        }
    }
}
