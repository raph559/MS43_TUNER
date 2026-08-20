using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Performance;

namespace MS43Logger.AvaloniaApp;

/// <summary>A choice of the "Compare against" picker; "None" has no file.</summary>
public sealed record BaselineChoice(string Text, string? FileName);

/// <summary>One gear of the speed calibration: "3rd", km/h per 1000 rpm "22.3", the overall ratio "5.18" (performance spec 6.5).</summary>
public sealed record CalibrationRow(string Gear, string KmhPer1000, string OverallRatio);

/// <summary>A ready check as the pane lists it; its tick and reading change in place as the samples come (minor review, finding 21).</summary>
public sealed class ReadyCheckRow : ViewModelBase
{
    private bool _met;
    private string _detail;

    internal ReadyCheckRow(ReadyCheck check)
    {
        Label = check.Label;
        _met = check.Met;
        _detail = check.Detail;
    }

    public string Label { get; }

    public bool Met { get => _met; private set => SetField(ref _met, value); }

    public string Detail { get => _detail; private set => SetField(ref _detail, value); }

    internal void Update(ReadyCheck check)
    {
        Met = check.Met;
        Detail = check.Detail;
    }
}
