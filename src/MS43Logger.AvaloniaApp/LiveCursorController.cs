using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>What the map, curve and surface draw for the engine's position: the fix, its fading trail, and whether samples stopped.</summary>
public sealed record LiveCursorState(CursorFix Fix, IReadOnlyList<TrailPoint> Trail, bool IsStale)
{
    /// <summary>The ECU is not reading this table now: the cursor is drawn in Ink3 instead of Now.</summary>
    public bool IsUnused => Fix.Use.State == MapUseState.NotInUse;
}

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
    private MapUseRules? _rules;
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
    private string? _use;
    private string? _useDetail;
    private bool _useIsDim;
    private uint? _watchFor;
    private byte _watchCount;
    private DateTimeOffset _watchCountAt;

    public LiveCursorController(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _start = _time.GetUtcNow();
    }

    /// <summary>The app's one V9 watch: told when this controller's table comes to the front or leaves the screen.</summary>
    public MapWatchCoordinator? Coordinator { get; set; }

    /// <summary>The data address of the table this controller shows, for the V9 watch; null without one.</summary>
    public uint? WatchAddress => _editor?.Table.Z.Data.Address is long address and >= 0 and < 0x80000 ? (uint)address : null;

    /// <summary>The window showing this controller's table was brought to the front (a map window's focus).</summary>
    public void Focused() => Coordinator?.Activate(this);

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
                Coordinator?.Activate(this);
            }
            else if (!value)
            {
                Coordinator?.Hide(this);
            }
        }
    }

    public LiveTableBinding? Binding { get => _binding; private set => SetField(ref _binding, value); }

    public string? Readout { get => _readout; private set => SetField(ref _readout, value); }

    public bool ReadoutIsLive { get => _readoutIsLive; private set => SetField(ref _readoutIsLive, value); }

    /// <summary>"In use", "Not in use", "RON98 72 % · RON91 28 %", "Possibly in use" or "Selection unknown"; null with no cursor.</summary>
    public string? Use { get => _use; private set => SetField(ref _use, value); }

    /// <summary>Why the use is unknown, or the rule's caveat: the label's tooltip.</summary>
    public string? UseDetail { get => _useDetail; private set => SetField(ref _useDetail, value); }

    /// <summary>The label is Ink3 when the map is not in use or samples stopped.</summary>
    public bool UseIsDim { get => _useIsDim; private set => SetField(ref _useIsDim, value); }

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
            Rebind();
            if (editor is not null && IsVisible) Coordinator?.Activate(this);
            else if (editor is null) Coordinator?.Hide(this);
            return;
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
        if (!IsVisible || _editor is not { } editor || _sample is not { } sample || sample.Payload.IsEmpty)
        {
            return;
        }

        // With a V9 ECU watching this table, the record places the cursor, whatever the logger carries.
        if (sample is { Watch: { } record, WatchedAddress: { } watched } && watched == WatchAddress && Binding is { } watchedBinding)
        {
            TickWatched(editor, watchedBinding, sample, record);
            return;
        }

        if (Binding is not { HasInput: true } binding)
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
                UseIsDim = true;
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
            ClearUse();
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
            ClearUse();
            return;
        }

        var at = now - _start;
        _trail?.Add(fix.Row?.Position ?? 0, fix.Col?.Position ?? 0, at);
        editor.Live = new LiveCursorState(fix, _trail?.Points(at) ?? Array.Empty<TrailPoint>(), stale);
        Readout = Format(fix, editor.Table, stale);
        ReadoutIsLive = !stale;
        Use = fix.Use.Text;
        UseDetail = fix.Use.Detail ?? binding.Use.Rule?.Caveat;
        UseIsDim = stale || fix.Use.State == MapUseState.NotInUse;
    }

    /// <summary>
    /// The record's cell. While its count moves the use line is the rules' text where they say the table is used, else
    /// "In use", except that a table whose share of its blend is 0 % stays "Not in use", dimmed: the count proves a read,
    /// not a use. When the count has not moved for <see cref="StaleAfter"/>, "Not read now" with the time since the last
    /// read, and the last read's cell dimmed; "not read since connect" before any read.
    /// </summary>
    private void TickWatched(TableEditorViewModel editor, LiveTableBinding binding, LiveSample sample, WatchRecord record)
    {
        var now = _time.GetUtcNow();
        if (_watchFor != sample.WatchedAddress)
        {
            (_watchFor, _watchCount, _watchCountAt) = (sample.WatchedAddress, record.Count, now);
            _trail?.Clear();
        }
        else if (record.Count != _watchCount)
        {
            (_watchCount, _watchCountAt) = (record.Count, now);
        }

        if (record.Kind == WatchKind.None)
        {
            editor.Live = null;
            Readout = NotReadSinceConnect;
            ReadoutIsLive = false;
            (Use, UseDetail, UseIsDim) = (NotReadSinceConnect, null, true);
            _pending = false;
            return;
        }

        CursorFix fix;
        try
        {
            fix = binding.LocateWatched(record, editor.Values.Physical);
        }
        catch (Exception error)
        {
            editor.Live = null;
            Readout = $"No live input: the watch record could not be placed ({error.Message})";
            ReadoutIsLive = false;
            ClearUse();
            return;
        }

        var reading = now - _watchCountAt <= StaleAfter;
        var zero = reading && _definition is not null ? binding.Use.ZeroShare(_definition, sample.Payload.Span) : null;
        if (zero is not null)
        {
            fix = fix with { Use = zero };                              // read, but counted for nothing: drawn as unused
        }

        var at = now - _start;
        if (_pending) _trail?.Add(fix.Row?.Position ?? 0, fix.Col?.Position ?? 0, at);
        _pending = false;
        editor.Live = new LiveCursorState(fix, _trail?.Points(at) ?? Array.Empty<TrailPoint>(), !reading);
        Readout = Format(fix, editor.Table, !reading);
        ReadoutIsLive = reading;
        if (zero is not null)
        {
            Use = zero.Text;
            UseDetail = ZeroShareDetail;
            UseIsDim = true;
        }
        else if (reading)
        {
            var rules = _definition is null ? null : binding.Use.Evaluate(_definition, sample.Payload.Span);
            Use = rules is { State: MapUseState.InUse or MapUseState.Possible } ? rules.Text : "In use";
            UseDetail = rules is { State: MapUseState.InUse or MapUseState.Possible } ? rules.Detail ?? binding.Use.Rule?.Caveat : null;
            UseIsDim = false;
        }
        else
        {
            Use = $"Not read now \u00B7 last read {(int)Math.Round((now - _watchCountAt).TotalSeconds)} s ago";
            (UseDetail, UseIsDim) = (null, true);
        }
    }

    private const string NotReadSinceConnect = "Not read now \u00B7 not read since connect";

    private const string ZeroShareDetail = "The ECU reads this map, but its share of the blend is 0 %, so it changes nothing.";

    private void ClearUse()
    {
        Use = null;
        UseDetail = null;
        UseIsDim = false;
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Rebind();

    private void Rebind()
    {
        Binding = _document is { } document && _editor is { } editor ? LiveTableBinding.Resolve(MapFor(document), document, editor.Table, _rules) : null;
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
            _rules = MapUseRules.LoadBundled(firmware);
            _mapFirmware = firmware;
            _mapLoaded = true;
        }

        return _map;
    }

    private void ShowIdleReadout()
    {
        ClearUse();
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

        var inputs = fix.Inputs.Select(input => WithUnits(Number(input.Physical, input.Input, input.DecimalPlaces), input.Units)).ToList();
        var z = table.Z;
        var value = ValueCodec.Format(fix.Value, 0, z.DecimalPlaces, z.OutputType == 2 ? 2 : null, z.Data.ElementBits);
        // A watched read has no logged inputs: the record gives the cell itself.
        var where = inputs.Count > 0 ? string.Join(" · ", inputs) : "exact cell";
        var text = "Live " + where + " · map " + (fix.Approximate ? "about " : string.Empty) + WithUnits(value, z.Units);
        return text + (stale ? " (stale)" : fix.Inputs.Any(input => input.Clamped != 0) ? " (off the map)" : string.Empty);
    }
}
