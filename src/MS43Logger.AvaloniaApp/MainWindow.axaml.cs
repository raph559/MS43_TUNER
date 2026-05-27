using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.AvaloniaApp.Views;

namespace MS43Logger.AvaloniaApp;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
        : this(new ShellViewModel(new MainWindowViewModel(), new TuneViewModel(AppSettings.Load(AppSettings.DefaultFolder))))
    {
    }

    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;

        shell.Live.ConnectionFailed += (_, errorLog) => new ErrorWindow(errorLog).ShowDialog(this);
        shell.Tune.AskToSave = async message =>
        {
            var answer = await new ConfirmDialog(message).ShowDialog<SaveChoice?>(this);
            return answer ?? SaveChoice.Cancel;
        };

        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext!;

    private TuneView? Tune => this.GetVisualDescendants().OfType<TuneView>().FirstOrDefault();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && Shell.Tune.Document is { IsModified: true })
        {
            // Ask first; close again once the edits are saved or knowingly dropped.
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await Shell.Tune.ConfirmDiscardAsync())
                {
                    _closeConfirmed = true;
                    Close();
                }
            });
            return;
        }

        // Leave the ECU's logger session cleanly when the window closes.
        if (Shell.Live.IsConnected)
        {
            Shell.Live.Disconnect();
        }
    }

    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        var tune = Shell.Tune;
        var typing = FocusManager?.GetFocusedElement() is TextBox;
        switch (e.Key)
        {
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                await SaveAsAsync();
                break;
            case Key.S:
                e.Handled = true;
                if (tune.Document?.Path is null)
                {
                    await SaveAsAsync();
                }
                else
                {
                    tune.Save();
                }

                break;
            case Key.O:
                e.Handled = true;
                Shell.SelectedTab = ShellViewModel.TuneTab;
                if (Tune is { } view)
                {
                    await view.OpenBinAsync();
                }

                break;
            case Key.K:
                e.Handled = true;
                Shell.SelectedTab = ShellViewModel.TuneTab;
                Dispatcher.UIThread.RunJobs();
                Tune?.FocusSearch();
                break;
            case Key.Z when !typing:
                e.Handled = true;
                tune.Undo();
                break;
            case Key.Y when !typing:
                e.Handled = true;
                tune.Redo();
                break;
        }
    }

    private async System.Threading.Tasks.Task SaveAsAsync()
    {
        if (Shell.Tune.Document is not { } document)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save bin as",
            SuggestedFileName = System.IO.Path.GetFileName(document.Path ?? "tune.bin"),
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("MS43 bin") { Patterns = ["*.bin"] }],
        });

        if (file?.Path is { IsAbsoluteUri: true } path)
        {
            Shell.Tune.Save(path.LocalPath);
        }
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => Shell.Live.RefreshPorts();

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e) => await Shell.Live.ToggleConnectionAsync();

    public async System.Threading.Tasks.Task OpenBinAsync()
    {
        Shell.SelectedTab = ShellViewModel.TuneTab;
        if (Tune is { } view)
        {
            await view.OpenBinAsync();
        }
    }
}
