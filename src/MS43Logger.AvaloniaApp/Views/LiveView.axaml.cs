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

    private void ClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } live)
        {
            live.SearchText = string.Empty;
            this.FindControl<TextBox>("ChannelFilter")?.Focus();
        }
    }

    private void OpenPerformanceWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && TopLevel.GetTopLevel(this) is Window owner)
        {
            new PerformanceWindow { DataContext = ViewModel }.Show(owner);
        }
    }
}
