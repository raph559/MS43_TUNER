using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MS43Logger.AvaloniaApp.Views;

public partial class TuneView : UserControl
{
    public TuneView()
    {
        InitializeComponent();
    }

    private TuneViewModel? ViewModel => DataContext as TuneViewModel;

    public void FocusSearch() => this.FindControl<TextBox>("SearchBox")?.Focus();

    private async void OpenBin_Click(object? sender, RoutedEventArgs e) => await OpenBinAsync();

    /// <summary>Asks about unsaved edits, then opens a bin with the XDF remembered for its firmware (or asks for one).</summary>
    public async System.Threading.Tasks.Task OpenBinAsync()
    {
        if (ViewModel is not { } tune || !await tune.ConfirmDiscardAsync() || await PickAsync("Open bin", "MS43 bin", "*.bin") is not { } bin)
        {
            return;
        }

        await tune.OpenAsync(bin, null);
        if (tune.ErrorMessage?.StartsWith("Choose the XDF", System.StringComparison.Ordinal) == true &&
            await PickAsync(tune.ErrorMessage, "TunerPro XDF", "*.xdf") is { } xdf)
        {
            await tune.OpenAsync(bin, xdf);
        }
    }

    private async void OpenXdf_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } tune || !await tune.ConfirmDiscardAsync() || await PickAsync("Open XDF", "TunerPro XDF", "*.xdf") is not { } xdf)
        {
            return;
        }

        var bin = tune.Document?.Path ?? (ViewModel.Settings.LastBin is { } last && System.IO.File.Exists(last) ? last : null)
            ?? await PickAsync("Open the bin to use with this XDF", "MS43 bin", "*.bin");
        if (bin is not null)
        {
            await tune.OpenAsync(bin, xdf);
        }
    }

    private void Rail_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } tune || sender is not ListBox list)
        {
            return;
        }

        switch (list.SelectedItem)
        {
            case RailItem item:
                tune.SelectedItem = item.Item;
                break;
            case RailCategory category:
                tune.ToggleCategory(category.Name);
                break;
        }
    }

    private void Tool_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tune && sender is Button { Tag: string kind } && System.Enum.TryParse<EditKind>(kind, out var edit))
        {
            tune.RunTool(edit);
        }
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => ViewModel?.Undo();

    private void Redo_Click(object? sender, RoutedEventArgs e) => ViewModel?.Redo();

    private void Save_Click(object? sender, RoutedEventArgs e) => ViewModel?.Save();

    private async void SaveAs_Click(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    /// <summary>Asks where to save, then saves there (the edit log and .bak follow the new file).</summary>
    public async System.Threading.Tasks.Task SaveAsAsync()
    {
        if (ViewModel is not { Document: { } document } tune || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save bin as",
            SuggestedFileName = System.IO.Path.GetFileName(document.Path ?? "tune.bin"),
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("MS43 bin") { Patterns = ["*.bin"] }],
        });

        if (file?.Path is { IsAbsoluteUri: true } path)
        {
            tune.Save(path.LocalPath);
        }
    }

    private void ToolValue_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            ViewModel?.RunTool(EditKind.Set);
            e.Handled = true;
        }
    }

    private void SetValue_Click(object? sender, RoutedEventArgs e) => ViewModel?.Value?.Apply();

    private void ValueInput_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            ViewModel?.Value?.Apply();
            e.Handled = true;
        }
    }

    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedItem: { } item } tune)
        {
            tune.TogglePin(item);
        }
    }

    private async System.Threading.Tasks.Task<string?> PickAsync(string title, string kind, string pattern)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = [pattern] }, FilePickerFileTypes.All],
        });

        return files.FirstOrDefault()?.Path is { IsAbsoluteUri: true } path ? path.LocalPath : null;
    }
}
