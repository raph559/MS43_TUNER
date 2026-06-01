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

    public bool IsTiles { get => _shell.IsTiles; set => _shell.IsTiles = value; }

    public bool IsDials { get => _shell.IsDials; set => _shell.IsDials = value; }

    /// <summary>"Not saved: …" while settings.json cannot be written; null once it can.</summary>
    public string? SaveError => _settings.SaveError is { } reason
        ? $"Not saved: {reason.TrimEnd().TrimEnd('.')}. Your choices apply until you close the app."
        : null;

    public bool HasSaveError => SaveError is not null;
}
