using System;
using System.Collections.Generic;
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
    private readonly List<MapWindow> _mapWindows = new();
    private readonly List<MapWindow> _asideForDialog = new();
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

        // Fault memory on the Live tab: the stored faults, read on open over the connected line.
        shell.Live.ShowFaultMemoryRequested += (_, _) =>
            _ = Dialogs.ShowAsync(new FaultMemoryView(new FaultMemoryViewModel(() => shell.Live.Line)));

        // The save prompt reads the Saving settings as it opens; Esc, or another dialog taking its place, cancels.
        shell.Compare.AskToSwap = async message => await Dialogs.ShowAsync(new ConfirmActionDialog(message,
            "Tune's undo history starts again with the new A. Unsaved changes to A are asked about first.", "Swap")) is true;
        shell.Performance.AskToDelete = async message => await Dialogs.ShowAsync(new ConfirmActionDialog(message,
            "The result's file is deleted from the results folder. This cannot be undone.", "Delete")) is true;

        shell.Tune.AskToSave = async message =>
        {
            var settings = shell.Tune.Settings;
            var answer = await Dialogs.ShowAsync(new ConfirmDialog(message, ConfirmDialog.SaveDetail(settings.KeepBackup, settings.AppendEditLog)));
            return answer is SaveChoice choice ? choice : SaveChoice.Cancel;
        };
        shell.Performance.Run.AskToSaveResult = async message =>
        {
            var answer = await Dialogs.ShowAsync(new ConfirmDialog(message, "Don't save drops the run and its samples."));
            return answer is SaveChoice choice ? choice : SaveChoice.Cancel;
        };

        UpdateWindowButtons();
        App.ThemeChanging += CaptureTheme;
        App.ThemeChanged += FadeTheme;
        shell.Tune.MapWindowOpened += (_, map) => ShowMapWindow(map);
        Dialogs.IsOpenChanged += (_, _) => StepMapWindowsAside(Dialogs.IsOpen);
        Closed += (_, _) =>
        {
            App.ThemeChanging -= CaptureTheme;
            App.ThemeChanged -= FadeTheme;
            shell.Tune.CloseMapWindows();
        };
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
            // Layout steps of system 8: below 1280 px the header compacts, below 1200 px lists go to one column. The fifth
            // tab (live tuning) takes 110 px more of the header, so it compacts below 1536 px of window.
            Classes.Set("compact", ClientSize.Width < 1280);
            Classes.Set("compactheader", ClientSize.Width < CompactHeaderBelow);
            Classes.Set("tight", ClientSize.Width < 1200);

            // Below 760 px of height Live's pinned tiles give their room to the list (system 8).
            Classes.Set("short", ClientSize.Height < 760);
        }
    }

    // The header stands in for the system title bar: a press on its empty parts moves the window (Windows snaps it as
    // usual), a double-click maximizes or restores. Presses on its buttons, tabs and pickers are handled by them first.
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !StartsWindowDrag(e.Source as Visual))
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

    /// <summary>
    /// Whether a press here moves the window: on the header's background or plain text, yes; on anything a person
    /// operates (a picker, a button, a tab), no. A picker opens on the release, which a window drag would swallow.
    /// </summary>
    internal static bool StartsWindowDrag(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is InputElement { Focusable: true })
            {
                return false;
            }

            if (visual is Border { Name: "TitleBar" })
            {
                return true;
            }
        }

        return true;
    }

    /// <summary>Three 46 px window buttons plus the 8 px before them: the header's right padding.</summary>
    private const double WindowButtonsWidth = 146;

    // 1280 px of header and the 146 px of window buttons, and the fifth tab's room.
    private const double CompactHeaderBelow = 1536;

    // The theme cross-fade (system 10.3): the window as it looks now is laid over it, the theme changes underneath, and the
    // picture fades out in 280 ms. Without Windows' animation effects the theme simply changes.
    private void CaptureTheme()
    {
        if (!MotionSettings.AnimationsEnabled || !IsVisible || RootGrid.Bounds.Width < 1 || RootGrid.Bounds.Height < 1)
        {
            return;
        }

        var scale = RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)(RootGrid.Bounds.Width * scale)), Math.Max(1, (int)(RootGrid.Bounds.Height * scale)));
        var picture = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        picture.Render(RootGrid);
        (ThemeFade.Source as IDisposable)?.Dispose();
        ThemeFade.Source = picture;
        ThemeFade.Opacity = 1;
        ThemeFade.IsVisible = true;
    }

    private async void FadeTheme()
    {
        if (ThemeFade.Source is not { } picture || !ThemeFade.IsVisible)
        {
            return;
        }

        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(280),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
            },
        };
        await fade.RunAsync(ThemeFade);

        // A second switch during the fade puts its own picture there; only the picture this fade started with is dropped.
        if (ReferenceEquals(ThemeFade.Source, picture))
        {
            ThemeFade.IsVisible = false;
            ThemeFade.Source = null;
            (picture as IDisposable)?.Dispose();
        }
    }

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

    /// <summary>The tables open in windows of their own, in the order they were opened.</summary>
    internal IReadOnlyList<MapWindow> MapWindows => _mapWindows;

    // A table window starts at the size its grid wants, up to most of the screen, cascaded from this window's top left as
    // TunerPro's are; after that it is the person's to size and move.
    private void ShowMapWindow(MapWindowViewModel map)
    {
        var view = new MapWindow(map, this) { SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual };
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            view.MaxWidth = screen.WorkingArea.Width / screen.Scaling * 0.9;
            view.MaxHeight = screen.WorkingArea.Height / screen.Scaling * 0.9;
        }

        var step = (int)Math.Round((64 + 32 * (_mapWindows.Count % 8)) * RenderScaling);
        view.Position = new PixelPoint(Position.X + step, Position.Y + step);
        view.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            view.SizeToContent = SizeToContent.Manual;
            view.MaxWidth = double.PositiveInfinity;
            view.MaxHeight = double.PositiveInfinity;

            // Kept on its screen: a cascade from a window near the screen's edge would start partly off it.
            if (view.Screens.ScreenFromWindow(view) is { } shown && view.FrameSize is { } frame)
            {
                var area = shown.WorkingArea;
                var size = PixelSize.FromSize(frame, shown.Scaling);
                view.Position = new PixelPoint(
                    Math.Clamp(view.Position.X, area.X, Math.Max(area.X, area.Right - size.Width)),
                    Math.Clamp(view.Position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
            }
        });
        view.Closed += (_, _) => _mapWindows.Remove(view);
        _mapWindows.Add(view);
        view.Show(this);
    }

    // This window's prompts are drawn inside it, and an owned map window stays above it, so a map could hide the save
    // prompt on closing. While a prompt is open the map windows step aside; they come back, unactivated, when it closes.
    private void StepMapWindowsAside(bool aside)
    {
        if (aside)
        {
            foreach (var view in _mapWindows.Where(view => view.IsVisible))
            {
                view.Hide();
                _asideForDialog.Add(view);
            }

            return;
        }

        foreach (var view in _asideForDialog.Where(_mapWindows.Contains))
        {
            view.ShowActivated = false;
            view.Show(this);
            view.ShowActivated = true;
        }

        _asideForDialog.Clear();
    }

    private TuneView? Tune => this.GetVisualDescendants().OfType<TuneView>().FirstOrDefault();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && (Shell.Tune.Document is { IsModified: true } || Shell.Performance.Run.HasUnsavedResult))
        {
            // Ask first; close again once the edits and a finished run are saved or knowingly dropped (performance spec 2).
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                using var hold = Shell.Tune.HoldMapWindows();
                if (await Shell.Tune.ConfirmDiscardAsync() && await Shell.Performance.Run.ConfirmCloseAsync())
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

        // Nothing acts behind an open dialog: the shortcuts belong to the screen it covers. The enlarged surface edits the map,
        // so Undo and Redo reach through it.
        var surfaceOpen = Dialogs.IsOpen && Dialogs.Content is SurfaceDialog;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || (Dialogs.IsOpen && !(surfaceOpen && e.Key is Key.Z or Key.Y)))
        {
            return;
        }

        // Performance has no undo history: Ctrl Z and Ctrl Y there never reach another screen's (performance spec 9.1), and a
        // field keeps them for its own text.
        if (Shell.IsPerformance && e.Key is Key.Z or Key.Y)
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
            // Performance's Save is the result's, never the bin's (minor review, finding 31).
            case Key.S when Shell.IsPerformance:
                e.Handled = true;
                if (Shell.Performance.Run.CanSave)
                {
                    Shell.Performance.Run.Save();
                }

                break;
            case Key.S:
                e.Handled = true;
                await SaveAsync();
                break;
            case Key.O:
                e.Handled = true;
                await OpenBinAsync();
                break;
            case Key.B when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Shell.IsTune:
                e.Handled = true;
                Shell.Tune.CopyFromB();
                break;
            case Key.B when Shell.IsTune && Shell.Tune.HasDocument:
                e.Handled = true;
                Shell.Tune.SetRailCollapsed(!Shell.Tune.RailCollapsed);
                break;
            case Key.B when Shell.IsCompare && Shell.Compare.HasB:
                e.Handled = true;
                Shell.Compare.SetRailCollapsed(!Shell.Compare.RailCollapsed);
                break;
            case Key.B when Shell.IsPerformance:
                e.Handled = true;
                Shell.Performance.SetRailCollapsed(!Shell.Performance.RailCollapsed);
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

                if (Shell.IsCompare && this.GetVisualDescendants().OfType<CompareView>().FirstOrDefault() is { } compareView && Shell.Compare.HasB)
                {
                    compareView.OpenRailOnSearch();
                    break;
                }

                // The tab's one search is the results' (performance spec 9.1): Run gives way to it.
                if (Shell.IsPerformance && this.GetVisualDescendants().OfType<PerformanceView>().FirstOrDefault() is { } performanceView)
                {
                    performanceView.OpenResultsOnSearch();
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

    internal async System.Threading.Tasks.Task SaveAsync()
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
