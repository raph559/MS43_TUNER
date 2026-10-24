using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

        // Show details on a Live banner opens the connection log; nothing waits for it to close.
        shell.Live.ShowConnectionLogRequested += (_, _) =>
            _ = Dialogs.ShowAsync(new ConnectionLogView(shell.Live.Banner, shell.Live.ConnectionLog ?? string.Empty));

        // The save prompt reads the Saving settings as it opens; Esc, or another dialog taking its place, cancels.
        shell.Tune.AskToSave = async message =>
        {
            var settings = shell.Tune.Settings;
            var answer = await Dialogs.ShowAsync(new ConfirmDialog(message, ConfirmDialog.SaveDetail(settings.KeepBackup, settings.AppendEditLog)));
            return answer is SaveChoice choice ? choice : SaveChoice.Cancel;
        };

        UpdateWindowButtons();
        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        shell.Live.PropertyChanged += OnLiveChanged;
        ShowConnectionLine();
        Opened += async (_, _) => await Shell.StartAsync();
    }

    // Settings has no result to wait for: every choice applies as it is made. Not awaiting its lifetime also keeps
    // the async key handler from staying open for as long as the dialog is.
    private void OpenSettings(NavigationMethod focusWith)
    {
        if (!Dialogs.IsOpen)
        {
            _ = Dialogs.ShowAsync(new SettingsView { DataContext = new SettingsViewModel(Shell.Tune.Settings, Shell) }, focusWith);
        }
    }

    // A gear pressed with Enter or Space has the keyboard's focus ring; one clicked with the pointer does not.
    private void Settings_Click(object? sender, RoutedEventArgs e) =>
        OpenSettings(sender is Control { IsFocused: true } gear && gear.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateWindowButtons();
        }

        if (change.Property == ClientSizeProperty)
        {
            // Layout steps of system 8: below 1280 px the header compacts, below 1200 px lists go to one column.
            Classes.Set("compact", ClientSize.Width < 1280);
            Classes.Set("compactheader", ClientSize.Width < 1280 + WindowButtonsWidth);
            Classes.Set("tight", ClientSize.Width < 1200);

            // Below 760 px of height Live's pinned tiles give their room to the list (system 8).
            Classes.Set("short", ClientSize.Height < 760);
        }
    }

    // The header stands in for the system title bar: a press on its empty parts moves the window (Windows snaps it as
    // usual), a double-click maximizes or restores. Presses on its buttons, tabs and pickers are handled by them first.
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
        }
        else
        {
            BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    /// <summary>Three 46 px window buttons plus the 8 px before them: the header's right padding.</summary>
    private const double WindowButtonsWidth = 146;

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximized();

    // Same path as Alt F4: the unsaved-edits prompt in OnClosing still asks first.
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        Classes.Set("maximized", maximized);
        var label = maximized ? "Restore down" : "Maximize";
        ToolTip.SetTip(MaximizeButton, label);
        Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, label);
    }

    private void OnLiveChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.StatusParts))
        {
            ShowConnectionLine();
        }
    }

    private void ShowConnectionLine()
    {
        if (this.FindControl<TextBlock>("ConnectionText") is not { } line)
        {
            return;
        }

        var inlines = new InlineCollection();
        foreach (var part in Shell.Live.StatusParts)
        {
            var run = new Run(part.Text);
            run.Classes.Set("num", part.IsNumber);
            run.Classes.Set("id", part.IsId);
            run.Classes.Set("now", part.IsNow);
            inlines.Add(run);
        }

        line.Inlines = inlines;

        // A long log name trims the line's end; the tooltip keeps all of it (system 8: nothing is clipped).
        ToolTip.SetTip(line, string.Concat(Shell.Live.StatusParts.Select(part => part.Text)));
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
        // Esc closes the Tune inspector's overlay below 1280 px, as Details opened it.
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !Dialogs.IsOpen && Classes.Contains("compact") && Shell.Tune.InspectorOpen)
        {
            Shell.Tune.InspectorOpen = false;
            e.Handled = true;
            return;
        }

        // Nothing acts behind an open dialog: the shortcuts belong to the screen it covers.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || Dialogs.IsOpen)
        {
            return;
        }

        // A field keeps Ctrl Z for its own text, except the number fields that commit edits (class "commits"): they
        // empty or keep the number once it is applied, so Ctrl Z there means the edit, not the typing.
        var typing = FocusManager?.GetFocusedElement() is TextBox box && !box.Classes.Contains("commits");
        var undos = Shell.IsLive ? Shell.Live.Undos : Shell.Tune.Undos;
        switch (e.Key)
        {
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                await SaveAsAsync();
                break;
            case Key.S:
                e.Handled = true;
                await SaveAsync();
                break;
            case Key.O:
                e.Handled = true;
                await OpenBinAsync();
                break;
            case Key.B when Shell.IsTune && Shell.Tune.HasDocument:
                e.Handled = true;
                Shell.Tune.SetRailCollapsed(!Shell.Tune.RailCollapsed);
                break;
            case Key.OemComma:
                e.Handled = true;
                OpenSettings(NavigationMethod.Tab);
                break;
            case Key.K:
                // The search field of the tab in front (system 5.3): channels on Live, items on Tune and Compare.
                e.Handled = true;
                if (Shell.IsTune && this.GetVisualDescendants().OfType<TuneView>().FirstOrDefault() is { } tuneView && Shell.Tune.HasDocument)
                {
                    tuneView.OpenRailOnSearch();
                    break;
                }

                var field = Shell.IsLive ? "ChannelFilter" : Shell.IsTune ? "SearchBox" : "CompareSearchBox";
                this.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>()
                    .FirstOrDefault(box => box.Name == field && box.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab);
                break;
            case Key.Z when !typing && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y when !typing:
                e.Handled = true;
                undos.Redo();
                break;
            case Key.Z when !typing:
                e.Handled = true;
                undos.Undo();
                break;
        }
    }

    public async System.Threading.Tasks.Task SaveAsAsync()
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

    private async System.Threading.Tasks.Task SaveAsync()
    {
        if (Shell.Tune.Document?.Path is null)
        {
            await SaveAsAsync();
        }
        else
        {
            Shell.Tune.Save();
        }
    }

    public async System.Threading.Tasks.Task OpenBinAsync()
    {
        Shell.SelectedTab = ShellViewModel.TuneTab;
        if (Tune is { } view)
        {
            await view.OpenBinAsync();
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void SaveAs_Click(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async void OpenBin_Click(object? sender, RoutedEventArgs e) => await OpenBinAsync();

    private async void UseXdf_Click(object? sender, RoutedEventArgs e)
    {
        Shell.SelectedTab = ShellViewModel.TuneTab;
        if (Tune is { } view)
        {
            await view.OpenXdfAsync();
        }
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => Shell.Live.RefreshPorts();

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e) => await Shell.Live.ToggleConnectionAsync();
}
