using System.Collections.Generic;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One of the five live maps in the Live tuning list: its state as the ECU's last status gives it,
/// whether the page holds changes the ECU does not have, and its own on/off switch.
/// </summary>
public sealed class LiveMapRow : ViewModelBase
{
    private string _title;
    private OverlayState? _state;
    private MapIntent _intent;
    private bool _hasUnsent;
    private bool _canSwitch;
    private bool _canLoad;
    private string _capacityText = string.Empty;

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

    /// <summary>Whether the switch can do anything now: the overlay found, the engine running, the map in the ECU (Ready or On).</summary>
    public bool CanSwitch { get => _canSwitch; internal set => SetField(ref _canSwitch, value); }
    public bool CanLoad { get => _canLoad; internal set => SetField(ref _canLoad, value); }
    public string CapacityText { get => _capacityText; internal set => SetField(ref _capacityText, value); }

    public MapIntent Intent { get => _intent; internal set => SetField(ref _intent, value); }
}
