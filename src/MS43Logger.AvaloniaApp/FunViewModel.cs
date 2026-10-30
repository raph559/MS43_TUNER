using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public enum FunMode { None, Bin, Live }

/// <summary>
/// A feature in the Fun tab's list: its key (<see cref="FunFeatures"/>), title and what its switch reads. Every feature is
/// listed; one the bin or the ECU does not have is greyed and says why (<see cref="Unavailable"/>).
/// </summary>
public sealed class FunFeature(string key, string title) : ViewModelBase
{
    private string _switchText = string.Empty;
    private string? _unavailable;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string SwitchText { get => _switchText; internal set => SetField(ref _switchText, value); }
    public string? Unavailable { get => _unavailable; internal set { if (SetField(ref _unavailable, value)) OnPropertyChanged(nameof(IsAvailable)); } }
    public bool IsAvailable => Unavailable is null;
}

/// <summary>
/// The Fun tab: launch control, no-lift shift, rolling anti-lag, hardcut, pops and the downshift blip in one place. It holds
/// no data of its own. With a verified overlay it edits the live page's copy and sends through the live page; otherwise it
/// edits the open bin through Tune's history, and undoes an edit the firmware would refuse. A feature's page shows every
/// group of it (<see cref="FunFeatures.MapIndicesOf"/>) that the ECU or the bin has.
/// </summary>
public sealed class FunViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        [FunFeatures.LaunchControl] = "Launch control",
        [FunFeatures.NoLiftShift] = "No-lift shift",
        [FunFeatures.RollingAntiLag] = "Rolling anti-lag",
        [FunFeatures.Hardcut] = "Hardcut",
        [FunFeatures.Pops] = "Pops & Bangs",
        [FunFeatures.Blip] = "Downshift blip",
    };

    private readonly TuneViewModel _tune;
    private FunFeature? _selected;
    private FunMode _mode;
    private string _modeText = string.Empty;
    private string? _problem;
    private bool _isVisible;

    public FunViewModel(TuneViewModel tune, LiveTuningViewModel live)
    {
        _tune = tune;
        LiveTuning = live;
        OfflineGears = new ObservableCollection<LimiterRpmRow>(Enumerable.Range(0, 6).Select(i => new LimiterRpmRow(i)));
        foreach (var key in FunFeatures.Order) Features.Add(new FunFeature(key, Titles[key]));
        live.PropertyChanged += OnLiveChanged;
        live.PageRebuilt += OnPageRebuilt;
        tune.PropertyChanged += OnTuneChanged;
        Refresh();
    }

    public LiveTuningViewModel LiveTuning { get; }
    public ObservableCollection<FunFeature> Features { get; } = new();
    public ObservableCollection<LiveScalarRow> ScalarRows { get; } = new();
    public ObservableCollection<LimiterRpmRow> OfflineGears { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (SetField(ref _isVisible, value) && value) Refresh(); }
    }

    public FunMode Mode { get => _mode; private set { if (SetField(ref _mode, value)) { OnPropertyChanged(nameof(IsLive)); OnPropertyChanged(nameof(IsBin)); } } }
    public bool IsLive => Mode == FunMode.Live;
    public bool IsBin => Mode == FunMode.Bin;
    public string ModeText { get => _modeText; private set => SetField(ref _modeText, value); }
    public string? Problem { get => _problem; private set => SetField(ref _problem, value); }

    public FunFeature? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                Problem = null;
                RebuildRows();
            }
        }
    }

    public bool IsHardcut => Selected is { IsAvailable: true, Key: FunFeatures.Hardcut };
    public bool IsGroup => Selected is { IsAvailable: true } && !IsHardcut;
    public string? Unavailable => Selected?.Unavailable;
    public bool ShowsSaveHint => IsBin && Selected is { IsAvailable: true };
    public string? Note => Selected?.Key is { } key && FunFeatures.MapIndexOf(key) is { } index
        ? EditedMaps.FirstOrDefault(m => m.Index == index)?.ScalarGroup?.Note : null;

    // The current overlay's definitions; a bin without it has only LC, NLS, RAL and the hardcut (FunFeatures.Offline).
    private static IReadOnlyList<OverlayMap> BinMaps => OverlayCatalogue.Maps;

    /// <summary>The catalogue of what is edited: the ECU's verified one when connected, else the open bin's.</summary>
    private IEnumerable<OverlayMap> EditedMaps => Mode == FunMode.Live ? LiveTuning.Maps.Select(m => m.Map) : BinMaps;

    /// <summary>The live rows of the chosen feature's groups, its main group first.</summary>
    private IReadOnlyList<LiveMapRow> LiveRows => Selected?.Key is { } key
        ? FunFeatures.MapIndicesOf(key).Select(index => LiveTuning.Maps.FirstOrDefault(m => m.Map.Index == index && !m.Map.Reserved)).OfType<LiveMapRow>().ToArray()
        : [];

    /// <summary>
    /// The live A/B for the chosen feature. It reads the main group, whose byte 0 is the feature's switch: the feature is
    /// live while that group is on. Setting it switches every group the ECU holds: on uses the sent copies, off the flashed
    /// values.
    /// </summary>
    public bool IsOn
    {
        get => LiveRows is [var main, ..] && main.IsOn;
        set
        {
            foreach (var row in LiveRows.Where(row => row.CanSwitch)) row.IsOn = value;
            OnPropertyChanged();
        }
    }

    public bool CanSwitch => IsLive && LiveRows is [{ CanSwitch: true }, ..];

    /// <summary>Re-reads the mode, which features are available and each switch; keeps the chosen feature.</summary>
    public void Refresh() => Refresh(rebuild: true);

    private void Refresh(bool rebuild)
    {
        var mode = Mode;
        var bin = _tune.Document;
        var live = LiveTuning.HasVerifiedCatalogue && LiveTuning.ReadOnlyReason is null && LiveTuning.Page is not null;
        Mode = live ? FunMode.Live : bin is not null ? FunMode.Bin : FunMode.None;
        ModeText = Mode switch
        {
            FunMode.Live => $"Car connected · editing live (overlay V{LiveTuning.OverlayLabel})",
            FunMode.Bin => $"No car · editing the open bin {System.IO.Path.GetFileName(bin!.Path) ?? "(unsaved)"}"
                + (LiveTuning.ReadOnlyReason is { } why && why != "Not connected." && why != "Open the car's bin on the Tune tab." ? $" · live unavailable: {why}" : string.Empty),
            _ => "Open the car's bin on the Tune tab.",
        };
        var keys = Mode switch
        {
            FunMode.Live => FunFeatures.Live(LiveTuning.Maps.Where(m => m.Map.ScalarGroup is not null
                && LiveScalarDefinitions.ConstantsFor(m.Map, bin!.Xdf) is not null).Select(m => m.Map.Index), LiveTuning.HasVerifiedCatalogue),
            FunMode.Bin => FunFeatures.Offline(bin!),
            _ => Array.Empty<string>(),
        };
        var changed = false;
        foreach (var feature in Features)
        {
            var why = keys.Contains(feature.Key) ? null : WhyUnavailable(feature.Key, bin);
            changed |= feature.Unavailable != why;
            feature.Unavailable = why;
        }
        if (!changed && !rebuild && Mode == mode)
        {
            // Nothing structural changed (the live page refreshes every second): keep the rows, so typing and messages survive.
            foreach (var feature in Features) feature.SwitchText = SwitchText(feature);
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(CanSwitch));
            return;
        }
        foreach (var feature in Features) feature.SwitchText = SwitchText(feature);
        // The chosen feature stays chosen, greyed or not, so its reason stays in view; the first pick is the first available.
        var keep = _selected ?? Features.FirstOrDefault(f => f.IsAvailable) ?? Features[0];
        if (!ReferenceEquals(keep, _selected)) Selected = keep;
        else RebuildRows();
    }

    /// <summary>Why a feature is greyed: what the ECU or the bin lacks, and what would bring it.</summary>
    private string WhyUnavailable(string key, CalibrationDocument? bin)
    {
        if (Mode == FunMode.None) return "Open the car's bin on the Tune tab, or connect the car.";
        if (key is FunFeatures.Pops or FunFeatures.Blip)
        {
            var (what, need, them) = key == FunFeatures.Pops ? ("pops", "need", "them") : ("blip", "needs", "it");
            if (Mode == FunMode.Live) return $"The loaded XDF and the ECU's overlay do not agree on the {what} settings.";
            return FunFeatures.OverlayOf(bin!) switch
            {
                null => $"This bin has no live overlay; the {what} {need} V{OverlayProtocol.Label}. Open the V{OverlayProtocol.Label} bin to set {them}.",
                { } other when !OverlayProtocol.IsCurrent(other) =>
                    $"This bin has overlay V{other.Label}; the app works with V{OverlayProtocol.Label} only. Open the V{OverlayProtocol.Label} bin to set the {what}.",
                _ => $"The {what} settings could not be read from this bin.",
            };
        }
        if (key == FunFeatures.Hardcut)
            return Mode == FunMode.Live ? "The ECU's overlay has no live limiter."
                : "The loaded XDF needs the manual gear limits, limiter hysteresis and cut-mode definitions.";
        return Mode == FunMode.Live ? "The ECU's overlay does not have this group, or the loaded XDF does not define it."
            : "The loaded XDF does not define every setting of this group.";
    }

    private CalibrationDocument? Edited => Mode switch { FunMode.Live => LiveTuning.Page, FunMode.Bin => _tune.Document, _ => null };

    private string SwitchText(FunFeature feature)
    {
        if (!feature.IsAvailable) return "Unavailable";
        var key = feature.Key;
        if (Edited is not { } document) return string.Empty;
        var bytes = document.Image.Bytes;
        if (key == FunFeatures.Hardcut)
            return (Mode == FunMode.Live ? LiveTuning.LimiterMode == 1 : bytes[LimiterInBin.ModeAt] != 0) ? "Ignition cut" : "Fuel cut";
        var index = FunFeatures.MapIndexOf(key)!.Value;
        if (EditedMaps.FirstOrDefault(m => m.Index == index) is not { } map) return string.Empty;
        var value = bytes[map.Address & 0x7FFFF];
        if (key is FunFeatures.Pops or FunFeatures.Blip && OverlayTriggers.Pops.FirstOrDefault(trigger => trigger.Value == value) is { Short: { } named })
            return named;
        if (OverlayProtocol.IsCurrent(bytes)
            && OverlayTriggers.For(index).FirstOrDefault(trigger => trigger.Value == value) is { Short: { } trigger })
            return trigger;
        return value switch { 0 => "Off", 1 => "On", 2 => "Cruise", _ => value.ToString() };
    }

    private void RebuildRows()
    {
        foreach (var row in ScalarRows) row.Dispose();
        ScalarRows.Clear();
        if (Selected is { IsAvailable: true, Key: var key })
        {
            foreach (var index in FunFeatures.MapIndicesOf(key))
            {
                var map = EditedMaps.FirstOrDefault(m => m.Index == index && !m.Reserved);
                IReadOnlyList<LiveScalarRow>? rows = null;
                if (map is not null && Mode == FunMode.Live) rows = LiveTuning.BuildScalarRows(map);
                else if (map is not null && Mode == FunMode.Bin && _tune.Document is { } bin && _tune.History is { } history
                    && LiveScalarDefinitions.ConstantsFor(map, bin.Xdf) is { } constants)
                {
                    rows = LiveScalarRow.Build(bin, history, map, constants);
                    foreach (var row in rows) row.Editor.Guard = plan => Refusal(bin, map, plan);
                }
                foreach (var row in rows ?? Array.Empty<LiveScalarRow>()) ScalarRows.Add(row);
            }
        }
        if (IsHardcut && Mode == FunMode.Bin && _tune.Document is { } document)
        {
            var settings = LimiterInBin.Read(document.Image);
            for (var i = 0; i < 6; i++) OfflineGears[i].Rpm = (settings.GearLimits[i] * 32).ToString();
            OfflineHysteresis = (settings.Hysteresis * 32).ToString();
            OfflineMode = settings.IgnitionCut ? 1 : 0;
        }
        OnPropertyChanged(nameof(IsHardcut));
        OnPropertyChanged(nameof(IsGroup));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Unavailable));
        OnPropertyChanged(nameof(ShowsSaveHint));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(CanSwitch));
    }

    // ---- connected ----

    /// <summary>
    /// Sends the chosen feature's groups once every one of them passes the checks, the main group (the feature's switch)
    /// last, so a refusal never leaves the feature switched on with settings it was not sent with. A refusal stops the
    /// sending, and the message says which groups went live.
    /// </summary>
    public async Task SendAsync()
    {
        if (!IsLive || LiveRows is not { Count: > 0 } rows) return;
        if (rows.Select(LiveTuning.RefusalFor).OfType<string>().FirstOrDefault() is { } refused)
        {
            Problem = refused;
            return;
        }
        var live = new List<string>();
        foreach (var row in rows.Reverse())
        {
            if (!await LiveTuning.SendAsync(row))
            {
                var problem = LiveTuning.Problem ?? "Not sent: the ECU did not take it.";
                Problem = live.Count == 0 ? problem : $"{problem} {string.Join(" and ", live)} went live; the rest, the switch included, was not sent.";
                Refresh(rebuild: false);
                return;
            }
            live.Add(row.Title);
        }
        Problem = null;
        Refresh(rebuild: false);
    }

    public async Task WriteToBinAsync()
    {
        Problem = null;
        if (!IsLive) return;
        if (IsHardcut)
        {
            LiveTuning.CopyLimiterToTune();
            Problem = LiveTuning.Problem;
        }
        else if (LiveRows is { Count: > 0 } rows)
        {
            var sent = rows.Where(row => row.State != OverlayState.Free).ToArray();
            if (sent.Length == 0) { Problem = "Send these settings before writing them into the bin."; return; }
            Problem = await LiveTuning.CopyToTuneAsync(sent) ?? "Written into the bin: save it with the Save button at the top.";
        }
    }

    public async Task SendLimiterAsync()
    {
        await LiveTuning.SendLimiterAsync();
        Problem = LiveTuning.Problem;
    }

    // ---- offline ----

    private string _offlineHysteresis = string.Empty;
    private int _offlineMode;
    public string OfflineHysteresis { get => _offlineHysteresis; set => SetField(ref _offlineHysteresis, value); }
    public int OfflineMode { get => _offlineMode; set => SetField(ref _offlineMode, value); }

    /// <summary>Writes the hardcut fields into the open bin: RPM in multiples of 32 up to 8160, each limit above the hysteresis.</summary>
    public void ApplyOfflineLimiter()
    {
        Problem = null;
        if (!IsBin || _tune.Document is not { } bin || _tune.History is not { } history) return;
        if (LimiterInBin.Find(bin.Xdf) is not { } items)
        {
            Problem = "The loaded XDF needs the manual gear limits, limiter hysteresis and cut-mode definitions.";
            return;
        }
        try
        {
            static byte Raw(string value)
            {
                if (!int.TryParse(value, out var rpm) || rpm < 0 || rpm > 8160 || rpm % 32 != 0)
                    throw new ArgumentException("RPM values must be multiples of 32, between 0 and 8160.");
                return (byte)(rpm / 32);
            }
            var settings = new LimiterSettings(OfflineGears.Select(g => Raw(g.Rpm)).ToArray(), Raw(OfflineHysteresis), OfflineMode == 1, false);
            var before = history.UndoCount;
            LimiterInBin.Write(bin, history, items, settings, "Set hardcut");
            _limiterSteps = history.UndoCount - before;
            Refresh();
        }
        catch (Exception error) when (error is ArgumentException or EditRefusedException) { Problem = error.Message; }
    }

    private int _limiterSteps;

    public void Undo()
    {
        if (IsLive) LiveTuning.Undo();
        else if (IsBin && IsHardcut && _limiterSteps > 0)
        {
            // One hardcut change can write three items (gears, hysteresis, mode): undo all of them.
            for (; _limiterSteps > 0; _limiterSteps--) _tune.Undo();
            Refresh();
        }
        else if (IsBin) _tune.Undo();
    }

    public void Redo() { if (IsLive) LiveTuning.Redo(); else if (IsBin) _tune.Redo(); }

    /// <summary>
    /// Offline, an edit of a group is checked before it is applied: the group as it would become must be what the bin's
    /// overlay accepts (pops limits, LC/NLS/RAL modes, V10's stab and blip limits). The bin's current bytes stand for the
    /// flashed ones, so the RAL neighbour is free.
    /// </summary>
    private static string? Refusal(CalibrationDocument bin, OverlayMap map, EditPlan plan)
    {
        var at = map.Address & 0x7FFFF;
        var current = bin.Image.Bytes.Slice(at, 8).ToArray();
        var after = current.ToArray();
        if (plan.Item is XdfConstant { Data.Address: { } address } && plan.Changes.Count > 0)
        {
            var offset = BinImage.ToOffset(address) - at;
            if (offset is >= 0 and < 8) after[offset] = (byte)plan.Changes[0].NewRaw;
        }
        var refused = OverlayProtocol.IsCurrent(bin.Image.Bytes) ? OverlaySettings.ValidateCopy(map.Index, after, current) : OverlayV8.ValidateCopy(map.Index, after, current);
        if (refused is null) return null;
        return (OverlaySettings.Refusal(map.Index, after) ?? "That activation mode is not supported") + ". Nothing was changed.";
    }

    private void OnLiveChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveTuningViewModel.HasVerifiedCatalogue) or nameof(LiveTuningViewModel.ReadOnlyReason)
            or nameof(LiveTuningViewModel.IsReadOnly) or nameof(LiveTuningViewModel.Maps) or nameof(LiveTuningViewModel.LimiterMode))
            Refresh(rebuild: false);
    }

    private void OnPageRebuilt(object? sender, EventArgs e) => Refresh();

    private void OnTuneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TuneViewModel.Document) or nameof(TuneViewModel.History)) Refresh();
    }

    public void Dispose()
    {
        LiveTuning.PropertyChanged -= OnLiveChanged;
        LiveTuning.PageRebuilt -= OnPageRebuilt;
        _tune.PropertyChanged -= OnTuneChanged;
        foreach (var row in ScalarRows) row.Dispose();
        ScalarRows.Clear();
    }
}
