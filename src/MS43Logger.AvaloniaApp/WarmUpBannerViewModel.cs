using System;
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One warm-up or condition banner on Live and Performance (performance spec 5): red with the warning icon for danger (a cold
/// engine, the overheat lamp), amber for caution (cold or hot oil, hot coolant), grey with the info icon for a note.
/// </summary>
public sealed class WarmUpBannerViewModel : ViewModelBase
{
    private WarmUpBanner _banner;

    public WarmUpBannerViewModel(WarmUpBanner banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        _banner = banner;
    }

    public WarmUpBanner Banner => _banner;

    /// <summary>"coolant-cold", "oil-cold", "oil-unreported", "coolant-hot", "oil-hot" or "overheat".</summary>
    public string Code => _banner.Code;

    public string Text => _banner.Text;

    public bool IsDanger => _banner.Level == WarmUpLevel.Danger;

    public bool IsCaution => _banner.Level == WarmUpLevel.Caution;

    public bool IsInfo => _banner.Level == WarmUpLevel.Info;

    /// <summary>A new reading for the same banner: the text changes in place, so the banner does not slide in again.</summary>
    internal void Update(WarmUpBanner banner)
    {
        if (banner.Code != _banner.Code || banner.Level != _banner.Level)
        {
            throw new ArgumentException("Only the same banner's reading can change in place.", nameof(banner));
        }

        _banner = banner;
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(Text));
    }
}
