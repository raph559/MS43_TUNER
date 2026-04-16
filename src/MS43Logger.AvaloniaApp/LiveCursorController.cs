using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>What the map, curve and surface draw for the engine's position: the fix, its fading trail, and whether samples stopped.</summary>
public sealed record LiveCursorState(CursorFix Fix, IReadOnlyList<TrailPoint> Trail, bool IsStale);

/// <summary>
/// Keeps the latest logger sample and, at most <see cref="MaxRedrawsPerSecond"/> times a second, places it on the table shown in
/// the Tune tab. Samples arrive on the UI thread through the Live view model's existing post; the polling thread does no new work.
/// </summary>
public sealed class LiveCursorController : ViewModelBase
{
    public const int MaxRedrawsPerSecond = 30;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(1);
    private const string StartLogging = " Start logging to see the engine on this map.";

    private readonly TimeProvider _time;
    private readonly DateTimeOffset _start;
    private readonly CursorTrail _trail = new();
    private DispatcherTimer? _timer;
    private CalibrationDocument? _document;
    private TableEditorViewModel? _editor;
    private LiveInputMap? _map;
    private string? _mapFirmware;
    private bool _mapLoaded;
    private LiveTableBinding? _binding;
    private LiveSample? _sample;
    private AdxDefinition? _definition;
    private DateTimeOffset _sampleTime;
    private bool _pending;
    private bool _isVisible;
    private string? _readout;
    private bool _readoutIsLive;

    public LiveCursorController(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _start = _time.GetUtcNow();
    }

    /// <summary>Run <see cref="Tick"/> from a timer once samples arrive. Tests turn it off and tick by hand.</summary>
    public bool AutoTick { get; set; } = true;

    /// <summary>True while the Tune tab is shown; nothing is located or redrawn otherwise.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetField(ref _isVisible, value) && value)
            {
                _pending = _sample is not null;
            }
        }
    }

    public LiveTableBinding? Binding { get => _binding; private set => SetField(ref _binding, value); }

    public string? Readout { get => _readout; private set => SetField(ref _readout, value); }

    public bool ReadoutIsLive { get => _readoutIsLive; private set => SetField(ref _readoutIsLive, value); }

    private LiveInputMap? MapFor(CalibrationDocument document)
    {
        var firmware = document.Firmware.SoftwareId;
        if (!_mapLoaded || !string.Equals(firmware, _mapFirmware, StringComparison.OrdinalIgnoreCase))
        {
            _map = LiveInputMap.LoadBundled(firmware);
            _mapFirmware = firmware;
            _mapLoaded = true;
        }

        return _map;
    }

    private void ShowIdleReadout()
    {
        ReadoutIsLive = false;
        Readout = Binding switch
        {
            null => null,
            { HasInput: false } => Binding.Reason,
            _ => Binding.Describe() + StartLogging,
        };
    }

    private static string Format(CursorFix fix, XdfTable table)
    {
        static string Number(double value, long raw, int? decimals) => ValueCodec.Format(value, raw, decimals, null, 16).Replace('-', '−');
        static string WithUnits(string text, string units) => units.Length > 0 ? text + " " + units : text;

        var inputs = fix.Inputs.Select(input =>
            WithUnits(Number(input.Physical, input.Input, input.DecimalPlaces), input.Units) + (input.Clamped != 0 ? " (off the map)" : string.Empty));
        var z = table.Z;
        var value = ValueCodec.Format(fix.Value, 0, z.DecimalPlaces, z.OutputType == 2 ? 2 : null, z.Data.ElementBits).Replace('-', '−');
        return string.Join(" · ", inputs) + " → " + (fix.Approximate ? "≈ " : string.Empty) + WithUnits(value, z.Units);
    }
}
