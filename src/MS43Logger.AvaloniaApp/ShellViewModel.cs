using System;
using System.Linq;
using Avalonia;
using Avalonia.Styling;

namespace MS43Logger.AvaloniaApp;

/// <summary>The window: header tabs, theme, and the screens.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    public const string LiveTab = "Live";
    public const string TuneTab = "Tune";
    public const string CompareTab = "Compare";
    public const string PerformanceTab = "Performance";

    private string _selectedTab = LiveTab;
    private System.Threading.Tasks.Task? _start;

    public ShellViewModel(MainWindowViewModel live, TuneViewModel tune)
    {
        Live = live;
        Tune = tune;
        Compare = new CompareViewModel(tune);
        Performance = new PerformanceViewModel(live, tune);
        Compare.ShowInTuneRequested += (_, item) =>
        {
            tune.SelectedItem = item;
            SelectedTab = TuneTab;
        };
        App.SetTheme(tune.Settings.Theme);
        tune.LiveCursor.Trail = tune.Settings.CursorTrail;
        live.Readouts = tune.Settings.Readouts;
        ApplyLiveSettings();

        // The last port used is preselected while it is still plugged in (system 7.2); otherwise the first stays.
        if (tune.Settings.LastPort is { } port && live.AvailablePorts.Contains(port))
        {
            live.SelectedPort = port;
        }
        if (Application.Current is { } app)
        {
            // Follow Windows changes the theme without a setter call; the status bar shows the theme in effect.
            app.ActualThemeVariantChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsNight));
                OnPropertyChanged(nameof(IsDay));
            };
        }

        if (tune.Settings.Variant is { } variant && live.AdxVariants.Contains(variant))
        {
            live.SelectedVariant = variant;
        }

        live.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedVariant))
            {
                tune.Settings.Variant = live.SelectedVariant;
                tune.Settings.Save();
            }

            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                OnPropertyChanged(nameof(ConnectIsPrimary));
                if (!live.IsConnected)
                {
                    tune.LiveCursor.Clear();
                    // A run ends incomplete with what it measured (review focus 1).
                    Performance.LinkLost();
                }
                else if (live.SelectedPort is { Length: > 0 } connected && connected != tune.Settings.LastPort)
                {
                    tune.Settings.LastPort = connected;
                    tune.Settings.Save();
                }
            }
        };

        live.SampleShown += (_, sample) => tune.LiveCursor.Push(sample, live.CurrentDefinition);
        // On every tab, so a run survives switching tabs; Live has already fed its warm-up monitor the same frame (performance
        // spec 5).
        live.SampleShown += (_, sample) => Performance.Push(sample, live.LastFrame, live.CurrentDefinition);
        // A finished run to save takes the primary from Connect (minor review, findings 20 and 33).
        Performance.Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PerformanceRunViewModel.CanSave))
            {
                OnPropertyChanged(nameof(ConnectIsPrimary));
            }
        };
        tune.LiveCursor.IsVisible = IsTune;

        tune.DocumentOpened += (_, _) =>
        {
            // The rev counter's red zone follows the limiter of the open tune.
            live.SetRedline(GaugeViewModel.RedlineFrom(tune.Document));

            // MS43X001 bins log through the MS43X definition.
            if (!live.IsConnected && tune.Document?.Firmware.SoftwareId == "43X001")
            {
                live.SelectedVariant = "ms43x";
            }
        };
    }

    /// <summary>
    /// Reopens the last bin at start when the Settings switch says so (system 5.12). A missing bin or XDF leaves the
    /// empty Tune screen with a banner naming the missing file; no file picker opens at start. Runs once: every caller
    /// gets the same task.
    /// </summary>
    public System.Threading.Tasks.Task StartAsync() => _start ??= StartCoreAsync();

    private async System.Threading.Tasks.Task StartCoreAsync()
    {
        if (!Tune.Settings.ReopenLastBin || Tune.Settings.LastBin is not { } bin)
        {
            return;
        }

        var name = System.IO.Path.GetFileName(bin);
        if (!System.IO.File.Exists(bin))
        {
            Tune.ShowError($"Could not reopen {name}: the file was not found.", TuneAction.OpenBin);
            return;
        }

        await Tune.OpenAsync(bin, null);
        if (Tune.Document is null && Tune.MissingXdfPath is { } xdf)
        {
            Tune.ShowError($"Could not reopen {name}: its XDF {System.IO.Path.GetFileName(xdf)} was not found.", TuneAction.OpenXdf);
        }
    }

    public MainWindowViewModel Live { get; }

    public TuneViewModel Tune { get; }

    public CompareViewModel Compare { get; }

    public PerformanceViewModel Performance { get; }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetField(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsLive));
                OnPropertyChanged(nameof(IsTune));
                OnPropertyChanged(nameof(IsCompare));
                OnPropertyChanged(nameof(IsPerformance));
                OnPropertyChanged(nameof(ConnectIsPrimary));
                OnPropertyChanged(nameof(SaveIsPrimary));
                Tune.LiveCursor.IsVisible = IsTune;
                Compare.IsVisible = IsCompare;
                Performance.IsVisible = IsPerformance;
            }
        }
    }

    public bool IsLive
    {
        get => SelectedTab == LiveTab;
        set
        {
            if (value)
            {
                SelectedTab = LiveTab;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsTune
    {
        get => SelectedTab == TuneTab;
        set
        {
            if (value)
            {
                SelectedTab = TuneTab;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsCompare
    {
        get => SelectedTab == CompareTab;
        set
        {
            if (value)
            {
                SelectedTab = CompareTab;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsPerformance
    {
        get => SelectedTab == PerformanceTab;
        set
        {
            if (value)
            {
                SelectedTab = PerformanceTab;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The theme in effect, also while it follows Windows. Pressing Night or Day fixes the theme and saves it.</summary>
    public bool IsNight
    {
        get => Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        set => PressTheme(ThemeMode.Night, value);
    }

    public bool IsDay
    {
        get => !IsNight;
        set => PressTheme(ThemeMode.Day, value);
    }

    /// <summary>Live's readout band: tiles (the default) or dials. The status bar and Settings both switch it.</summary>
    public bool IsTiles
    {
        get => Tune.Settings.Readouts == ReadoutsMode.Tiles;
        set => SetReadouts(value ? ReadoutsMode.Tiles : null);
    }

    public bool IsDials
    {
        get => Tune.Settings.Readouts == ReadoutsMode.Dials;
        set => SetReadouts(value ? ReadoutsMode.Dials : null);
    }

    public event EventHandler? ReadoutsChanged;

    /// <summary>Raised after a setting changed and was applied, for screens that read one (the Live trend length).</summary>
    public event EventHandler? SettingsApplied;

    /// <summary>A setting changed in the Settings dialog: apply it wherever it shows (checkpoint M8: one place).</summary>
    public void SettingsChanged()
    {
        Tune.SettingsChanged();
        Tune.LiveCursor.Trail = Tune.Settings.CursorTrail;
        ApplyLiveSettings();
        SettingsApplied?.Invoke(this, EventArgs.Empty);
    }

    // Live reads the trend length, the log folder and the CSV switch's start from Settings (checkpoint M8).
    private void ApplyLiveSettings()
    {
        Live.TrendSeconds = Tune.Settings.TrendSeconds;
        Live.LogFolder = Tune.Settings.LogFolder;
        Live.StartCsvOnConnect = Tune.Settings.StartCsvOnConnect;
    }

    /// <summary>
    /// Connect is the screen's one primary action on Live and on Performance, where a test needs the link, and only until
    /// connected (system 5.1, performance spec 9.1); on Performance a finished run to save keeps Save result the primary.
    /// </summary>
    public bool ConnectIsPrimary => (IsLive || (IsPerformance && !Performance.Run.CanSave)) && !Live.IsConnected;

    /// <summary>Save is the primary action on Tune and Compare; on Live Connect is, on Performance Connect or the test's Arm.</summary>
    public bool SaveIsPrimary => IsTune || IsCompare;

    // A lit segment pressed again sends false. That changes nothing, except while the theme follows Windows, where
    // pressing the theme in effect fixes it.
    private void PressTheme(ThemeMode mode, bool on)
    {
        var lit = mode == ThemeMode.Night ? IsNight : !IsNight;
        if (on || (lit && Tune.Settings.Theme == ThemeMode.FollowWindows))
        {
            Tune.Settings.Theme = mode;
            Tune.Settings.Save();
            App.SetTheme(mode);
        }

        OnPropertyChanged(nameof(IsNight));
        OnPropertyChanged(nameof(IsDay));
    }

    private void SetReadouts(ReadoutsMode? mode)
    {
        if (mode is { } chosen && chosen != Tune.Settings.Readouts)
        {
            Tune.Settings.Readouts = chosen;
            Tune.Settings.Save();
            Live.Readouts = chosen;
            ReadoutsChanged?.Invoke(this, EventArgs.Empty);
        }

        OnPropertyChanged(nameof(IsTiles));
        OnPropertyChanged(nameof(IsDials));
    }
}
