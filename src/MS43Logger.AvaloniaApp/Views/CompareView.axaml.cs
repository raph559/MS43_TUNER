using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Animation.Easings;
using Avalonia.Animation;
using Avalonia;
using System.Threading.Tasks;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MS43Logger.AvaloniaApp.Views;

public partial class CompareView : UserControl
{
    private readonly RailSizer _rail;

    public CompareView()
    {
        InitializeComponent();
        _rail = new RailSizer(CompareList, CompareResizeHandle, CompareSearchBox);
        DataContextChanged += (_, _) => _rail.Watch(DataContext as CompareViewModel);
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } compare)
            {
                compare.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(CompareViewModel.Selected) or nameof(CompareViewModel.ShowsBytes))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(ShowCurrentRow);
                    }
                };
            }
        };
    }

    // The current row in view, as the list scrolls on its own (the mockup's list shows the selected item).
    private void ShowCurrentRow()
    {
        if (ViewModel is { } compare && this.FindControl<ListBox>("DiffList") is { } list)
        {
            var current = compare.Entries.FirstOrDefault(entry => entry is CompareRow { IsCurrent: true } or CompareBytesRow { IsCurrent: true });
            if (current is not null)
            {
                list.ScrollIntoView(current);
            }
        }
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

    // No A yet: the Tune tab's own open flow, which asks about unsaved edits first.
    private async void OpenA_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window)
        {
            await window.OpenBinAsync();
        }
    }

    private void Kind_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } compare && sender is ToggleButton { Tag: string key } button)
        {
            compare.Kind = key;

            // A lit segment pressed again stays lit (the filter has no "none"); its binding saw no change to undo the toggle.
            button.SetCurrentValue(ToggleButton.IsCheckedProperty, compare.Kind == key);
        }
    }

    private void List_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } compare || sender is not ListBox list)
        {
            return;
        }

        // Both actions rebuild the list; inside the ListBox's own selection change that crashed it (see TuneView's rail).
        var entry = list.SelectedItem;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (entry)
            {
                case CompareRow row:
                    compare.Selected = row.Difference;
                    break;
                case CompareBytesRow:
                    compare.ShowBytes();
                    break;
            }
        });
    }

    private void Copy_Click(object? sender, RoutedEventArgs e) => ViewModel?.CopySelectedFromB();

    private void EditInTune_Click(object? sender, RoutedEventArgs e) => ViewModel?.ShowInTune();

    private async void Swap_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CompareViewModel compare && await compare.SwapAsync() && MotionSettings.AnimationsEnabled)
        {
            // Mid-swap the list is shown again only after B reopens; an animation that starts on a hidden control ends at
            // once, so the names cross over after that layout pass.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
            await CrossOverAsync();
        }
    }

    // The swap seen (system 10.3): each name slides from the other's row into its own, and the swap icon turns half a turn.
    private Task CrossOverAsync()
    {
        // The A and B rows are 32 px apart (their DockPanels' Height). Measured here, mid-swap, the list is still being laid out
        // again and the two names can read as one row.
        const double rows = 32;
        var time = TimeSpan.FromMilliseconds(280);
        Animation Move(AvaloniaProperty property, double from, double to) => new()
        {
            Duration = time,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        };

        return Task.WhenAll(
            Move(TranslateTransform.YProperty, rows, 0).RunAsync(FileAText),
            Move(TranslateTransform.YProperty, -rows, 0).RunAsync(FileBText),
            Move(RotateTransform.AngleProperty, 0, 180).RunAsync(SwapIcon));
    }

    private void RailCollapse_Click(object? sender, RoutedEventArgs e) => (DataContext as CompareViewModel)?.SetRailCollapsed(true);

    private void RailExpand_Click(object? sender, RoutedEventArgs e) => (DataContext as CompareViewModel)?.SetRailCollapsed(false);

    private void RailSearch_Click(object? sender, RoutedEventArgs e) => OpenRailOnSearch();

    /// <summary>Shows the list if it is collapsed, then puts the keyboard in its search field (Ctrl K).</summary>
    public void OpenRailOnSearch() => _rail.OpenOnSearch();
}
