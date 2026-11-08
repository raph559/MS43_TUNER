using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>The Live tuning tab (live tuning spec 4). The view only forwards presses; every state lives in <see cref="LiveTuningViewModel"/>.</summary>
public partial class LiveTuningView : UserControl
{
    public LiveTuningView()
    {
        InitializeComponent();
    }

    private LiveTuningViewModel? Page => DataContext as LiveTuningViewModel;

    /// <summary>
    /// A map row selects the map; a category opens or closes. Both can rebuild the list, so the action runs once the
    /// ListBox's own selection change has finished (as the Tune rail does), and the list then shows the selected map.
    /// </summary>
    private void Maps_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Page is not { } page || sender is not ListBox list || list.SelectedItem is not { } entry || ReferenceEquals(entry, page.Selected))
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (entry)
            {
                case LiveMapRow row:
                    page.Selected = row;
                    break;
                case LiveRailCategory category:
                    page.ToggleCategory(category.Name);
                    break;
            }

            list.SelectedItem = page.Rail.Contains(page.Selected!) ? page.Selected : null;
        });
    }

    private void Tool_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<EditKind>(tag, out var kind))
        {
            Page?.ApplyTool(kind);
        }
    }

    private void Value_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Page?.ApplyTool(EditKind.Set);
            e.Handled = true;
        }
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => Page?.Undo();

    private void ScalarSet_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { IsReadOnly: false } && sender is Control { DataContext: LiveScalarRow row }) row.Editor.Apply();
    }

    private void ScalarValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Page is { IsReadOnly: false } && sender is Control { DataContext: LiveScalarRow row })
        {
            row.Editor.Apply();
            e.Handled = true;
        }
    }

    private void Redo_Click(object? sender, RoutedEventArgs e) => Page?.Redo();

    private async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page)
        {
            await page.SendSelectedAsync();
        }
    }

    private async void SendAll_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page)
        {
            await page.SendAllAsync();
        }
    }

    private async void Revert_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page)
        {
            await page.RevertAllAsync();
        }
    }

    private async void Free_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page)
        {
            await page.FreeSelectedAsync();
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page)
        {
            await page.CopyEditedToTuneAsync();
        }
    }

    private async void LimiterSend_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page) await page.SendLimiterAsync();
    }

    private void LimiterCopy_Click(object? sender, RoutedEventArgs e) => Page?.CopyLimiterToTune();

    // The switch shows the ECU's state: a press asks for the other side, and the next status moves it.
    private async void AB_Click(object? sender, RoutedEventArgs e)
    {
        if (Page is { } page && sender is ToggleSwitch toggle)
        {
            var wanted = !page.AllEdited;
            toggle.IsChecked = page.AllEdited;
            await page.SetAllEditedAsync(wanted);
        }
    }
}
