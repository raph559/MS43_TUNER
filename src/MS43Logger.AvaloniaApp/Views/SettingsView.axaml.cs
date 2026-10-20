using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // An emptied Log folder keeps the saved folder (checkpoint M9), so the field shows it again rather than nothing.
        var box = this.FindControl<TextBox>("LogFolderBox")!;
        box.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text) && ViewModel is { } settings)
            {
                box.Text = settings.LogFolder;
            }
        };
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this);

    private void TryAgain_Click(object? sender, RoutedEventArgs e) => ViewModel?.TryAgain();

    // A row's label is part of its switch's hit area.
    private void Label_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: string name } && this.FindControl<ToggleSwitch>(name) is { } toggle)
        {
            toggle.IsChecked = toggle.IsChecked != true;
        }
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } settings || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Log folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.Path is { IsAbsoluteUri: true } path)
        {
            settings.LogFolder = path.LocalPath;
        }
    }
}
