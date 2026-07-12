using System.ComponentModel;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// The Settings dialog. Every choice applies at once and is written to settings.json; there is no Apply
/// or Cancel. The status bar's Night / Day and Tiles / Dials switch the same settings, so both always agree.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly ShellViewModel _shell;

    public SettingsViewModel(AppSettings settings, ShellViewModel shell)
    {
        _settings = settings;
        _shell = shell;
        shell.PropertyChanged += OnShellChanged;
    }

    public bool IsNightTheme { get => _settings.Theme == ThemeMode.Night; set => SetTheme(ThemeMode.Night, value); }

    public bool IsDayTheme { get => _settings.Theme == ThemeMode.Day; set => SetTheme(ThemeMode.Day, value); }

    public bool IsFollowWindows { get => _settings.Theme == ThemeMode.FollowWindows; set => SetTheme(ThemeMode.FollowWindows, value); }

    public bool IsTiles { get => _shell.IsTiles; set => _shell.IsTiles = value; }

    public bool IsDials { get => _shell.IsDials; set => _shell.IsDials = value; }

    public bool IsTrend10 { get => _settings.TrendSeconds == 10; set => SetTrend(10, value); }

    public bool IsTrend30 { get => _settings.TrendSeconds == 30; set => SetTrend(30, value); }

    public bool IsTrend60 { get => _settings.TrendSeconds == 60; set => SetTrend(60, value); }

    /// <summary>
    /// The folder for new, automatically named CSV logs, written when the field is left (not per keystroke). An empty
    /// value keeps the saved folder (checkpoint M9); the view shows it again.
    /// </summary>
    public string LogFolder
    {
        get => _settings.LogFolder;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != _settings.LogFolder)
            {
                _settings.LogFolder = value.Trim();
                Save();
            }

            OnPropertyChanged();
        }
    }

    public bool StartCsvOnConnect { get => _settings.StartCsvOnConnect; set => Set(value, _settings.StartCsvOnConnect, v => _settings.StartCsvOnConnect = v); }

    public bool IsPhysical { get => !_settings.OpenMapsRaw; set => SetMaps(raw: false, value); }

    public bool IsRaw { get => _settings.OpenMapsRaw; set => SetMaps(raw: true, value); }

    public bool IsTrailOff { get => _settings.CursorTrail == TrailMode.Off; set => SetTrail(TrailMode.Off, value); }

    public bool IsTrailShort { get => _settings.CursorTrail == TrailMode.Short; set => SetTrail(TrailMode.Short, value); }

    public bool IsTrailLong { get => _settings.CursorTrail == TrailMode.Long; set => SetTrail(TrailMode.Long, value); }

    public bool ReopenLastBin { get => _settings.ReopenLastBin; set => Set(value, _settings.ReopenLastBin, v => _settings.ReopenLastBin = v); }

    public bool KeepBackup { get => _settings.KeepBackup; set => Set(value, _settings.KeepBackup, v => _settings.KeepBackup = v); }

    public bool AppendEditLog { get => _settings.AppendEditLog; set => Set(value, _settings.AppendEditLog, v => _settings.AppendEditLog = v); }

    /// <summary>"Not saved: …" while settings.json cannot be written; null once it can.</summary>
    public string? SaveError => _settings.SaveError is { } reason
        ? $"Not saved: {reason.TrimEnd().TrimEnd('.')}. Your choices apply until you close the app."
        : null;

    public bool HasSaveError => SaveError is not null;

    public void TryAgain() => Save();

    private void Set(bool value, bool current, System.Action<bool> apply)
    {
        if (value != current)
        {
            apply(value);
            Save();
        }
    }

    // A segment pressed while lit sends false; the choice stays, and the segment is told to show it again.
    private void SetTheme(ThemeMode mode, bool on)
    {
        if (on && _settings.Theme != mode)
        {
            _settings.Theme = mode;
            App.SetTheme(mode);
            Save();
        }

        OnPropertyChanged(nameof(IsNightTheme));
        OnPropertyChanged(nameof(IsDayTheme));
        OnPropertyChanged(nameof(IsFollowWindows));
    }

    private void SetTrend(int seconds, bool on)
    {
        if (on && _settings.TrendSeconds != seconds)
        {
            _settings.TrendSeconds = seconds;
            Save();
        }

        OnPropertyChanged(nameof(IsTrend10));
        OnPropertyChanged(nameof(IsTrend30));
        OnPropertyChanged(nameof(IsTrend60));
    }

    private void SetMaps(bool raw, bool on)
    {
        if (on && _settings.OpenMapsRaw != raw)
        {
            _settings.OpenMapsRaw = raw;
            Save();
        }

        OnPropertyChanged(nameof(IsPhysical));
        OnPropertyChanged(nameof(IsRaw));
    }

    private void SetTrail(TrailMode mode, bool on)
    {
        if (on && _settings.CursorTrail != mode)
        {
            _settings.CursorTrail = mode;
            Save();
        }

        OnPropertyChanged(nameof(IsTrailOff));
        OnPropertyChanged(nameof(IsTrailShort));
        OnPropertyChanged(nameof(IsTrailLong));
    }

    private void Save()
    {
        _settings.Save();
        _shell.SettingsChanged();
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.IsNight) or nameof(ShellViewModel.IsDay):
                OnPropertyChanged(nameof(IsNightTheme));
                OnPropertyChanged(nameof(IsDayTheme));
                OnPropertyChanged(nameof(IsFollowWindows));
                OnPropertyChanged(nameof(SaveError));
                OnPropertyChanged(nameof(HasSaveError));
                break;
            case nameof(ShellViewModel.IsTiles) or nameof(ShellViewModel.IsDials):
                OnPropertyChanged(nameof(IsTiles));
                OnPropertyChanged(nameof(IsDials));
                OnPropertyChanged(nameof(SaveError));
                OnPropertyChanged(nameof(HasSaveError));
                break;
        }
    }
}
