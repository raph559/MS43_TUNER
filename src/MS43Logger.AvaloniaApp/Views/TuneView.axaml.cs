using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Views;

public partial class TuneView : UserControl
{
    public TuneView()
    {
        InitializeComponent();

        // The slider asks; the value editor writes, one undo step per step, jump or released drag (system 5.14).
        var slider = this.FindControl<ValueSlider>("ValueSlider")!;
        slider.StepRequested += (_, steps) => ViewModel?.Value?.StepBy(steps);
        slider.JumpRequested += (_, toHigh) => ViewModel?.Value?.JumpTo(toHigh);
        slider.DragUpdated += (_, physical) =>
        {
            if (ViewModel?.Value is { } value)
            {
                if (!value.IsDragging)
                {
                    value.BeginDrag();
                }

                value.DragTo(physical);
            }
        };
        slider.DragCommitted += (_, _) => ViewModel?.Value?.CommitDrag();
        slider.DragCancelled += (_, _) => ViewModel?.Value?.CancelDrag();
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

    private async void OpenXdf_Click(object? sender, RoutedEventArgs e) => await OpenXdfAsync();

    /// <summary>Asks about unsaved edits, then opens the current bin (or the last one, or one picked) with another XDF.</summary>
    public async System.Threading.Tasks.Task OpenXdfAsync()
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

    private void ToolValue_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            ViewModel?.RunTool(EditKind.Set);
            e.Handled = true;
        }
    }

    private void SetValue_Click(object? sender, RoutedEventArgs e) => ViewModel?.Value?.Apply();

    private void StepDown_Click(object? sender, RoutedEventArgs e) => ViewModel?.Value?.StepBy(-1);

    private void StepUp_Click(object? sender, RoutedEventArgs e) => ViewModel?.Value?.StepBy(1);

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

    private void Details_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tune)
        {
            tune.InspectorOpen = !tune.InspectorOpen;
        }
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tune)
        {
            tune.SearchText = string.Empty;
            FocusSearch();
        }
    }

    private async void UseAnotherXdf_Click(object? sender, RoutedEventArgs e) => await OpenXdfAsync();

    // The banner's way out of what went wrong (system 7.1).
    private async void ErrorAction_Click(object? sender, RoutedEventArgs e)
    {
        switch (ViewModel?.ErrorAction)
        {
            case TuneAction.OpenBin:
                await OpenBinAsync();
                break;
            case TuneAction.OpenXdf:
            case TuneAction.UseAnotherXdf:
                await OpenXdfAsync();
                break;
            case TuneAction.SaveAs when TopLevel.GetTopLevel(this) is MainWindow window:
                await window.SaveAsAsync();
                break;
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
