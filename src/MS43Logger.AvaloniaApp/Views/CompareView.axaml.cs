using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MS43Logger.AvaloniaApp.Views;

public partial class CompareView : UserControl
{
    public CompareView()
    {
        InitializeComponent();
    }

    private CompareViewModel? ViewModel => DataContext as CompareViewModel;

    private async void OpenB_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } compare || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open the bin to compare with (B)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("MS43 bin") { Patterns = ["*.bin"] }, FilePickerFileTypes.All],
        });

        if (files.FirstOrDefault()?.Path is { IsAbsoluteUri: true } path)
        {
            compare.OpenB(path.LocalPath);
        }
    }

    private void Kind_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } compare && sender is ToggleButton { Tag: string key })
        {
            compare.Kind = key;
        }
    }

    private void List_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } compare || sender is not ListBox list)
        {
            return;
        }

        switch (list.SelectedItem)
        {
            case CompareRow row:
                compare.Selected = row.Difference;
                break;
            case CompareBytesRow:
                compare.ShowBytes();
                break;
        }
    }

    private void Copy_Click(object? sender, RoutedEventArgs e) => ViewModel?.CopySelectedFromB();

    private void EditInTune_Click(object? sender, RoutedEventArgs e) => ViewModel?.ShowInTune();
}
