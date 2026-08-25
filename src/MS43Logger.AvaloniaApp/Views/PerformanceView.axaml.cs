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
/// The Performance tab: the test rail with Run / Results, the chosen test's Run side and the results
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

    private PerformanceViewModel? ViewModel => DataContext as PerformanceViewModel;

    // A group heading is a heading, not a row: no focus, no hover and no press. Containers are
    // reused, so a test's row is made a row again.
    private static void ShapeRow(Control container)
    {
        var heading = container.DataContext is PerformanceGroup;
        container.Focusable = !heading;
        container.IsHitTestVisible = !heading;
    }

    // On the Results side the rail marks no test, so pressing the one last shown shows its Run side
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

    // "Calibrate the pedal first" comes with the way to it.
    private void CalibratePedal_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance && performance.Entries.OfType<PerformanceTestEntry>().FirstOrDefault(entry => entry.Key == "pedal") is { } pedal)
        {
            performance.SelectedEntry = pedal;
        }
    }

    // The line saying another test is on comes with the way to that test.
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

    private async void DeleteResult_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } performance && (sender as Control)?.DataContext is ResultRow row)
        {
            await performance.Results.DeleteAsync(row);
        }
    }
}
