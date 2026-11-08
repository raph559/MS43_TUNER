using System.Collections.Generic;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One of the five live maps in the Live tuning list (live tuning spec 4): its state as the ECU's last status gives it,
/// whether the page holds changes the ECU does not have, and its own on/off switch.
/// </summary>
public sealed class LiveMapRow : ViewModelBase
{
    private readonly LiveTuningViewModel _owner;
    private string _title;
    private OverlayState? _state;
    private MapIntent _intent;
    private bool _hasUnsent;
    private bool _canSwitch;
    private bool _canLoad;
    private string _capacityText = string.Empty;

    internal LiveMapRow(LiveTuningViewModel owner, OverlayMap map)
    {
        _owner = owner;
        Map = map;
        _title = map.Key;
    }

    public OverlayMap Map { get; }

    public string Group => Map.Group;

    /// <summary>The XDF table's title once a bin is open; the overlay's key before.</summary>
    public string Title { get => _title; internal set => SetField(ref _title, value); }

    private IReadOnlyList<string> _categories = new[] { MS43Logger.Core.Tuning.XdfCatalog.OtherCategory };
    private string _description = string.Empty;
    private string _detail = string.Empty;

    /// <summary>The XDF's categories for this map (its constants' for a settings group); "Other" without any.</summary>
    public IReadOnlyList<string> Categories { get => _categories; internal set => SetField(ref _categories, value); }

    /// <summary>The XDF's description, as the Tune rail shows it on hover; a settings group's lists each setting.</summary>
    public string Description
    {
        get => _description;
        internal set
        {
            if (SetField(ref _description, value))
            {
                OnPropertyChanged(nameof(HasDescription));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>The hover's last line: the shape, the size and where the map sits in the bin.</summary>
    public string Detail { get => _detail; internal set => SetField(ref _detail, value); }

    /// <summary>Everything the search finds this map by: title, XDF name, categories, description, units, address.</summary>
    internal string Haystack { get; set; } = string.Empty;

    /// <summary>The map's state in the ECU's last status; null before any status.</summary>
    public OverlayState? State
    {
        get => _state;
        internal set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsOn));
            }
        }
    }

    /// <summary>Whether the switch can do anything now: the overlay found, the engine running, the map in the ECU (Ready or On).</summary>
    public bool CanSwitch { get => _canSwitch; internal set => SetField(ref _canSwitch, value); }
    public bool CanLoad { get => _canLoad; internal set => SetField(ref _canLoad, value); }
    public string CapacityText { get => _capacityText; internal set => SetField(ref _capacityText, value); }

    /// <summary>Tells the switch its state again, so one the ECU refused goes back.</summary>
    internal void NotifyIsOn() => OnPropertyChanged(nameof(IsOn));

    public MapIntent Intent { get => _intent; internal set => SetField(ref _intent, value); }

    public bool HasUnsent
    {
        get => _hasUnsent;
        internal set
        {
            if (SetField(ref _hasUnsent, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => (State switch
    {
        null or OverlayState.Free => "flashed",
        OverlayState.On => "LIVE",
        _ => "sent, off",
    }) + (HasUnsent ? " · unsent changes" : string.Empty);

    /// <summary>The per-map A/B: on reads the edit, off the flashed map. Setting it asks the ECU; the next status shows the outcome.</summary>
    public bool IsOn
    {
        get => State == OverlayState.On;
        set
        {
            if (value != IsOn)
            {
                _ = _owner.SetOnAsync(this, value);
            }
        }
    }
}
