using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS43Logger.AvaloniaApp.Controls;

namespace MS43Logger.AvaloniaApp.Views;

public partial class TuneView : UserControl
{
    private const double RailCollapsedWidth = 48;
    private const double RailKeyStep = 16;
    private TuneViewModel? _watched;
    private double? _dragFrom;
    private double _dragWidth;
    private bool _dragMoved;

    public TuneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as TuneViewModel);

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

    private void Watch(TuneViewModel? tune)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnTuneChanged;
        }

        _watched = tune;
        if (tune is not null)
        {
            tune.PropertyChanged += OnTuneChanged;
        }

        ApplyRailWidth();
    }

    private void OnTuneChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TuneViewModel.RailCollapsed))
        {
            // Only showing and hiding the rail slides (Motion.axaml); a drag follows the pointer exactly.
            RailHost.Classes.Add("toggling");
            ApplyRailWidth();
            Avalonia.Threading.DispatcherTimer.RunOnce(() => RailHost.Classes.Remove("toggling"), System.TimeSpan.FromMilliseconds(250));
        }
        else if (e.PropertyName == nameof(TuneViewModel.RailWidth))
        {
            ApplyRailWidth();
        }
    }

    // Collapsed: the strip. A width the user chose: that width at every window size. Otherwise the styles' 256 or 224.
    private void ApplyRailWidth()
    {
        if (_watched is { RailCollapsed: true })
        {
            RailHost.Width = RailCollapsedWidth;
        }
        else if (_watched?.RailWidth is double width)
        {
            RailHost.Width = width;
        }
        else
        {
            RailHost.ClearValue(WidthProperty);
        }
    }

    private void SurfaceZoomIn_Click(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1.2);

    private void SurfaceZoomOut_Click(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.2);

    // The large view opens in the window's dialog layer on the small plot's view; whatever is done there stays there.
    private void SurfaceEnlarge_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Editor: { } editor, SelectedItem: { } item } || TopLevel.GetTopLevel(this) is not MainWindow window)
        {
            return;
        }

        _ = window.Dialogs.ShowAsync(new SurfaceDialog(editor, item.Title, Surface, window.ClientSize));
    }

    private void SortKey_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tune && sender is MenuItem { Tag: string key } && System.Enum.TryParse<MS43Logger.Core.Tuning.ItemSort>(key, out var sort))
        {
            tune.SetSort(sort, tune.SortDescending);
        }
    }

    private void SortOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } tune && sender is MenuItem item)
        {
            tune.SetSort(tune.Sort, item.Name == "SortDescending");
        }
    }

    private void RailCollapse_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRailCollapsed(true);

    private void RailExpand_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRailCollapsed(false);

    private void RailSearch_Click(object? sender, RoutedEventArgs e) => OpenRailOnSearch();

    /// <summary>Shows the rail if it is collapsed, then puts the keyboard in its search field (Ctrl K).</summary>
    public void OpenRailOnSearch()
    {
        ViewModel?.SetRailCollapsed(false);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchBox.Focus(Avalonia.Input.NavigationMethod.Tab), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void RailResize_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (ViewModel is not { } tune || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            tune.SetRailWidth(null, save: true);
        }
        else
        {
            _dragFrom = e.GetPosition(this).X;
            _dragWidth = RailHost.Bounds.Width;
            _dragMoved = false;
            e.Pointer.Capture(RailResizeHandle);
        }

        e.Handled = true;
    }

    private void RailResize_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_dragFrom is not double from || ViewModel is not { } tune)
        {
            return;
        }

        _dragMoved = true;
        tune.SetRailWidth(_dragWidth + e.GetPosition(this).X - from, save: false);
    }

    // A press without a move leaves the width as it was: only a real drag makes it the user's own.
    private void RailResize_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_dragFrom is null || ViewModel is not { } tune)
        {
            return;
        }

        _dragFrom = null;
        e.Pointer.Capture(null);
        if (_dragMoved)
        {
            tune.SetRailWidth(tune.RailWidth, save: true);
        }
    }

    private void RailResize_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (ViewModel is not { } tune || e.Key is not (Avalonia.Input.Key.Left or Avalonia.Input.Key.Right))
        {
            return;
        }

        tune.SetRailWidth(RailHost.Bounds.Width + (e.Key == Avalonia.Input.Key.Right ? RailKeyStep : -RailKeyStep), save: true);
        e.Handled = true;
    }

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

        // Both actions rebuild the rail. Rebuilding it inside the ListBox's own selection change made the ListBox read
        // its selection from the new, shorter list and crash, so the action runs once the change has finished.
        var entry = list.SelectedItem;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (entry)
            {
                case RailItem item:
                    tune.SelectedItem = item.Item;
                    break;
                case RailCategory category:
                    tune.ToggleCategory(category.Name);
                    break;
            }
        });
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
