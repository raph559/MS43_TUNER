using System.Linq;

namespace MS43Logger.AvaloniaApp;

/// <summary>The window: header tabs, theme, and the two screens.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    public const string LiveTab = "Live";
    public const string TuneTab = "Tune";

    private string _selectedTab = LiveTab;
    private bool _isNight = true;

    public MainWindowViewModel Live { get; }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetField(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsLive));
                OnPropertyChanged(nameof(IsTune));
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
