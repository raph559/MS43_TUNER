using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MS43Logger.Core;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// The Live tuning tab: the verified overlay catalogue with its state in the ECU, the selected map's grid
/// to edit, Send, the A/B switches and Revert all. The page edits a document of its own, made like a map window's: the
/// Tune tab's bin with the supported maps as the ECU's flash holds them, so the grid's changed cells are the edits against what
/// the ECU has, and each edit sum is taken against the ECU's bytes. Nothing reaches the ECU without Send, except the
/// session putting back what had been sent after a key cycle or a stall. The session and the page outlive the line: when
/// the app reconnects (a key cycle), the session is moved to the new line, detects again, and puts back what was sent.
/// </summary>
public sealed class LiveTuningViewModel : ViewModelBase
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly MainWindowViewModel _live;
    private readonly TuneViewModel _tune;
    private readonly LiveEditStore _store;
    private readonly TimeProvider _time;
    private readonly Func<IDs2Line, LiveTuningSession> _sessionFactory;
    private IDs2Line? _line;
    private LiveTuningSession? _session;
    private ITimer? _ticker;
    private bool _ticking;
    private CalibrationDocument? _page;
    private CalibrationDocument? _pageBin;
    private IReadOnlyList<byte[]>? _pageFlash;
    private EditHistory? _history;
    private bool _pageAdopted;
    private string? _sessionProblemShown;
    private LiveMapRow? _selected;
    private TableEditorViewModel? _editor;
    private string? _readOnlyReason;
    private string? _binDiffers;
    private string? _keptForOtherFlash;
    private string _linkText = string.Empty;
    private string? _problem;
    private bool _allEdited;
    private int _attachments;
    private string _toolValue = string.Empty;
    private string _mapSearch = string.Empty;
    private bool _loadedOnly;
    private string _memoryText = "Connect to read live RAM usage.";
    private string _allocationText = string.Empty;
    private int _memoryUsed;
    private int _memoryCapacity = OverlayV3.CopyBytes;
    private string _limiterHysteresis = string.Empty;
    private int _limiterMode;
    private bool _limiterEnabled;
    private string _limiterState = string.Empty;
    private bool _limiterAdopted;
    private CalibrationDocument? _scalarPage;
    private int _scalarMapIndex = -1;

    public ObservableCollection<LiveMapRow> Maps { get; }
    private IEnumerable<OverlayMap> MapDefinitions => Maps.Select(row => row.Map);
    public ObservableCollection<LiveMapRow> FilteredMaps { get; } = new();

    /// <summary>
    /// The list as shown, as the Tune rail: without a search, a <see cref="LiveRailCategory"/> per XDF category followed,
    /// when it is open, by its maps; with a search, the matching maps, best first.
    /// </summary>
    public ObservableCollection<object> Rail { get; } = new();

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private object? _describedFor;
    private LiveMapRow? _openedFor;
    public string MapSearch { get => _mapSearch; set { if (SetField(ref _mapSearch, value)) UpdateFilteredMaps(); } }
    public bool LoadedOnly { get => _loadedOnly; set { if (SetField(ref _loadedOnly, value)) UpdateFilteredMaps(); } }
    public bool HasVerifiedCatalogue => _line is not null && _page is not null
        && _session?.Detection is { Problem: null, Header.Version: 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 };
    public bool IsV4 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version is 4 or 5 or 6 or 7 or 8 or 9 or 10;
    public bool IsV5 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version == 5;
    public bool IsV6 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version is 6 or 7 or 8 or 9 or 10;
    public ObservableCollection<LiveScalarRow> ScalarFields { get; } = new();
    public bool HasScalarEditor => HasVerifiedCatalogue && ScalarFields.Count > 0;
    public bool HasTableEditor => HasVerifiedCatalogue && Editor is not null;
    public string? ScalarNote => Selected?.Map.ScalarGroup?.Note;

    public string LimiterScopeText => IsV6
        ? "Launch control, no-lift shift and rolling anti-lag have separate optional settings groups. The hard fuel-cut limit keeps its flashed value."
        : "The separate hard fuel-cut limit and launch / no-lift-shift thresholds keep their flashed values.";
    public string CatalogueText
    {
        get
        {
            if (_line is null)
            {
                if (_live.IsReconnecting) return "Reconnecting to check live-map support…";
                if (_tune.Document is { } bin && OverlayHeader.Parse(bin.Image.Bytes.Slice(OverlayContract.HeaderAddress & 0x7FFFF, 16)) is null)
                    return "No live-tuning overlay in this BIN.";
                return "Connect to check live-map support.";
            }
            if (_tune.Document is null) return "Open a BIN to check live-map support.";
            if (_session?.Detection?.Problem == "not found") return "No live-tuning overlay on this ECU.";
            if (_session?.Detection?.Problem is not null) return "Live-tuning overlay not supported.";
            if (!HasVerifiedCatalogue) return "Checking live-map support…";
            return IsV6 ? $"{Maps.Count(m => m.Map.ScalarGroup is null)} supported maps + {Maps.Count(m => m.Map.ScalarGroup is not null)} settings groups · load while RAM fits"
                : IsV5 ? $"{Maps.Count} supported maps · load while RAM fits"
                : IsV4 ? $"{Maps.Count} supported maps · choose up to 5" : $"{Maps.Count} supported maps";
        }
    }
    public string MemoryText { get => _memoryText; private set => SetField(ref _memoryText, value); }
    public string AllocationText { get => _allocationText; private set => SetField(ref _allocationText, value); }
    public int MemoryUsed { get => _memoryUsed; private set => SetField(ref _memoryUsed, value); }
    public int MemoryCapacity { get => _memoryCapacity; private set => SetField(ref _memoryCapacity, value); }
    public bool CanSendSelected => !IsReadOnly && Selected is { CanLoad: true };
    public ObservableCollection<LimiterRpmRow> LimiterGears { get; }
    public string LimiterHysteresis { get => _limiterHysteresis; set => SetField(ref _limiterHysteresis, value); }
    public int LimiterMode { get => _limiterMode; set => SetField(ref _limiterMode, value); }
    public bool LimiterEnabled { get => _limiterEnabled; set => SetField(ref _limiterEnabled, value); }
    public string LimiterState { get => _limiterState; private set => SetField(ref _limiterState, value); }

    public LiveMapRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                RebuildEditor();
                OnPropertyChanged(nameof(CanSendSelected));
                RebuildRail();
            }
        }
    }

    /// <summary>The selected map's grid: editable once the overlay is found, a read-only view before and while reconnecting.</summary>
    public TableEditorViewModel? Editor { get => _editor; private set => SetField(ref _editor, value); }

    public LiveCursorController LiveCursor { get; }

    public bool IsReadOnly => ReadOnlyReason is not null;

    /// <summary>Why nothing can be sent: not connected, reconnecting, no bin, no overlay, an unknown overlay, a stopped link.</summary>
    public string? ReadOnlyReason
    {
        get => _readOnlyReason;
        private set
        {
            if (SetField(ref _readOnlyReason, value))
            {
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }
    }

    /// <summary>Said when the ECU's flashed maps are not the open bin's: the edits start from the ECU's.</summary>
    public string? BinDiffers { get => _binDiffers; private set => SetField(ref _binDiffers, value); }

    /// <summary>Said when edits are kept for maps whose flashed bytes have changed since (another bin flashed): not applied.</summary>
    public string? KeptForOtherFlash { get => _keptForOtherFlash; private set => SetField(ref _keptForOtherFlash, value); }

    public string LinkText { get => _linkText; private set => SetField(ref _linkText, value); }

    /// <summary>The last refusal, failure or note, in a sentence; null when the last action went through.</summary>
    public string? Problem { get => _problem; private set => SetField(ref _problem, value); }

    /// <summary>The big A/B switch's position: true while any map is live in the ECU.</summary>
    public bool AllEdited { get => _allEdited; private set => SetField(ref _allEdited, value); }

    /// <summary>Per map, in the overlay's order: "flashed", or "v" and the first 8 hex digits of its live copy's SHA-256 while it is on.</summary>
    public IReadOnlyList<string> Tags => Maps.Select(row =>
        row.State == OverlayState.On && _session?.SentCopy(row.Map.Index) is { } copy
            ? "v" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(copy))[..8].ToLowerInvariant()
            : "flashed").ToArray();

    /// <summary>The CSV's <c>live_maps</c> cell: "flashed", or each live map as key=version, separated by ";".</summary>
    public string CsvCell => Maps.Zip(Tags).Where(pair => pair.Second != "flashed").Select(pair => pair.First.Map.Key + "=" + pair.Second)
        .ToArray() is { Length: > 0 } live ? string.Join(";", live) : "flashed";

    /// <summary>The toolbar's value box, read by Set, Add and Scale %.</summary>
    public string ToolValue { get => _toolValue; set => SetField(ref _toolValue, value ?? string.Empty); }

    /// <summary>A toolbar operation on the grid's selection; the page's own history takes it back.</summary>
    public bool ApplyTool(EditKind kind) => Editor is { IsReadOnly: false } editor && editor.ApplyTool(kind, ToolValue);

    public bool Undo() => !IsReadOnly && _history?.Undo() == true;

    public bool Redo() => !IsReadOnly && _history?.Redo() == true;

    /// <summary>The engine's position for the live cursor, fed by the shell on every tab.</summary>
    public void Push(LiveSample sample, AdxDefinition? definition) => LiveCursor.Push(sample, definition);

    public Task SetAllEditedAsync(bool on) => Act(session => session.SetAllAsync(on));

    /// <summary>The per-map switch. The row's switch is told its state again afterwards, so a refusal shows as off.</summary>
    internal async Task SetOnAsync(LiveMapRow row, bool on)
    {
        await Act(session => session.SetOnAsync(row.Map.Index, on));
        row.NotifyIsOn();
    }

    /// <summary>
    /// Writes the chosen maps as they were sent (what the ECU ran) into the Tune tab's open bin, one undoable step per map; a
    /// map never sent is not copied. No checksum is touched. Returns a warning when the open bin is not the overlay bin,
    /// whose flashing would take live tuning off the car; null otherwise.
    /// </summary>
    public Task<string?> CopyToTuneAsync(IReadOnlyList<LiveMapRow> rows)
    {
        if (_tune.Document is not { } bin || _tune.History is not { } history || _session is not { } session)
        {
            return Task.FromResult<string?>(null);
        }

        var source = bin.Image.ToArray();
        foreach (var row in rows)
        {
            if (session.SentCopy(row.Map.Index) is not { } sent) continue;
            if (row.Map.ScalarGroup is { } group)
            {
                if (ScalarsFor(row.Map) is not { } constants)
                    return Task.FromResult<string?>("The settings group needs complete matching definitions in the loaded XDF.");
                var plans = constants.Select((constant, i) => TableEdits.SetConstantRaw(bin, constant, sent[group.Fields[i].Offset])).ToArray();
                foreach (var plan in plans) history.Apply(plan, $"Copy live edits: {row.Title} · {plan.Item.Title}");
            }
            else if (TableFor(row.Map) is { } table)
            {
                sent.CopyTo(source, row.Map.Address & 0x7FFFF);
                history.Apply(TableEdits.CopyFrom(bin, table, BinImage.FromBytes(source)), $"Copy live edits: {row.Title}");
            }
        }

        var overlay = bin.Image.Bytes.Slice(OverlayContract.HeaderAddress & 0x7FFFF, 4).SequenceEqual("LTOV"u8);
        return Task.FromResult(overlay ? null : "This bin is not the overlay bin: flashing it removes live tuning from the car.");
    }

    /// <summary>The page's Copy to Tune: every map with edits that was sent goes to the Tune tab's bin; the others are named.</summary>
    public async Task CopyEditedToTuneAsync()
    {
        if (_page is null || _pageFlash is not { } flash || _session is not { } session)
        {
            return;
        }

        var edited = Maps.Where(row => !PageCopy(row.Map).AsSpan().SequenceEqual(flash[row.Map.Index]) || session.SentCopy(row.Map.Index) is not null).ToList();
        var sent = edited.Where(row => session.SentCopy(row.Map.Index) is not null).ToList();
        var never = edited.Where(row => session.SentCopy(row.Map.Index) is null).ToList();
        if (sent.Count == 0)
        {
            Problem = edited.Count == 0 ? "No map has edits to copy." : "Nothing was sent yet: send a map before copying it.";
            return;
        }

        var warning = await CopyToTuneAsync(sent);
        Problem = $"Copied {sent.Count} {(sent.Any(row => row.Map.ScalarGroup is not null) ? (sent.Count == 1 ? "item" : "items") : (sent.Count == 1 ? "map" : "maps"))} to the Tune tab: save the bin there."
            + (never.Count > 0 ? $" Not copied, never sent: {string.Join(", ", never.Select(row => row.Title))}." : string.Empty)
            + (warning is null ? string.Empty : " " + warning);
    }

    /// <summary>
    /// Why the page's copy of <paramref name="row"/> would be refused before anything is sent (a missing definition, the
    /// overlay's rules, protocol 2's edit limits), or null when it may be sent; the Fun tab checks every group of a page
    /// this way before it sends any.
    /// </summary>
    public string? RefusalFor(LiveMapRow row)
    {
        if (_session is null || _pageFlash is not { } flash || _page is null) return null;
        var index = row.Map.Index;
        if (!HasDefinition(row.Map)) return "This item needs complete matching definitions in the loaded XDF.";
        var copy = PageCopy(row.Map);
        if (IsV6 && OverlayV9.ValidateFor(OverlayVersion ?? 0, index, copy, flash[index], _session.Detection?.Header?.Minor ?? 0) is not null)
            return (OverlayV10.Refusal(index, copy)
                ?? "The settings contain an unsupported activation mode or change a neighbouring calibration byte") + ". Nothing was sent.";
        var fullRange = _session.Detection?.Header?.Version is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10;
        var leaner = fullRange ? Array.Empty<int>() : OverlayContract.LeanerCells(index, copy, flash[index]);
        if (leaner.Count > 0)
            return $"{row.Title}: {leaner.Count} {(leaner.Count == 1 ? "cell is" : "cells are")} leaner than flashed. Nothing was sent: richer only until a wideband sensor is fitted.";
        var outside = fullRange ? Array.Empty<int>() : OverlayContract.EditLimitCells(index, copy, flash[index]);
        if (outside.Count > 0)
            return $"{row.Title}: {outside.Count} {(outside.Count == 1 ? "cell exceeds" : "cells exceed")} the firmware limit relative to flashed values. "
                + (row.Map.RicherOnly ? "AFR cells allow 0 to +8 raw steps." : "Ignition and cam cells allow -8 to +8 raw steps.")
                + " Nothing was sent. These limits do not establish engine safety.";
        return null;
    }

    private async Task Act(Func<LiveTuningSession, Task<OverlayReason?>> action, bool clearProblem = false)
    {
        if (_session is not { } session || IsReadOnly)
        {
            Refresh();
            return;
        }

        try
        {
            var refused = await action(session);
            Problem = refused is { } reason ? $"The ECU refused: {Describe(reason)}." : clearProblem || Problem is null ? null : Problem;
            _sessionProblemShown = session.Problem;
        }
        catch (TimeoutException)
        {
            Problem = "The ECU did not answer. Try again.";
        }
        catch (System.IO.InvalidDataException)
        {
            Problem = "The ECU's reply was not understood. Try again.";
        }
        catch (Exception error)
        {
            Problem = session.Problem ?? error.Message;
        }

        Refresh();
    }

    /// <summary>A map the ECU holds that the page has no edit for shows what the ECU holds (the app was restarted).</summary>
    private void AdoptWhatTheEcuHolds()
    {
        if (_page is null || _pageFlash is not { } flash || _session is null)
        {
            return;
        }

        var adopted = false;
        foreach (var map in MapDefinitions)
        {
            if (_session.SentCopy(map.Index) is { } held && PageCopy(map).AsSpan().SequenceEqual(flash[map.Index]) && !held.AsSpan().SequenceEqual(flash[map.Index]))
            {
                WriteCopy(map, held);
                adopted = true;
            }
        }

        if (adopted)
        {
            _page.RaiseChanged();
        }
    }

    private void Refresh()
    {
        var session = _session;
        ReadOnlyReason = (_line, session) switch
        {
            (null, _) when _live.IsReconnecting => "Reconnecting to the ECU…",
            (null, _) => "Not connected.",
            (_, null) => "Open the car's bin on the Tune tab.",
            (_, { IsStopped: true }) => $"The link to the overlay stopped: {session!.Problem}",
            (_, { Detection.Problem: "not found" }) => "No live-tuning overlay on this ECU.",
            (_, { Detection.Problem: { } problem }) => problem,
            (_, { Detection: null }) => "Looking for the live-tuning overlay…",
            _ when _page is null => "Looking for the live-tuning overlay…",
            _ => null,
        };
        var status = _line is null ? null : session?.Last;
        LinkText = _line is not null && session?.Detection?.Header is { } header
            ? $"Overlay {header.Label} found" + (status is null ? string.Empty
                : $" · engine {(status.Running ? "running" : "stopped")} · {status.FreeBytes} bytes free")
            : string.Empty;
        foreach (var row in Maps)
        {
            var index = row.Map.Index;
            row.Title = row.Map.ScalarGroup?.Title ?? TableFor(row.Map)?.Title ?? row.Map.Key;
            row.State = status?.Tables[index].State;
            row.Intent = session?.Intent(index) ?? MapIntent.None;
            row.HasUnsent = _page is not null && _pageFlash is { } flash && !PageCopy(row.Map).AsSpan()
                .SequenceEqual(session?.SentCopy(index) is { } sent && row.Intent != MapIntent.None ? sent : flash[index]);
            row.CanSwitch = !IsReadOnly && status is { Running: true } && row.State is OverlayState.Ready or OverlayState.On;
            row.NotifyIsOn();
            var fits = status is not null && CopyAreaPlanner.Place(index, status) is not null;
            row.CanLoad = !IsReadOnly && status is { Running: true } && fits && HasDefinition(row.Map);
            var tracking = status?.Version is 5 or 6 or 7 or 8 or 9 or 10 && row.State == OverlayState.Free && fits
                && OverlayV5.Place(index, status)?.DirectorySlot >= OverlayV5.BaseSlots
                    ? OverlayV5.TrackingBytes(status) == 0 ? $" + {8 + OverlayV5.LookupBytes(status.Version)} tracking" : " + 8 tracking" : string.Empty;
            row.CapacityText = $"{row.Map.Bytes} bytes{tracking}" + (!HasDefinition(row.Map) ? " · XDF definition missing"
                : status is null ? string.Empty : row.State != OverlayState.Free ? " · allocated" : fits ? " · fits" : " · no room");
        }
        MemoryCapacity = status?.Version == 2 ? OverlayContract.CopyBytes : status?.Version >= 5 ? OverlayV5.Budget(status.Version) : OverlayV3.CopyBytes;
        MemoryUsed = status is null ? 0 : MemoryCapacity - status.FreeBytes;
        MemoryText = status is null ? "Connect to read live RAM usage." : $"{MemoryUsed} / {MemoryCapacity} bytes used · {status.FreeBytes} free";
        var loadedGroups = status is null ? 0 : Maps.Count(row => row.Map.ScalarGroup is not null && status.Tables[row.Map.Index].State != OverlayState.Free);
        var loadedText = status?.Version is 6 or 7 or 8 or 9 or 10
            ? $"{Maps.Count(row => row.Map.ScalarGroup is null && status.Tables[row.Map.Index].State != OverlayState.Free)} maps + {loadedGroups} settings {(loadedGroups == 1 ? "group" : "groups")}"
            : $"{status?.Tables.Count(e => e.State != OverlayState.Free)} maps";
        AllocationText = status?.Version is 5 or 6 or 7 or 8 or 9 or 10 ? $"{loadedText} loaded · {OverlayV5.TrackingBytes(status)} bytes for extra tracking\nLargest free block: {OverlayV5.LargestBlock(status)} bytes · hardcut: 8 bytes reserved."
            : status?.Version == 4 ? $"{status.Tables.Count(e => e.State != OverlayState.Free)} / 5 map slots · largest free block {OverlayV4.LargestBlock(status)} bytes\nHardcut: 8 bytes reserved separately."
            : status?.Version == 3 ? "Fixed map locations. Freeing a map does not move the others." : string.Empty;
        if (status?.Limiter is { } hard)
        {
            if (!_limiterAdopted) { if (hard.Confirmed) ShowLimiter(hard); _limiterAdopted = true; }
            LimiterState = (hard.Enabled ? "Live settings enabled" : "Flashed settings selected")
                + $" · currently {(hard.AppliedIgnitionCut ? "ignition" : "fuel")} cut"
                + (hard.AppliedIgnitionCut != (hard.Enabled ? hard.IgnitionCut : session!.Detection!.LimiterFlash!.IgnitionCut)
                    ? ". Mode change waits for the limiter to release." : string.Empty);
        }
        UpdateFilteredMaps();
        OnPropertyChanged(nameof(CanSendSelected));
        OnPropertyChanged(nameof(HasVerifiedCatalogue));
        OnPropertyChanged(nameof(IsV4));
        OnPropertyChanged(nameof(IsV5));
        OnPropertyChanged(nameof(IsV6));
        OnPropertyChanged(nameof(LimiterScopeText));
        OnPropertyChanged(nameof(CatalogueText));
        OnPropertyChanged(nameof(HasScalarEditor));
        OnPropertyChanged(nameof(HasTableEditor));

        AllEdited = status?.Tables.Any(entry => entry.State == OverlayState.On) == true || status?.Limiter is { Enabled: true };
        var editable = !IsReadOnly && _page is not null;
        if (Selected?.Map.ScalarGroup is not null)
        {
            if (_scalarMapIndex != Selected.Map.Index || !ReferenceEquals(_scalarPage, _page)) RebuildEditor();
        }
        else if (Editor is null || Editor.IsReadOnly == editable)
        {
            RebuildEditor();
        }
    }

    private void RebuildEditor()
    {
        var old = Editor;
        ClearScalarEditors();
        if (Selected is { Map.ScalarGroup: not null } scalarRow && BuildScalarRows(scalarRow.Map) is { } rows)
        {
            foreach (var field in rows) ScalarFields.Add(field);
            _scalarPage = _page;
            _scalarMapIndex = scalarRow.Map.Index;
        }
        OnPropertyChanged(nameof(HasScalarEditor));
        OnPropertyChanged(nameof(ScalarNote));
        var table = Selected is { } row ? TableFor(row.Map) : null;
        if (table is null)
        {
            Editor = null;
        }
        else if (_page is not null && _history is not null && !IsReadOnly)
        {
            Editor = new TableEditorViewModel(_page, _history, table) { Selection = new CellRange(0, 0, 0, 0), ShowRaw = _tune.Settings.OpenMapsRaw };
        }
        else
        {
            // Read-only: the page's edits when there is a page (while reconnecting or looking again), the open bin's map before.
            var shown = (_page ?? _tune.Document)!;
            var values = shown.Reader.ReadTable(table);
            var marked = new HashSet<(int Row, int Col)>();
            if (_page is not null)
            {
                var flashed = new CalibrationReader(_page.Xdf, _page.Saved).ReadTable(table);
                for (var r = 0; r < values.Rows; r++)
                {
                    for (var c = 0; c < values.Cols; c++)
                    {
                        if (values.Raw[r, c] != flashed.Raw[r, c])
                        {
                            marked.Add((r, c));
                        }
                    }
                }
            }

            Editor = TableEditorViewModel.Snapshot(values, marked);
        }

        old?.Dispose();
        OnPropertyChanged(nameof(HasTableEditor));
        LiveCursor.Bind(_page ?? _tune.Document, Editor);
    }

    private void ClearScalarEditors()
    {
        foreach (var field in ScalarFields) field.Dispose();
        ScalarFields.Clear();
        _scalarPage = null;
        _scalarMapIndex = -1;
    }

    /// <summary>
    /// The rows of a settings group on the live page's copy, whatever is selected (the Fun tab uses them); null without a
    /// page or the group's definitions. The caller disposes them.
    /// </summary>
    public IReadOnlyList<LiveScalarRow>? BuildScalarRows(OverlayMap map) =>
        map.ScalarGroup is not null && ScalarsFor(map) is { } constants && _page is { } page && _history is { } history
            ? LiveScalarRow.Build(page, history, map, constants) : null;

    /// <summary>The detected overlay's protocol version; null before a detection.</summary>
    public int? OverlayVersion => _session?.Detection?.Header?.Version;

    public string? OverlayLabel => _session?.Detection?.Header?.Label;

    /// <summary>The live page's copy of the ECU's maps; null without a verified connection.</summary>
    public CalibrationDocument? Page => _page;

    /// <summary>Raised when the page is built again or dropped: rows built on the old page must be rebuilt.</summary>
    public event EventHandler? PageRebuilt;

    private IReadOnlyList<XdfConstant>? ScalarsFor(OverlayMap map) =>
        _tune.Document is { } bin ? LiveScalarDefinitions.ConstantsFor(map, bin.Xdf) : null;

    private bool HasDefinition(OverlayMap map) => map.ScalarGroup is null ? TableFor(map) is not null : ScalarsFor(map) is not null;

    private XdfTable? TableFor(OverlayMap map) =>
        _tune.Document?.Xdf.Tables.FirstOrDefault(table => table.Z.Data.Address is { } address && BinImage.ToOffset(address) == map.Address
            && table.Z.Data is { Signed: false, IsSupported: true } && table.Z.Data.ElementBits == map.ElementBits
            && (map.ElementBits == 8 || table.Z.Data.LsbFirst) && table.Z.Data.Count * (map.ElementBits / 8) == map.Bytes);

    private byte[] PageCopy(OverlayMap map) => _page!.Image.Bytes.Slice(map.Address & 0x7FFFF, map.Bytes).ToArray();

    private void WriteCopy(OverlayMap map, byte[] copy)
    {
        for (var i = 0; i < copy.Length; i++)
        {
            _page!.Image.WriteElement(new XdfData(map.Address + i, 8, false, true, 1, 1, null), 0, copy[i]);
        }
    }

    private static string MapFlashKey(OverlayMap map, byte[] flash) => map.Key + ":" + LiveEditStore.FlashKey(new[] { flash });

    private static bool SameFlash(IReadOnlyList<byte[]>? a, IReadOnlyList<byte[]> b) =>
        a is not null && a.Count == b.Count && a.Zip(b).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second));

    /// <summary>Gives each row its XDF categories, description and search text, once per bin and catalogue.</summary>
    private void DescribeMaps()
    {
        var key = (object?)_tune.Document ?? this;
        if (ReferenceEquals(key, _describedFor) && Maps.All(row => row.Haystack.Length > 0)) return;
        _describedFor = key;
        var catalog = _tune.Document is { } bin ? new XdfCatalog(bin.Xdf) : null;
        foreach (var row in Maps)
        {
            var title = row.Map.ScalarGroup?.Title ?? TableFor(row.Map)?.Title ?? row.Map.Key;
            var categories = new List<string>();
            var units = new List<string>();
            string description;
            var shape = $"{row.Map.Bytes} bytes";
            if (row.Map.ScalarGroup is { } group)
            {
                var constants = ScalarsFor(row.Map) ?? (IReadOnlyList<XdfConstant>)Array.Empty<XdfConstant>();
                categories.AddRange(group.Fields.Select(field => field.Definition?.Category).OfType<string>());
                if (catalog is not null) categories.AddRange(constants.Where(constant => constant.UniqueId >= 0).SelectMany(catalog.CategoriesOf));
                units.AddRange(constants.Select(constant => constant.Units));
                description = string.Join("\n", new[] { group.Note }.Concat(constants.Select(constant =>
                    string.IsNullOrWhiteSpace(constant.Description) ? constant.Title : $"{constant.Title}: {constant.Description}"))
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
                shape = $"{group.Fields.Count} settings · {shape}";
            }
            else if (TableFor(row.Map) is { } table)
            {
                if (catalog is not null) categories.AddRange(catalog.CategoriesOf(table));
                units.AddRange(new[] { table.Z.Units, table.X.Units, table.Y.Units });
                description = table.Description;
                shape = $"{table.Z.Data.Rows} × {table.Z.Data.Cols} · {shape}";
            }
            else
            {
                description = string.Empty;
            }

            if (categories.Count == 0) categories.Add(XdfCatalog.OtherCategory);
            row.Categories = categories.Distinct().ToList();
            row.Description = description ?? string.Empty;
            row.Detail = $"{shape} · 0x{row.Map.Address:X}";
            row.Haystack = string.Join(" ", new[] { title, row.Map.Key, row.Map.Group, row.Description, $"0x{row.Map.Address:X} {row.Map.Address:X}" }
                .Concat(row.Categories).Concat(units.Where(unit => !string.IsNullOrWhiteSpace(unit)))
                .Concat(row.Map.ScalarGroup?.Fields.Select(field => field.Key) ?? Enumerable.Empty<string>()));
        }
    }

    /// <summary>How well a row matches the search, as the Tune rail ranks it: 0 best, 4 no match.</summary>
    private static int Rank(LiveMapRow row, string query, string[] words)
    {
        bool HasAll(string where) => words.All(word => where.Contains(word, StringComparison.OrdinalIgnoreCase));
        return row.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0
            : row.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1
            : HasAll(row.Title) || HasAll(row.Map.Key) ? 2
            : HasAll(row.Haystack) ? 3 : 4;
    }

    /// <summary>Builds the shown list again; it is left alone when nothing in it changed, as on most status polls.</summary>
    private void RebuildRail()
    {
        var rail = new List<object>();
        if (!string.IsNullOrWhiteSpace(MapSearch))
        {
            rail.AddRange(FilteredMaps);
        }
        else
        {
            if (Selected is { } current && !ReferenceEquals(current, _openedFor))
            {
                _expanded.UnionWith(current.Categories.Take(1));      // the selected map is always shown once
                _openedFor = current;
            }

            foreach (var category in FilteredMaps.SelectMany(row => row.Categories).Distinct().Order(StringComparer.OrdinalIgnoreCase))
            {
                var rows = FilteredMaps.Where(row => row.Categories.Contains(category)).ToList();
                var open = _expanded.Contains(category);
                rail.Add(new LiveRailCategory(category, rows.Count, open));
                if (open) rail.AddRange(rows);
            }
        }

        if (rail.SequenceEqual(Rail)) return;
        Rail.Clear();
        foreach (var entry in rail) Rail.Add(entry);
    }

    private void UpdateFilteredMaps()
    {
        DescribeMaps();
        var selected = Selected;
        var query = MapSearch.Trim();
        var words = XdfCatalog.Words(query);
        var wanted = Maps.Where(row => !_loadedOnly || row.State is OverlayState.Loaded or OverlayState.Ready or OverlayState.On)
            .Select(row => (Row: row, Rank: query.Length == 0 ? 0 : Rank(row, query, words)))
            .Where(pair => pair.Rank < 4)
            .OrderBy(pair => pair.Rank)
            .Select(pair => pair.Row).ToArray();
        for (var i = 0; i < wanted.Length; i++)
        {
            if (i < FilteredMaps.Count && ReferenceEquals(FilteredMaps[i], wanted[i])) continue;
            var found = FilteredMaps.IndexOf(wanted[i]);
            if (found >= 0) FilteredMaps.Move(found, i); else FilteredMaps.Insert(i, wanted[i]);
        }
        while (FilteredMaps.Count > wanted.Length) FilteredMaps.RemoveAt(FilteredMaps.Count - 1);
        if (selected is not null && FilteredMaps.Contains(selected)) Selected = selected;
        else if (Selected is null || !FilteredMaps.Contains(Selected)) Selected = FilteredMaps.FirstOrDefault();
        RebuildRail();
    }

    private void ShowLimiter(LimiterSettings settings)
    {
        for (var i = 0; i < 6; i++) LimiterGears[i].Rpm = (settings.GearLimits[i] * 32).ToString();
        LimiterHysteresis = (settings.Hysteresis * 32).ToString();
        LimiterMode = settings.IgnitionCut ? 1 : 0;
        LimiterEnabled = settings.Enabled;
    }

    public void CopyLimiterToTune()
    {
        if (_tune.Document is not { } bin || _tune.History is not { } history
            || _session?.Last?.Limiter is not { Confirmed: true } sent)
        {
            Problem = "Send the limiter settings before copying them to Tune.";
            return;
        }
        if (LimiterInBin.Find(bin.Xdf) is not { } items)
        {
            Problem = "The loaded XDF needs the manual gear limits, limiter hysteresis and cut-mode definitions. Nothing was copied.";
            return;
        }
        try
        {
            LimiterInBin.Write(bin, history, items, sent with { Enabled = false, Confirmed = false, AppliedIgnitionCut = false }, "Copy live limiter");
            Problem = "Copied the confirmed limiter settings to Tune: save the bin there.";
            if (!bin.Image.Bytes.Slice(OverlayContract.HeaderAddress & 0x7FFFF, 4).SequenceEqual("LTOV"u8))
                Problem += " This bin has no live overlay; flashing it removes live tuning.";
        }
        catch (Exception error) when (error is ArgumentException or EditRefusedException) { Problem = error.Message; }
    }

    private static string Describe(OverlayReason reason) => reason switch
    {
        OverlayReason.NotRunning => "the engine is not running",
        OverlayReason.TableOn => "the map is on",
        OverlayReason.NotLoaded => "the map is not in the ECU",
        OverlayReason.Integrity => "the complete table did not match its expected content (readback or CRC-32 failed)",
        OverlayReason.Leaner => "a λ cell is leaner than flashed",
        OverlayReason.EditLimit => "a cell exceeds the firmware's limit relative to flashed values (±8 raw for ignition/cams; 0 to +8 for AFR)",
        OverlayReason.Session => "the ECU's editing session changed; reconnect before sending another edit",
        OverlayReason.Revision => "the ECU has a different map revision; refresh before sending another edit",
        OverlayReason.Overlap or OverlayReason.Range => "it does not fit in the ECU's copy area",
        _ => reason.ToString(),
    };
}
