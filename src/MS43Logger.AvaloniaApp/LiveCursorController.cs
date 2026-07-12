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
    private const string ConnectHint = " Connect to see the engine's position on this map.";

    private readonly TimeProvider _time;
    private readonly DateTimeOffset _start;
    private CursorTrail? _trail = new(32, TimeSpan.FromSeconds(1.2));
    private TrailMode _trailMode = TrailMode.Short;
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

    /// <summary>The Settings trail: Off, Short (32 fixes over 1.2 s) or Long (96 over 3.6 s).</summary>
    public TrailMode Trail
    {
        get => _trailMode;
        set
        {
            if (SetField(ref _trailMode, value))
            {
                _trail = value switch
                {
                    TrailMode.Off => null,
                    TrailMode.Long => new CursorTrail(96, TimeSpan.FromSeconds(3.6)),
                    _ => new CursorTrail(32, TimeSpan.FromSeconds(1.2)),
                };
            }
        }
    }

    /// <summary>Follows the table on screen. A different table starts a new trail.</summary>
    public void Bind(CalibrationDocument? document, TableEditorViewModel? editor)
    {
        if (!ReferenceEquals(document, _document))
        {
            if (_document is not null)
            {
                _document.Changed -= OnDocumentChanged;
            }

            _document = document;
            if (document is not null)
            {
                document.Changed += OnDocumentChanged;
            }
        }

        if (!ReferenceEquals(editor, _editor))
        {
            if (_editor is not null)
            {
                _editor.Live = null;
            }

            _editor = editor;
            _trail?.Clear();
        }

        Rebind();
    }

    public void Push(LiveSample sample, AdxDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _sample = sample;
        _definition = definition;
        _sampleTime = _time.GetUtcNow();
        _pending = true;
        if (AutoTick && _timer is null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / MaxRedrawsPerSecond), DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        }
    }

    /// <summary>The logger stopped: drop the sample, the trail and the cursor.</summary>
    public void Clear()
    {
        _timer?.Stop();
        _timer = null;
        _sample = null;
        _definition = null;
        _pending = false;
        _trail?.Clear();
        if (_editor is not null)
        {
            _editor.Live = null;
        }

        ShowIdleReadout();
    }

    public void Tick()
    {
        if (!IsVisible || _editor is not { } editor || Binding is not { HasInput: true } binding || _sample is not { } sample || sample.Payload.IsEmpty)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var stale = now - _sampleTime > StaleAfter;
        if (!_pending)
        {
            if (stale && editor.Live is { IsStale: false } live)
            {
                editor.Live = live with { IsStale = true };
                ReadoutIsLive = false;
                Readout = Format(live.Fix, editor.Table, stale: true);
            }

            return;
        }

        _pending = false;
        var problem = _definition is null ? "No live input: the logger definition is not known." : binding.CheckLogger(_definition);
        if (problem is not null)
        {
            editor.Live = null;
            Readout = problem;
            ReadoutIsLive = false;
            return;
        }

        CursorFix fix;
        try
        {
            fix = binding.Locate(_definition!, sample.Payload.Span, editor.Values.Physical);
        }
        catch (Exception error)
        {
            // This runs from a timer during a drive: a sample that cannot be placed must never take the app down.
            editor.Live = null;
            Readout = $"No live input: the last sample could not be read ({error.Message})";
            ReadoutIsLive = false;
            return;
        }

        var at = now - _start;
        _trail?.Add(fix.Row?.Position ?? 0, fix.Col?.Position ?? 0, at);
        editor.Live = new LiveCursorState(fix, _trail?.Points(at) ?? Array.Empty<TrailPoint>(), stale);
        Readout = Format(fix, editor.Table, stale);
        ReadoutIsLive = !stale;
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Rebind();

    private void Rebind()
    {
        Binding = _document is { } document && _editor is { } editor ? LiveTableBinding.Resolve(MapFor(document), document, editor.Table) : null;
        _pending = _sample is not null;
        if (Binding is not { HasInput: true } && _editor is not null)
        {
            _editor.Live = null;
        }

        if (_sample is null || Binding is not { HasInput: true })
        {
            ShowIdleReadout();
        }
    }

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
            _ => Binding.Describe() + ConnectHint,
        };
    }

    /// <summary>
    /// "Live 2480 rpm · 112.4 mg/stk · map 27.5 °CRK" (system 7.1); "map about" for an approximate value, since Bahnschrift
    /// has no "≈" (system 3.3); then " (off the map)" or " (stale)".
    /// </summary>
    private static string Format(CursorFix fix, XdfTable table, bool stale)
    {
        static string Number(double value, long raw, int? decimals) => ValueCodec.Format(value, raw, decimals, null, 16);
        static string WithUnits(string text, string units) => units.Length > 0 ? text + " " + units : text;

        var inputs = fix.Inputs.Select(input => WithUnits(Number(input.Physical, input.Input, input.DecimalPlaces), input.Units));
        var z = table.Z;
        var value = ValueCodec.Format(fix.Value, 0, z.DecimalPlaces, z.OutputType == 2 ? 2 : null, z.Data.ElementBits);
        var text = "Live " + string.Join(" · ", inputs) + " · map " + (fix.Approximate ? "about " : string.Empty) + WithUnits(value, z.Units);
        return text + (stale ? " (stale)" : fix.Inputs.Any(input => input.Clamped != 0) ? " (off the map)" : string.Empty);
    }
}
