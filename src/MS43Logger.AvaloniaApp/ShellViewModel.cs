using System;
using System.Linq;
using Avalonia;
using Avalonia.Styling;

namespace MS43Logger.AvaloniaApp;

/// <summary>The window: header tabs, theme, and the two screens.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    public const string LiveTab = "Live";
    public const string TuneTab = "Tune";
    public const string CompareTab = "Compare";

    private string _selectedTab = LiveTab;
    private bool _isNight = true;
    private System.Threading.Tasks.Task? _start;

    public ShellViewModel(MainWindowViewModel live, TuneViewModel tune)
    {
        Live = live;
        Tune = tune;
        Compare = new CompareViewModel(tune);
        Compare.ShowInTuneRequested += (_, item) =>
        {
            tune.SelectedItem = item;
            SelectedTab = TuneTab;
        };
        App.SetTheme("Dark");

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

            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected) && !live.IsConnected)
            {
                tune.LiveCursor.Clear();
            }
        };

        live.SampleShown += (_, sample) => tune.LiveCursor.Push(sample, live.CurrentDefinition);
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
                OnPropertyChanged(nameof(ConnectIsPrimary));
                OnPropertyChanged(nameof(SaveIsPrimary));
                Tune.LiveCursor.IsVisible = IsTune;
                Compare.IsVisible = IsCompare;
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

    public bool IsNight
    {
        get => _isNight;
        set
        {
            if (SetField(ref _isNight, value))
            {
                App.SetTheme(value ? "Dark" : "Light");
                OnPropertyChanged(nameof(IsDay));
            }
        }
    }

    public bool IsDay
    {
        get => !IsNight;
        set => IsNight = !value;
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

    /// <summary>Connect is the screen's one primary action only on Live, and only until connected (system 5.1).</summary>
    public bool ConnectIsPrimary => IsLive && !Live.IsConnected;

    /// <summary>Save is the primary action on Tune and Compare; on Live, Connect is.</summary>
    public bool SaveIsPrimary => !IsLive;

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
