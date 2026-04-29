using System.Linq;

namespace MS43Logger.AvaloniaApp;

/// <summary>The window: header tabs, theme, and the two screens.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    public const string LiveTab = "Live";
    public const string TuneTab = "Tune";
    public const string CompareTab = "Compare";

    private string _selectedTab = LiveTab;
    private bool _isNight = true;

    public ShellViewModel(MainWindowViewModel live, TuneViewModel tune)
    {
        Live = live;
        Tune = tune;
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
                Tune.LiveCursor.IsVisible = IsTune;
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
}
