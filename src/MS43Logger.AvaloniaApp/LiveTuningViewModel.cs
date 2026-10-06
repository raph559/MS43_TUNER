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
    public bool HasVerifiedCatalogue => _line is not null && _page is not null
        && _session?.Detection is { Problem: null, Header.Version: 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 };
    public bool IsV4 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version is 4 or 5 or 6 or 7 or 8 or 9 or 10;
    public bool IsV5 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version == 5;
    public bool IsV6 => HasVerifiedCatalogue && _session!.Detection!.Header!.Version is 6 or 7 or 8 or 9 or 10;
    public ObservableCollection<LiveScalarRow> ScalarFields { get; } = new();
    public bool HasScalarEditor => HasVerifiedCatalogue && ScalarFields.Count > 0;
    public bool HasTableEditor => HasVerifiedCatalogue && Editor is not null;

    public string LimiterScopeText => IsV6
        ? "Launch control, no-lift shift and rolling anti-lag have separate optional settings groups. The hard fuel-cut limit keeps its flashed value."
        : "The separate hard fuel-cut limit and launch / no-lift-shift thresholds keep their flashed values.";
    public string MemoryText { get => _memoryText; private set => SetField(ref _memoryText, value); }
    public string AllocationText { get => _allocationText; private set => SetField(ref _allocationText, value); }
    public int MemoryUsed { get => _memoryUsed; private set => SetField(ref _memoryUsed, value); }
    public int MemoryCapacity { get => _memoryCapacity; private set => SetField(ref _memoryCapacity, value); }
    public ObservableCollection<LimiterRpmRow> LimiterGears { get; }
    public string LimiterHysteresis { get => _limiterHysteresis; set => SetField(ref _limiterHysteresis, value); }
    public int LimiterMode { get => _limiterMode; set => SetField(ref _limiterMode, value); }
    public bool LimiterEnabled { get => _limiterEnabled; set => SetField(ref _limiterEnabled, value); }
    public string LimiterState { get => _limiterState; private set => SetField(ref _limiterState, value); }

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

    /// <summary>The toolbar's value box, read by Set, Add and Scale %.</summary>
    public string ToolValue { get => _toolValue; set => SetField(ref _toolValue, value ?? string.Empty); }

    /// <summary>A toolbar operation on the grid's selection; the page's own history takes it back.</summary>
    public bool ApplyTool(EditKind kind) => Editor is { IsReadOnly: false } editor && editor.ApplyTool(kind, ToolValue);

    public bool Undo() => !IsReadOnly && _history?.Undo() == true;

    public bool Redo() => !IsReadOnly && _history?.Redo() == true;

    /// <summary>The engine's position for the live cursor, fed by the shell on every tab.</summary>
    public void Push(LiveSample sample, AdxDefinition? definition) => LiveCursor.Push(sample, definition);

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
}
