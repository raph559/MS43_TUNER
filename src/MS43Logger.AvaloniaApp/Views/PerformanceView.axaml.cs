using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>
/// The Performance tab (performance spec 9.1): the test rail with Run / Results, the chosen test's Run side and the results
/// library with its comparison. The view only forwards presses; every state lives in <see cref="PerformanceViewModel"/>.
/// </summary>
public partial class PerformanceView : UserControl
{
    // The first flag's top 8 px margin and a line of it: less than that in view and the flags are below the fold.
    private const double FlagInView = 32;

    private readonly RailSizer _rail;
    private PerformanceViewModel? _watched;
    private bool _following;
    private Rect? _flagsViewport;

    public PerformanceView()
    {
        InitializeComponent();
        // The rail has no search of its own; Ctrl K goes to the results' search instead (MainWindow).
        _rail = new RailSizer(PerformanceRail, PerformanceResizeHandle, null);
        PerformanceTests.ContainerPrepared += (_, e) => ShapeRow(e.Container);
        ResultFlags.EffectiveViewportChanged += (_, e) =>
        {
            _flagsViewport = e.EffectiveViewport;
            ShowFlagSummary();
        };
        DataContextChanged += (_, _) =>
        {
            _rail.Watch(ViewModel);
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnPerformanceChanged;
                _watched.Run.PropertyChanged -= OnRunChanged;
            }

            _watched = ViewModel;
            if (_watched is not null)
            {
                _watched.PropertyChanged += OnPerformanceChanged;
                _watched.Run.PropertyChanged += OnRunChanged;
            }

            ShowCurrentTest();
            ShowFlagSummary();
        };
    }

    private PerformanceViewModel? ViewModel => DataContext as PerformanceViewModel;

    /// <summary>Shows the results with the keyboard in their search field (Ctrl K).</summary>
    public void OpenResultsOnSearch()
    {
        if (ViewModel is not { } performance)
        {
            return;
        }

        performance.IsResultsMode = true;
        performance.Results.CloseOpened();
        performance.Results.CloseComparison();
        Dispatcher.UIThread.Post(() => ResultsSearchBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    // The list's own selection follows the test in the pane, so a row chosen another way (Calibrate the pedal) and then the
    // old row pressed again still changes the test.
    private void OnPerformanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PerformanceViewModel.SelectedEntry))
        {
            ShowCurrentTest();
        }
    }

    // A group heading is a heading, not a row: no focus, no hover and no press (minor review, finding 40). Containers are
    // reused, so a test's row is made a row again.
    private static void ShapeRow(Control container)
    {
        var heading = container.DataContext is PerformanceGroup;
        container.Focusable = !heading;
        container.IsHitTestVisible = !heading;
    }

    private void OnRunChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PerformanceRunViewModel.FlagSummary))
        {
            ShowFlagSummary();
        }
    }

    // A run's flags named beside Save result while they sit below the fold (minor review, finding 43).
    private void ShowFlagSummary() =>
        FlagSummary.IsVisible = ViewModel?.Run.FlagSummary is not null && _flagsViewport is { } seen && seen.Bottom < FlagInView;

    private void ShowCurrentTest()
    {
        if (ViewModel is not { } performance || ReferenceEquals(PerformanceTests.SelectedItem, performance.SelectedEntry))
        {
            return;
        }

        // Following the model is not a choice: it must not post the test back, or two such posts take turns for ever.
        _following = true;
        try
        {
            PerformanceTests.SelectedItem = performance.SelectedEntry;
        }
        finally
        {
            _following = false;
        }
    }

    private void Tests_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // A group heading is not a test; choosing a test rebuilds the pane, so it waits for the list's own change to finish,
        // and is dropped if the list was moved to another test in the meantime.
        if (_following || ViewModel is not { } performance || PerformanceTests.SelectedItem is not PerformanceTestEntry entry)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(PerformanceTests.SelectedItem, entry))
            {
                performance.SelectedEntry = entry;
            }
        });
    }

    // On the Results side the rail marks no test (minor review, finding 44), so pressing the one last shown shows its Run side
    // as pressing any other does.
    private void Tests_Tapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { IsResultsMode: true } performance
            && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is PerformanceTestEntry entry
            && ReferenceEquals(entry, performance.SelectedEntry))
        {
            performance.IsRunMode = true;
        }
    }

    private void GoToRun_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance)
        {
            performance.IsRunMode = true;
        }
    }

    private void RailCollapse_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRailCollapsed(true);

    private void RailExpand_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRailCollapsed(false);

    private void Arm_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.Arm();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.Cancel();

    private void SaveResult_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.Save();

    private void Discard_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.Discard();

    private void RunAgain_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.RunAgain();

    private void SaveCalibration_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.SaveCalibration();

    // "Calibrate the pedal first" comes with the way to it (spec 4.4).
    private void CalibratePedal_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance && performance.Entries.OfType<PerformanceTestEntry>().FirstOrDefault(entry => entry.Key == "pedal") is { } pedal)
        {
            performance.SelectedEntry = pedal;
        }
    }

    // The line saying another test is on comes with the way to that test (minor review, finding 29).
    private void ShowRunningTest_Click(object? sender, RoutedEventArgs e) => ViewModel?.Run.ShowRunningTest();

    private void Compare_Click(object? sender, RoutedEventArgs e) => ViewModel?.Results.Compare();

    private void CloseComparison_Click(object? sender, RoutedEventArgs e) => ViewModel?.Results.CloseComparison();

    private void CloseOpened_Click(object? sender, RoutedEventArgs e) => ViewModel?.Results.CloseOpened();

    private void SaveDetails_Click(object? sender, RoutedEventArgs e) => ViewModel?.Results.Opened?.SaveDetails();

    private void OpenResult_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance && (sender as Control)?.DataContext is ResultRow row)
        {
            performance.Results.Open(row);
        }
    }

    // Rename opens the result on its name, selected, where the description is edited too (spec 9.1).
    private void RenameResult_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } performance || (sender as Control)?.DataContext is not ResultRow row)
        {
            return;
        }

        performance.Results.Open(row);
        if (performance.Results.HasOpened)
        {
            // Shown only after this layout pass; focus waits for it.
            Dispatcher.UIThread.Post(() =>
            {
                OpenedNameBox.Focus(NavigationMethod.Tab);
                OpenedNameBox.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }

    private async void DeleteResult_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance && (sender as Control)?.DataContext is ResultRow row)
        {
            await performance.Results.DeleteAsync(row);
        }
    }
}
