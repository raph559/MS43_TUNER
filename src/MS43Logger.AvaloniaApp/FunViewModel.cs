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
