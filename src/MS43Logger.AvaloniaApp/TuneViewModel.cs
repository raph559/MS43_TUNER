using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public abstract record RailEntry;

public sealed record RailGroup(string Title) : RailEntry;

/// <param name="Summary">What the category holds, for its tooltip (XDF categories carry no description).</param>
public sealed record RailCategory(string Name, int Count, bool IsExpanded, string Summary = "") : RailEntry;

/// <summary>One inspector row: a label and a value, with an optional second line; <see cref="TextIsId"/> sets the value as an id.</summary>
public sealed record Fact(string Label, string Text, string? Detail = null, bool TextIsId = false);

public enum TuneAction
{
    None,
    OpenBin,
    OpenXdf,
    SaveAs,
    UseAnotherXdf,
}

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <param name="Detail">The tooltip's facts line: kind, size, units and address ("Map, 20 × 16, °CRK, at 0x77315").</param>
public sealed record RailItem(XdfItem Item, ItemKind Kind, bool IsPinned, bool IsModified, bool IsCurrent, bool IsIndented, bool IsDifferent = false, string Detail = "") : RailEntry
{
    public string Title => Item.Title;

    public string Description => Item.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);

    /// <summary>Maps, curves and axes open in a window of their own; values, switches and flags stay in the pane.</summary>
    public bool OpensInWindow => Item is XdfTable;
}

/// <summary>The Tune screen: the open bin and XDF, the map rail, and the selected item.</summary>
public sealed class TuneViewModel : ViewModelBase, ISidePanel
{
    private readonly AppSettings _settings;
    private readonly TimeProvider? _time;
    private int _mapHolds;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private CalibrationDocument? _document;
    private EditHistory? _history;
    private int _historyDepth;
    private XdfCatalog? _catalog;
    private XdfItem? _selectedItem;
    private TableEditorViewModel? _editor;
    private ValueEditorViewModel? _value;
    private IReadOnlyList<Fact> _limitFacts = Array.Empty<Fact>();
    private IReadOnlyList<Fact> _detailFacts = Array.Empty<Fact>();
    private string? _scaleCaption;
    private (string? Min, string? Mid, string? Max) _scaleLabels;
    private string? _surfaceNote;
    private string _toolValue = string.Empty;
    private string _searchText = string.Empty;
    private bool _modifiedOnly;
    private bool _differsOnly;
    private IReadOnlySet<XdfItem>? _compareItems;
    private string? _compareFileB;
    private string _plateFile = "No bin open";
    private string? _plateFirmware;
    private string? _plateXdf;
    private string? _firmwareWarning;
    private string? _errorMessage;
    private TuneAction _errorAction;
    private bool _needsXdf;
    private bool _isBusy;
    private string? _busyText;
    private bool _inspectorOpen;

    public TuneViewModel(AppSettings settings, TimeProvider? time = null)
    {
        Undos.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(UndoTip));
            OnPropertyChanged(nameof(RedoTip));
        };

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _time = time;
        LiveCursor = new LiveCursorController(time);
    }

    /// <summary>
    /// True from an open's save prompt until the new bin is read: the map windows edit the old bin, which is about to go,
    /// and the file picker blocks only the main window, so they take no edits meanwhile.
    /// </summary>
    public bool MapWindowsHeld => _mapHolds > 0;

    /// <summary>Holds the map windows until the returned handle is disposed; holds nest.</summary>
    public IDisposable HoldMapWindows()
    {
        if (_mapHolds++ == 0)
        {
            OnPropertyChanged(nameof(MapWindowsHeld));
        }

        return new MapWindowHold(this);
    }

    private void ReleaseMapWindows()
    {
        if (--_mapHolds == 0)
        {
            OnPropertyChanged(nameof(MapWindowsHeld));
        }
    }

    private sealed class MapWindowHold(TuneViewModel tune) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                tune.ReleaseMapWindows();
            }
        }
    }

    /// <summary>A table was opened in a window of its own: the main window shows one for it.</summary>
    public event EventHandler<MapWindowViewModel>? MapWindowOpened;

    /// <summary>The tables open in windows of their own, in the order they were opened.</summary>
    public ObservableCollection<MapWindowViewModel> MapWindows { get; } = new();

    /// <summary>
    /// Opens a map, curve or axis in a window of its own, as TunerPro does, or brings its open window to the front. Any
    /// number can be open; each edits the bin through the one undo history.
    /// </summary>
    public MapWindowViewModel OpenMapWindow(XdfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (MapWindows.FirstOrDefault(open => ReferenceEquals(open.Table, table)) is { } open)
        {
            open.Activate();
            return open;
        }

        if (Document is null || History is null || Catalog is null)
        {
            throw new InvalidOperationException("Open a bin before opening a map in a window.");
        }

        var map = new MapWindowViewModel(this, table, _time);
        map.Closed += (_, _) => MapWindows.Remove(map);
        MapWindows.Add(map);
        MapWindowOpened?.Invoke(this, map);
        return map;
    }

    /// <summary>Closes every map window: the bin they edit is going away.</summary>
    public void CloseMapWindows()
    {
        foreach (var map in MapWindows.ToList())
        {
            map.Close();
        }
    }

    /// <summary>A logged sample for the cursor on the Tune tab and in every map window.</summary>
    public void PushSample(LiveSample sample, AdxDefinition? definition)
    {
        LiveCursor.Push(sample, definition);
        foreach (var map in MapWindows)
        {
            map.LiveCursor.Push(sample, definition);
        }
    }

    /// <summary>The logger stopped: every cursor goes.</summary>
    public void ClearLive()
    {
        LiveCursor.Clear();
        foreach (var map in MapWindows)
        {
            map.LiveCursor.Clear();
        }
    }

    /// <summary>The Settings trail, for the cursor on the Tune tab and in every map window.</summary>
    public void SetCursorTrail(TrailMode trail)
    {
        LiveCursor.Trail = trail;
        foreach (var map in MapWindows)
        {
            map.LiveCursor.Trail = trail;
        }
    }

    public event EventHandler? DocumentOpened;

    public AppSettings Settings => _settings;

    /// <summary>Places the logger's latest sample on the table shown.</summary>
    public LiveCursorController LiveCursor { get; }

    public ObservableCollection<RailEntry> Rail { get; } = new();

    public CalibrationDocument? Document { get => _document; private set => SetField(ref _document, value); }

    public EditHistory? History { get => _history; private set => SetField(ref _history, value); }

    public XdfCatalog? Catalog { get => _catalog; private set => SetField(ref _catalog, value); }

    public bool HasDocument => Document is not null;

    public string PlateFile { get => _plateFile; private set => SetPlate(ref _plateFile, value); }

    public string? PlateFirmware { get => _plateFirmware; private set => SetPlate(ref _plateFirmware, value); }

    public string? PlateXdf { get => _plateXdf; private set => SetPlate(ref _plateXdf, value); }

    /// <summary>The file block's tooltip: the bin name, then the firmware and XDF line, which the header hides when it is short.</summary>
    public string? PlateTip => HasDocument ? $"{PlateFile}\nFirmware {PlateFirmware} · XDF {PlateXdf}" : null;

    /// <summary>Set when the XDF was written for other firmware than the bin's.</summary>
    public string? FirmwareWarning { get => _firmwareWarning; private set => SetField(ref _firmwareWarning, value); }

    /// <summary>What went wrong last (system 7.1); <see cref="ErrorAction"/> is the banner's way out.</summary>
    public string? ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }

    public TuneAction ErrorAction { get => _errorAction; private set => SetField(ref _errorAction, value); }

    /// <summary>The banner's action button: "Open bin…", "Open XDF…", "Save as…" or "Use another XDF…".</summary>
    public string? ErrorActionText => ErrorAction switch
    {
        TuneAction.OpenBin => "Open bin…",
        TuneAction.OpenXdf => "Open XDF…",
        TuneAction.SaveAs => "Save as…",
        TuneAction.UseAnotherXdf => "Use another XDF…",
        _ => null,
    };

    /// <summary>The last open stopped for want of an XDF: on first run the empty block says so instead of a banner.</summary>
    public bool NeedsXdf { get => _needsXdf; private set => SetField(ref _needsXdf, value); }

    public bool HasErrorBanner => ErrorMessage is not null && !(NeedsXdf && Document is null);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowsBusyBar));
            }
        }
    }

    /// <summary>"Reading CAR_current_43X001_SP95_STAGE1.bin…", then "Reading Siemens_MS43_MS43X001_512K.xdf…"; null when idle.</summary>
    public string? BusyText { get => _busyText; private set => SetField(ref _busyText, value); }

    /// <summary>With a bin open the reading shows as the 2 px bar under the header; on first run the empty block shows it.</summary>
    public bool ShowsBusyBar => IsBusy && HasDocument;

    /// <summary>Shows a problem with its way out, or clears it with null.</summary>
    public void ShowError(string? message, TuneAction action = TuneAction.None)
    {
        NeedsXdf = false;
        ErrorMessage = message;
        ErrorAction = message is null ? TuneAction.None : action;
        OnPropertyChanged(nameof(ErrorActionText));
        OnPropertyChanged(nameof(HasErrorBanner));
    }

    /// <summary>The header chip: "Saved", "1 item not saved" or "3 items not saved"; empty with no bin open.</summary>
    public string SaveStateText
    {
        get
        {
            var count = Document?.ModifiedItems().Count ?? 0;
            return Document is null ? string.Empty : count == 0 ? "Saved" : $"{count} {(count == 1 ? "item" : "items")} not saved";
        }
    }

    public bool HasUnsavedChanges => Document?.IsModified == true;

    /// <summary>The XDF remembered for the last opened bin's firmware when that file is no longer on disk.</summary>
    public string? MissingXdfPath { get; private set; }

    /// <summary>The Settings dialog changed a choice that this screen shows.</summary>
    public void SettingsChanged() => OnPropertyChanged(nameof(SaveTip));

    /// <summary>Save's tooltip; it only promises a .bak while the Settings switch keeps one.</summary>
    public string SaveTip => !HasUnsavedChanges ? "Nothing to save" : Settings.KeepBackup ? "Save (Ctrl S). A .bak keeps the file as it was." : "Save (Ctrl S)";

    /// <summary>The editor for the selected table, map, curve or axis; null for values, switches and flags.</summary>
    public TableEditorViewModel? Editor { get => _editor; private set => SetField(ref _editor, value); }

    /// <summary>The editor for the selected value, switch or flag; null for tables.</summary>
    public ValueEditorViewModel? Value { get => _value; private set => SetField(ref _value, value); }

    /// <summary>The inspector's "Edit limits": Storable, Step, the XDF range when it narrows, and the active cell or value.</summary>
    public IReadOnlyList<Fact> LimitFacts
    {
        get => _limitFacts;
        private set
        {
            if (SetField(ref _limitFacts, value))
            {
                OnPropertyChanged(nameof(HasLimits));
            }
        }
    }

    public bool HasLimits => LimitFacts.Count > 0;

    /// <summary>The inspector's "Details": address, size, units, scaling and the axes.</summary>
    public IReadOnlyList<Fact> DetailFacts { get => _detailFacts; private set => SetField(ref _detailFacts, value); }

    /// <summary>Below 1280 px the inspector is an overlay that Details opens.</summary>
    public bool InspectorOpen { get => _inspectorOpen; set => SetField(ref _inspectorOpen, value); }

    public string PinText => SelectedItem is { } item && _settings.IsPinned(item.Title) ? "Unpin" : "Pin";

    /// <summary>"'colmajor' is read-only: …" for storage the app cannot write; null for an editable item.</summary>
    public string? ReadOnlyReason => SelectedItem switch
    {
        XdfTable { Z.Data.UnsupportedReason: { } reason } table => $"'{table.Title}' is read-only: {reason}.",
        XdfConstant { Data.UnsupportedReason: { } reason } constant => $"'{constant.Title}' is read-only: {reason}.",
        XdfFlag { Data.UnsupportedReason: { } reason } flag => $"'{flag.Title}' is read-only: {reason}.",
        _ => null,
    };

    public bool IsReadOnlyItem => ReadOnlyReason is not null;

    /// <summary>The rail's one line when a search finds nothing (system 7.1).</summary>
    public string? NoMatchText => HasDocument && SearchText.Trim().Length > 0 && Rail.Count == 0
        ? $"No map or value matches \"{SearchText.Trim()}\"."
        : null;

    public bool ShowsSurface => Editor is { Values: { Rows: > 1, Cols: > 1 } };

    /// <summary>A one-column table (a curve or an axis) sits centred in the pane with its key line (system 8, owner).</summary>
    public bool CentreGrid => Editor is { Values.Cols: 1 };



    public bool ShowsCurve => Editor is not null && !ShowsSurface;

    /// <summary>The inspector's chart title: "Surface" for a map, "Breakpoints" for an axis, "Curve" for a curve.</summary>
    public string ShapeTitle => ShowsSurface ? "Surface" : IsAxis ? "Breakpoints" : "Curve";

    /// <summary>An axis: its pane carries the breakpoint rule and its inspector the tables that use it (system 5.13).</summary>
    public bool IsAxis => SelectedItem is not null && Catalog is not null && Catalog.Kind(SelectedItem) == ItemKind.Axis;

    /// <summary>The tables that use the selected axis, from the XDF's axis links; empty for anything else.</summary>
    public IReadOnlyList<XdfItem> UsedBy => SelectedItem is XdfTable table && IsAxis ? Catalog!.AxisUsers(table) : [];

    public bool HasNoUsers => IsAxis && UsedBy.Count == 0;

    /// <summary>The colour scale's caption (system 2.2): "Colour: 3.0 to 39.0 °CRK. Hatched cells are at the storage limit (-23.6)."</summary>
    public string? ScaleCaption { get => _scaleCaption; private set => SetField(ref _scaleCaption, value); }

    public string? ScaleMinLabel => _scaleLabels.Min;

    public string? ScaleMidLabel => _scaleLabels.Mid;

    public string? ScaleMaxLabel => _scaleLabels.Max;

    /// <summary>Under the surface: the cells it leaves out, "The 6990 rpm row (storage limit) is not drawn."</summary>
    public string? SurfaceNote
    {
        get => _surfaceNote;
        private set
        {
            if (SetField(ref _surfaceNote, value))
            {
                OnPropertyChanged(nameof(SurfaceHint));
            }
        }
    }

    /// <summary>The surface's line: how to turn it, then what it leaves out.</summary>
    public string SurfaceHint => SurfaceNote is { } note ? SurfaceHelp + " " + note : SurfaceHelp;

    private const string SurfaceHelp = "Drag to turn, wheel to zoom. Click points to select cells, + or - to step them. Home resets.";

    /// <summary>Asks whether to save unsaved edits; set by the window. Without it, unsaved edits are never dropped.</summary>
    public Func<string, Task<SaveChoice>>? AskToSave { get; set; }

    /// <summary>The number typed in the toolbar's value box.</summary>
    public string ToolValue { get => _toolValue; set => SetField(ref _toolValue, value ?? string.Empty); }

    /// <summary>Tune's and Compare's undo history: calibration edits and item pins, in the order they were made.</summary>
    public UndoStack Undos { get; } = new();

    public bool CanUndo => Undos.CanUndo;

    public bool CanRedo => Undos.CanRedo;

    public string UndoTip => Undos.NextUndoLabel is { } label ? "Undo: " + label : "Nothing to undo";

    public string RedoTip => Undos.NextRedoLabel is { } label ? "Redo: " + label : "Nothing to redo";

    /// <summary>Runs a toolbar operation on the selected cells; Set, Add and Scale % use <see cref="ToolValue"/>.</summary>
    public bool RunTool(EditKind kind) => Editor is { } editor && editor.ApplyTool(kind, ToolValue);

    public void Undo() => Undos.Undo();

    public void Redo() => Undos.Redo();

    /// <summary>Saves to the open file, or to <paramref name="path"/> (Save As). Problems are shown in <see cref="ErrorMessage"/>.</summary>
    public bool Save(string? path = null)
    {
        if (Document is not { } document)
        {
            return false;
        }

        try
        {
            try
            {
                document.Save(path, options: new SaveOptions(_settings.KeepBackup, _settings.AppendEditLog));
                ShowError(null);
            }
            catch (EditLogException error)
            {
                ShowError($"Saved. The edit log {Path.GetFileName(error.LogPath)} was not written: {Reason(error.InnerException ?? error)}.");
            }

            PlateFile = Path.GetFileName(document.Path ?? PlateFile);
            _settings.LastBin = document.Path;
            _settings.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError($"Not saved: {Reason(error)}. The file on disk is unchanged.", TuneAction.SaveAs);
            return false;
        }
    }

    /// <summary>True when it is fine to close or replace the open bin: nothing unsaved, saved now, or the user chose not to save.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Document is not { IsModified: true } document)
        {
            return true;
        }

        var choice = AskToSave is null ? SaveChoice.Cancel : await AskToSave($"Save the changes to {Path.GetFileName(document.Path ?? "this bin")}?");
        return choice switch
        {
            SaveChoice.Save => Save(),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }

    public string SelectedKindText => SelectedItem is null || Catalog is null ? string.Empty : Catalog.Kind(SelectedItem).ToString();

    /// <summary>The selected item's kind, for its glyph in the title row.</summary>
    public ItemKind SelectedKind => SelectedItem is null || Catalog is null ? ItemKind.Value : Catalog.Kind(SelectedItem);

    /// <summary>A bin is open and nothing is picked yet (system 7.1).</summary>
    public bool NothingSelected => HasDocument && SelectedItem is null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                RebuildRail();
            }
        }
    }

    public bool ModifiedOnly
    {
        get => _modifiedOnly;
        set
        {
            if (SetField(ref _modifiedOnly, value))
            {
                RebuildRail();
            }
        }
    }

    /// <summary>Items that differ from the Compare tab's B; null while no B is open.</summary>
    public IReadOnlySet<XdfItem>? CompareItems { get => _compareItems; private set => SetField(ref _compareItems, value); }

    public string? CompareFileB { get => _compareFileB; private set => SetField(ref _compareFileB, value); }

    public bool HasCompare => CompareItems is not null;

    /// <summary>Lists only the items that differ from B.</summary>
    public bool DiffersOnly
    {
        get => _differsOnly;
        set
        {
            if (SetField(ref _differsOnly, value && CompareItems is not null))
            {
                RebuildRail();
            }
        }
    }

    /// <summary>Set by the Compare screen after each comparison; null items when there is no B.</summary>
    public void CompareWith(string? fileB, IReadOnlySet<XdfItem>? items, BinImage? image = null)
    {
        CompareFileB = fileB;
        CompareItems = items;
        _compareImage = items is null ? null : image;
        OnPropertyChanged(nameof(HasCompare));
        if (items is null)
        {
            DiffersOnly = false;
        }

        RebuildRail();
        RefreshCompare();
    }

    private BinImage? _compareImage;
    private string _gridView = "a";
    private TableComparison? _comparison;
    private TableEditorViewModel? _compareGrid;

    /// <summary>True while a B is open on Compare and an item is shown: Tune offers A, B and A − B and Copy from B.</summary>
    public bool ShowsCompare => _compareImage is not null && (Editor is not null || Value is not null);

    public bool ShowsCompareGrid => _compareImage is not null && Editor is not null;

    public bool ShowsUseB => _compareImage is not null && Value is not null;

    /// <summary>The grid shown on Tune: "a" (the bin edited), "b" (the compare bin) or "delta" (A − B); B and A − B are read-only.</summary>
    public string GridView
    {
        get => _gridView;
        set
        {
            if (value is "a" or "b" or "delta" && SetField(ref _gridView, value))
            {
                OnPropertyChanged(nameof(IsGridA));
                OnPropertyChanged(nameof(IsGridB));
                OnPropertyChanged(nameof(IsGridDelta));
                OnPropertyChanged(nameof(ShowsDeltaMode));
                RefreshCompare();
            }
        }
    }

    public bool IsGridA { get => GridView == "a"; set { if (value) { GridView = "a"; } else { OnPropertyChanged(); } } }

    public bool IsGridB { get => GridView == "b"; set { if (value) { GridView = "b"; } else { OnPropertyChanged(); } } }

    public bool IsGridDelta { get => GridView == "delta"; set { if (value) { GridView = "delta"; } else { OnPropertyChanged(); } } }

    /// <summary>
    /// How A − B reads, in Tune and on Compare alike (one setting): in the map's units, in stored counts, or as a percentage
    /// of A. Remembered.
    /// </summary>
    public DeltaMode DeltaMode
    {
        get => _settings.CompareDeltaMode;
        set
        {
            if (_settings.CompareDeltaMode == value)
            {
                return;
            }

            _settings.CompareDeltaMode = value;
            _settings.Save();
            foreach (var name in new[] { nameof(DeltaMode), nameof(IsDeltaUnits), nameof(IsDeltaRaw), nameof(IsDeltaPercent) })
            {
                OnPropertyChanged(name);
            }

            RefreshCompare();
        }
    }

    public bool IsDeltaUnits { get => DeltaMode == DeltaMode.Units; set { if (value) { DeltaMode = DeltaMode.Units; } else { OnPropertyChanged(); } } }

    public bool IsDeltaRaw { get => DeltaMode == DeltaMode.Raw; set { if (value) { DeltaMode = DeltaMode.Raw; } else { OnPropertyChanged(); } } }

    public bool IsDeltaPercent { get => DeltaMode == DeltaMode.Percent; set { if (value) { DeltaMode = DeltaMode.Percent; } else { OnPropertyChanged(); } } }

    /// <summary>The Units, Raw and % choice shows with the A − B grid it changes.</summary>
    public bool ShowsDeltaMode => ShowsCompareGrid && IsGridDelta;

    /// <summary>"Compare with MS43X001.bin": the strip that holds A, B, A − B and Copy from B.</summary>
    public string? CompareStripText => CompareFileB is { } b ? "Compare with " + b : null;

    /// <summary>Which way Interpolate and Smooth work on a map: X along each row, Y along each column, XY both. Remembered.</summary>
    public EditAxis EditAxis
    {
        get => _settings.EditAxis;
        set
        {
            if (_settings.EditAxis == value)
            {
                return;
            }

            _settings.EditAxis = value;
            _settings.Save();
            if (Editor is { } editor)
            {
                editor.Axis = value;
            }

            foreach (var name in new[] { nameof(EditAxis), nameof(IsEditAxisX), nameof(IsEditAxisY), nameof(IsEditAxisXY) })
            {
                OnPropertyChanged(name);
            }
        }
    }

    public bool IsEditAxisX { get => EditAxis == EditAxis.X; set { if (value) { EditAxis = EditAxis.X; } else { OnPropertyChanged(); } } }

    public bool IsEditAxisY { get => EditAxis == EditAxis.Y; set { if (value) { EditAxis = EditAxis.Y; } else { OnPropertyChanged(); } } }

    public bool IsEditAxisXY { get => EditAxis == EditAxis.XY; set { if (value) { EditAxis = EditAxis.XY; } else { OnPropertyChanged(); } } }

    /// <summary>X, Y and XY show for a map; on a curve the tools work along it.</summary>
    public bool ShowsEditAxis => Editor is { IsMap: true };

    /// <summary>The grid's editor: A's, or a read-only B or A − B over the same cells and selection.</summary>
    public TableEditorViewModel? ShownEditor => GridView != "a" && _compareGrid is not null ? _compareGrid : Editor;

    /// <summary>Where B's breakpoints differ from A's (cells are compared position by position), as the Compare tab says it.</summary>
    public string? CompareAxisNote => _comparison is { Axes.Count: > 0 } compared ? string.Join(" ", compared.Axes.Select(axis => axis.Describe())) : null;

    /// <summary>B at the active cell (or B's value): "B 25.5 °CRK, A − B -1.5", for the key line.</summary>
    public string? CompareText
    {
        get
        {
            if (Document is not { } document || _compareImage is not { } image)
            {
                return null;
            }

            if (Editor is { } editor && _comparison is { } compared)
            {
                var (row, col) = editor.ActiveCell;
                if (row >= compared.B.Rows || col >= compared.B.Cols)
                {
                    return null;
                }

                var z = compared.A.Table.Z;
                var units = editor.Units.Length > 0 ? " " + editor.Units : string.Empty;
                var b = ValueCodec.Format(compared.B.Physical[row, col], compared.B.Raw[row, col], z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
                var delta = BinComparer.Difference(compared, DeltaMode);
                return compared.Delta.Raw[row, col] == 0
                    ? $"B {b}{units}, same as A"
                    : $"B {b}{units}, A − B {BinComparer.FormatDelta(delta.Physical[row, col], delta.Raw[row, col], z, DeltaMode)}";
            }

            return SelectedItem switch
            {
                XdfConstant constant => $"B {ValueCodec.Format(document.Reader.Codec.ToPhysical(constant.Math, image.ReadElement(constant.Data, 0)), image.ReadElement(constant.Data, 0), constant.DecimalPlaces, constant.OutputType, constant.Data.ElementBits)}{(constant.Units is { Length: > 0 } u && u != "-" ? " " + u : string.Empty)}",
                XdfFlag flag => $"B {((image.ReadElement(flag.Data, 0) & flag.Mask) != 0 ? "on" : "off")}",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Writes B's values into A: the selected cells of a map (one undo step), or the whole of a value, switch or flag.
    /// Ctrl Shift B, or the Copy from B and Use B's value buttons.
    /// </summary>
    public bool CopyFromB()
    {
        if (Document is not { } document || History is not { } history || _compareImage is not { } image || SelectedItem is not { } item)
        {
            return false;
        }

        try
        {
            if (Editor is { Selection: CellRange range } editor && item is XdfTable table)
            {
                var plan = TableEdits.CopyFrom(document, table, range, image);
                if (plan.Changes.Count == 0)
                {
                    editor.Message = "These cells already hold B's values.";
                    return false;
                }

                history.Apply(plan, $"Copy {plan.Changes.Count} {(plan.Changes.Count == 1 ? "cell" : "cells")} of {table.Title} from {CompareFileB}");
                editor.Message = plan.Warnings.Count > 0 ? string.Join(" ", plan.Warnings) : null;
                return true;
            }

            var copy = TableEdits.CopyFrom(document, item, image);
            if (copy.Changes.Count == 0)
            {
                return false;
            }

            history.Apply(copy, $"Use {CompareFileB}'s {item.Title}");
            return true;
        }
        catch (EditRefusedException error)
        {
            if (Editor is { } shown)
            {
                shown.Message = error.Message;
            }

            return false;
        }
    }

    // B's view of the shown map: the differing cells ringed on A, and the B or A − B grid when one is picked.
    private void RefreshCompare()
    {
        var old = _compareGrid;
        _comparison = Editor is { } editor && Document is { } document && _compareImage is { } image
            ? BinComparer.CompareTable(document.Xdf, document.Image, image, editor.Table)
            : null;
        if (Editor is { } shown)
        {
            shown.DiffersCells = _comparison?.Changed ?? new HashSet<(int Row, int Col)>();
        }

        _compareGrid = _comparison is { } compared && Editor is { } a && GridView != "a"
            ? GridView == "b"
                ? TableEditorViewModel.Snapshot(compared.B, compared.Changed, mark: CellMark.Differs)
                : TableEditorViewModel.Snapshot(BinComparer.Difference(compared, DeltaMode), new HashSet<(int Row, int Col)>(), isDelta: true, deltaMode: DeltaMode)
            : null;
        if (_compareGrid is { } grid && Editor is { } source)
        {
            grid.ShowRaw = source.ShowRaw;
            grid.Selection = source.Selection;
            grid.ActiveCell = source.ActiveCell;
            grid.PropertyChanged += (_, e) =>
            {
                // A selection made on B or A − B is A's selection too: Copy from B and the tools act on it.
                if (ReferenceEquals(grid, _compareGrid) && e.PropertyName is nameof(TableEditorViewModel.Selection) or nameof(TableEditorViewModel.ActiveCell))
                {
                    source.Selection = grid.Selection;
                    source.ActiveCell = grid.ActiveCell;
                }
            };
        }

        old?.Dispose();
        foreach (var name in new[] { nameof(ShownEditor), nameof(ShowsCompare), nameof(ShowsCompareGrid), nameof(ShowsUseB), nameof(ShowsDeltaMode), nameof(CompareStripText), nameof(CompareText), nameof(CompareAxisNote) })
        {
            OnPropertyChanged(name);
        }
    }

    public XdfItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                Editor?.Dispose();
                Value?.Dispose();
                Editor = value is XdfTable table && Document is not null && History is not null && Catalog is not null
                    ? new TableEditorViewModel(Document, History, table)
                    {
                        Selection = new CellRange(0, 0, 0, 0),
                        ShowRaw = _settings.OpenMapsRaw,
                        NumberRows = Catalog.Kind(table) == ItemKind.Axis,
                        Axis = _settings.EditAxis,
                    }
                    : null;
                Value = value is XdfConstant or XdfFlag && Document is not null && History is not null && Catalog is not null
                    ? new ValueEditorViewModel(Document, History, value, Catalog)
                    : null;

                // Facts read the editors, so rebuild them once the editors have refreshed after an edit.
                if (Editor is { } editor)
                {
                    editor.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is nameof(TableEditorViewModel.Values) or nameof(TableEditorViewModel.Selection) or nameof(TableEditorViewModel.ActiveCell)
                            && ReferenceEquals(editor, Editor))
                        {
                            BuildFacts();
                            if (e.PropertyName == nameof(TableEditorViewModel.Values))
                            {
                                // An edit to A changes what differs from B and A − B.
                                RefreshCompare();
                            }
                            else
                            {
                                OnPropertyChanged(nameof(CompareText));
                            }
                        }
                    };
                }

                if (Value is { } single)
                {
                    single.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(ValueEditorViewModel.Current) && ReferenceEquals(single, Value))
                        {
                            BuildFacts();
                            OnPropertyChanged(nameof(CompareText));
                        }
                    };
                }

                RefreshCompare();
                LiveCursor.Bind(Document, Editor);
                OnPropertyChanged(nameof(SelectedKindText));
                OnPropertyChanged(nameof(SelectedKind));
                OnPropertyChanged(nameof(NothingSelected));
                OnPropertyChanged(nameof(PinText));
                OnPropertyChanged(nameof(ReadOnlyReason));
                OnPropertyChanged(nameof(IsReadOnlyItem));
                OnPropertyChanged(nameof(ShowsSurface));
                OnPropertyChanged(nameof(ShowsCurve));
                OnPropertyChanged(nameof(ShowsEditAxis));
                OnPropertyChanged(nameof(CentreGrid));
                OnPropertyChanged(nameof(IsAxis));
                OnPropertyChanged(nameof(UsedBy));
                OnPropertyChanged(nameof(HasNoUsers));
                OnPropertyChanged(nameof(ShapeTitle));
                BuildFacts();
                if (value is not null)
                {
                    _settings.AddRecent(value.Title);
                }

                RebuildRail();
            }
        }
    }

    /// <summary>
    /// Opens a bin with an XDF. Without an XDF, the one last used for the bin's firmware is taken. Problems end up in
    /// <see cref="ErrorMessage"/> with their way out and leave the current document open; nothing but a cancellation
    /// escapes, so the start-up reopen cannot bring the app down.
    /// </summary>
    public async Task OpenAsync(string binPath, string? xdfPath)
    {
        using var hold = HoldMapWindows();
        ShowError(null);
        MissingXdfPath = null;
        IsBusy = true;
        var readingXdf = false;
        try
        {
            BusyText = $"Reading {Path.GetFileName(binPath)}…";
            var image = await Task.Run(() => BinImage.Load(binPath));
            var firmware = FirmwareIdentifier.Identify(image);
            var remembered = firmware.SoftwareId is string id && _settings.XdfByFirmware.TryGetValue(id, out var path) ? path : null;
            MissingXdfPath = xdfPath is null && remembered is not null && !File.Exists(remembered) ? remembered : null;
            xdfPath ??= remembered is not null && File.Exists(remembered) ? remembered : null;
            if (xdfPath is null)
            {
                ShowError($"Choose the XDF for {(firmware.SoftwareId is null ? "this bin" : "firmware " + firmware.SoftwareId)}.", TuneAction.OpenXdf);
                NeedsXdf = true;
                OnPropertyChanged(nameof(HasErrorBanner));
                return;
            }

            readingXdf = true;
            BusyText = $"Reading {Path.GetFileName(xdfPath)}…";
            var xdf = await Task.Run(() => XdfDefinition.LoadFromFile(xdfPath));
            var document = CalibrationDocument.FromParts(xdf, image, Path.GetFullPath(binPath));
            Attach(document, xdfPath);
        }
        catch (InvalidDataException error) when (!readingXdf)
        {
            // BinImage's message names the file and its size: "backup.bin is 262144 bytes. An MS43 bin is …".
            ShowError("Not opened: " + error.Message, TuneAction.OpenBin);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var file = Path.GetFileName(readingXdf ? xdfPath : binPath);
            ShowError($"Not opened: {file} could not be read ({Reason(error)}).", readingXdf ? TuneAction.OpenXdf : TuneAction.OpenBin);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>An exception's message as a clause inside a sentence: without its closing full stop.</summary>
    private static string Reason(Exception error) => error.Message.Trim().TrimEnd('.');

    public void TogglePin(XdfItem item)
    {
        var title = item.Title;
        var label = (_settings.IsPinned(title) ? "Unpin " : "Pin ") + title;
        FlipPin(title);
        Undos.Push(new UndoStep(label, () => FlipPin(title), () => FlipPin(title)));
    }

    private void FlipPin(string title)
    {
        _settings.TogglePin(title);
        OnPropertyChanged(nameof(PinText));
        RebuildRail();
    }

    /// <summary>How the rail orders categories and items. Pinned and Recent keep their own order; search keeps best matches first.</summary>
    public ItemSort Sort => _settings.RailSort;

    public bool SortDescending => _settings.RailSortDescending;

    public bool SortIsName => Sort == ItemSort.Name;

    public bool SortIsAddress => Sort == ItemSort.Address;

    public bool SortIsKind => Sort == ItemSort.Kind;

    public bool SortIsSize => Sort == ItemSort.Size;

    /// <summary>The sort button's tooltip, in the words of the chosen order.</summary>
    public string SortTip => "Sorted by " + (Sort, SortDescending) switch
    {
        (ItemSort.Address, false) => "address, lowest first",
        (ItemSort.Address, true) => "address, highest first",
        (ItemSort.Kind, false) => "kind, maps first",
        (ItemSort.Kind, true) => "kind, flags first",
        (ItemSort.Size, false) => "size, smallest first",
        (ItemSort.Size, true) => "size, largest first",
        (_, true) => "name, Z to A",
        _ => "name, A to Z",
    };

    public void SetSort(ItemSort sort, bool descending)
    {
        if (_settings.RailSort == sort && _settings.RailSortDescending == descending)
        {
            return;
        }

        (_settings.RailSort, _settings.RailSortDescending) = (sort, descending);
        _settings.Save();
        foreach (var name in new[] { nameof(Sort), nameof(SortDescending), nameof(SortIsName), nameof(SortIsAddress), nameof(SortIsKind), nameof(SortIsSize), nameof(SortTip) })
        {
            OnPropertyChanged(name);
        }

        RebuildRail();
    }

    public const double RailMinWidth = 200;

    public const double RailMaxWidth = 480;

    /// <summary>The rail's width as the user dragged it, or null for the layout's own (256 px, 224 below 1280).</summary>
    public double? RailWidth => _settings.RailWidth;

    /// <summary>True while the rail is folded to its strip (Ctrl B). Remembered between runs.</summary>
    public bool RailCollapsed => _settings.RailCollapsed;

    public void SetRailCollapsed(bool collapsed)
    {
        if (_settings.RailCollapsed == collapsed)
        {
            return;
        }

        _settings.RailCollapsed = collapsed;
        _settings.Save();
        OnPropertyChanged(nameof(RailCollapsed));
    }

    /// <summary>Sets the rail's width, kept between <see cref="RailMinWidth"/> and <see cref="RailMaxWidth"/>; null goes back to the default.</summary>
    public void SetRailWidth(double? width, bool save)
    {
        _settings.RailWidth = width is double wanted ? Math.Clamp(Math.Round(wanted), RailMinWidth, RailMaxWidth) : null;
        if (save)
        {
            _settings.Save();
        }

        OnPropertyChanged(nameof(RailWidth));
    }

    public void ToggleCategory(string name)
    {
        if (!_expanded.Remove(name))
        {
            _expanded.Add(name);
        }

        RebuildRail();
    }

    public void RefreshModified() => RebuildRail();

    /// <summary>The XDF the open bin is read with, in full (Compare's swap reopens the other bin with it).</summary>
    public string? XdfPath { get; private set; }

    private void Attach(CalibrationDocument document, string xdfPath)
    {
        // The windows edit the old bin's tables through its history; a new bin or XDF closes them.
        CloseMapWindows();
        XdfPath = Path.GetFullPath(xdfPath);
        if (Document is not null)
        {
            Document.Changed -= OnDocumentChanged;
        }

        Document = document;
        document.Changed += OnDocumentChanged;
        // A new file starts a new history; pins made before it are no longer undone from here.
        Undos.Clear();
        var history = new EditHistory(document);
        History = history;
        _historyDepth = 0;
        history.Changed += (_, _) =>
        {
            // Every edit goes through EditHistory.Apply (grid, value box, switches, Compare's copy); each new one becomes
            // a step here. Steps replayed by Ctrl Z or Ctrl Y move the count too, but are already on the stack.
            if (!Undos.IsReplaying && history.UndoCount > _historyDepth && history.NextUndoLabel is { } label)
            {
                Undos.Push(new UndoStep(label, () => history.Undo(), () => history.Redo()));
            }

            _historyDepth = history.UndoCount;
        };
        Catalog = new XdfCatalog(document.Xdf);
        PlateFile = Path.GetFileName(document.Path ?? "untitled.bin");
        PlateFirmware = document.Firmware.SoftwareId is string id ? "MS43" + (id.StartsWith("43", StringComparison.Ordinal) ? id[2..] : id) : "Unknown firmware";
        PlateXdf = Path.GetFileName(xdfPath);
        FirmwareWarning = document.XdfMatchesFirmware
            ? null
            : $"This XDF describes {document.Xdf.Title}, but the bin is firmware {document.Firmware.SoftwareId ?? "unknown"}. Values may be wrong.";

        if (document.Firmware.SoftwareId is string software && document.XdfMatchesFirmware)
        {
            _settings.XdfByFirmware[software] = Path.GetFullPath(xdfPath);
        }

        _settings.LastBin = document.Path;
        _settings.Save();
        _expanded.Clear();
        SelectedItem = null;
        LiveCursor.Bind(document, null);
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(NothingSelected));
        OnPropertyChanged(nameof(ShowsBusyBar));
        OnPropertyChanged(nameof(HasErrorBanner));
        OnPropertyChanged(nameof(PlateTip));
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SaveTip));
        RebuildRail();
        DocumentOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SaveTip));
        BuildFacts();
        RebuildRail();
    }

    private void BuildFacts()
    {
        var limits = new List<Fact>();
        var details = new List<Fact>();
        ScaleCaption = null;
        SurfaceNote = null;
        SetScaleLabels(null, null, null);
        string Hex(long? address) => address is long a ? "0x" + a.ToString("X5", System.Globalization.CultureInfo.InvariantCulture) : "none";
        string Bits(XdfData data) => $"{data.ElementBits} bit{(data.Signed ? ", signed" : string.Empty)}";

        switch (SelectedItem)
        {
            case XdfTable table when Editor is { } editor && Catalog is { } catalog && Document is { } document:
                var values = editor.Values;
                var z = table.Z;
                AddLimits(limits, EditLimits.For(document.Reader.Codec, z.Math, z.Data, z.Min, z.Max), editor.Units, z.DecimalPlaces);
                if (editor.Selection is not null)
                {
                    var (row, col) = editor.ActiveCell;
                    var position = editor.Position(row, col);
                    limits.Add(position.Length == 0
                        ? new Fact("Active cell", editor.CellText(row, col))
                        : new Fact("Active cell", position, editor.CellText(row, col)));
                }

                details.Add(new Fact("Address", Hex(z.Data.Address)));
                details.Add(new Fact("Size", $"{values.Rows} × {values.Cols}, {Bits(z.Data)}"));
                if (editor.Units.Length > 0)
                {
                    details.Add(new Fact("Units", editor.Units));
                }

                details.Add(new Fact("Scaling", z.Math.Equation, TextIsId: true));
                // An axis's rows are its numbered breakpoints, not an axis of their own (system 5.13).
                if (values.Rows > 1 && !editor.NumberRows)
                {
                    details.Add(Axis("Rows", values.Y, values.Rows));
                }

                if (values.Cols > 1)
                {
                    details.Add(Axis("Columns", values.X, values.Cols));
                }

                DescribeScale(editor);

                break;
            case XdfConstant constant when Value is { } value:
                var raw = $"raw {value.Current.Raw} (0x{Masked(value.Current.Raw, constant.Data):X})";
                if (value.Choices is { } choices)
                {
                    // A switch (system 5.13): its choices come from the XDF description, its range in words.
                    limits.Add(new Fact("Choices", $"{choices.Count}, from the XDF description"));
                    if (SwitchRange(value.Limits) is { } range)
                    {
                        limits.Add(new Fact("XDF range", range));
                    }

                    limits.Add(new Fact("Value", value.SelectedChoice?.Label ?? $"{value.Current.Display}, not one of the choices", raw));
                }
                else
                {
                    AddLimits(limits, value.Limits, value.Units, constant.DecimalPlaces);
                    limits.Add(new Fact("Value", WithUnits(value.Current.Display, value.Units), raw));
                }

                details.Add(new Fact("Address", Hex(constant.Data.Address)));
                details.Add(new Fact("Size", Bits(constant.Data)));
                if (value.Units.Length > 0)
                {
                    details.Add(new Fact("Units", value.Units));
                }

                details.Add(new Fact("Scaling", constant.Math.Equation, TextIsId: true));
                break;
            case XdfFlag flag:
                details.Add(new Fact("Address", Hex(flag.Data.Address)));
                details.Add(new Fact("Size", Bits(flag.Data)));
                details.Add(new Fact("Bit mask", "0x" + flag.Mask.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
                break;
        }

        LimitFacts = limits;
        DetailFacts = details;
    }

    /// <summary>The colour scale's labels and caption and the surface's note, from the editor's colour range (system 2.2).</summary>
    private void DescribeScale(TableEditorViewModel editor)
    {
        var values = editor.Values;
        var z = values.Table.Z;
        var range = editor.ColourRange;
        var hatched = range.Hatched;
        string Number(double value) => ValueCodec.Format(value, 0, z.DecimalPlaces, null, 16);
        string Cell(int row, int col) => ValueCodec.Format(values.Physical[row, col], values.Raw[row, col], z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
        var limits = string.Join(" and ", hatched.OrderBy(cell => cell).Select(cell => Cell(cell.Row, cell.Col)).Distinct());
        var hatchNote = hatched.Count > 0 ? $" Hatched cells are at the storage limit ({limits})." : string.Empty;
        var working = Enumerable.Range(0, values.Rows).SelectMany(r => Enumerable.Range(0, values.Cols).Select(c => (Row: r, Col: c)))
            .Where(cell => !hatched.Contains(cell) && double.IsFinite(values.Physical[cell.Row, cell.Col])).ToList();
        if (working.Count == 0)
        {
            return;
        }

        if (range.IsConstant)
        {
            var value = WithUnits(Cell(working[0].Row, working[0].Col), editor.Units);
            ScaleCaption = hatched.Count == 0 ? $"All {working.Count} cells are {value}" : $"All {working.Count} working cells are {value}.{hatchNote}";
            SetScaleLabels(Number(range.Min), null, Number(range.Max));
        }
        else
        {
            ScaleCaption = $"Colour: {WithUnits($"{Number(range.Min)} to {Number(range.Max)}", editor.Units)}.{hatchNote}";
            SetScaleLabels(Number(range.Min), Number((range.Min + range.Max) / 2), Number(range.Max));
        }

        if (ShowsSurface && hatched.Count > 0)
        {
            var rows = Enumerable.Range(0, values.Rows).Where(r => Enumerable.Range(0, values.Cols).All(c => hatched.Contains((r, c)))).ToList();
            var onlyRows = hatched.All(cell => rows.Contains(cell.Row));
            var units = values.Y.Units.Length > 0 ? " " + values.Y.Units : string.Empty;
            var labels = rows.Select(editor.RowLabel).ToList();
            SurfaceNote = !onlyRows ? $"{hatched.Count} cells at the storage limit are not drawn."
                : rows.Count == 1 ? $"The {labels[0]}{units} row (storage limit) is not drawn."
                : $"The {string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}{units} rows (storage limit) are not drawn.";
        }
    }

    private void SetScaleLabels(string? min, string? mid, string? max)
    {
        _scaleLabels = (min, mid, max);
        OnPropertyChanged(nameof(ScaleMinLabel));
        OnPropertyChanged(nameof(ScaleMidLabel));
        OnPropertyChanged(nameof(ScaleMaxLabel));
    }

    /// <summary>"Storable", "Step" and, when the XDF narrows it, "XDF range", in the item's units (system 5.13).</summary>
    private static void AddLimits(List<Fact> facts, EditLimits limits, string units, int? decimals)
    {
        string Number(double value) => ValueCodec.Format(value, 0, decimals, null, 16);
        facts.Add(new Fact("Storable", WithUnits($"{Number(limits.StorableMin)} to {Number(limits.StorableMax)}", units)));
        if (limits.Step is double step)
        {
            // The step keeps every digit it has: 0.375, not 0.4.
            facts.Add(new Fact("Step", WithUnits(step.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture), units)));
        }

        if (limits.XdfLow is not null || limits.XdfHigh is not null)
        {
            facts.Add(new Fact("XDF range", WithUnits($"{Number(limits.XdfLow ?? limits.StorableMin)} to {Number(limits.XdfHigh ?? limits.StorableMax)}", units)));
        }
    }

    // A switch's XDF range in words (system 5.13): "up to 4", "from 1" or "1 to 4"; null where it narrows nothing.
    private static string? SwitchRange(EditLimits limits)
    {
        static string Whole(double value) => ValueCodec.Format(value, 0, 0, null, 16);
        return (limits.XdfLow, limits.XdfHigh) switch
        {
            (double low, double high) => $"{Whole(low)} to {Whole(high)}",
            (null, double high) => $"up to {Whole(high)}",
            (double low, null) => $"from {Whole(low)}",
            _ => null,
        };
    }

    // The raw value as its element stores it, so a negative signed value shows its own bits ("0xFF", not 16 F's).
    private static long Masked(long raw, XdfData data) => data.ElementBits >= 64 ? raw : raw & ((1L << data.ElementBits) - 1);

    /// <summary>An axis as the mockup's two lines: its id, then "rpm, 20 points".</summary>
    private static Fact Axis(string label, AxisValues axis, int count)
    {
        var summary = string.Join(", ", new[] { axis.Units, $"{count} points" }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(axis.Name) ? new Fact(label, summary) : new Fact(label, axis.Name, summary, TextIsId: true);
    }

    private static string WithUnits(string text, string units) => units.Length > 0 && units != "-" ? $"{text} {units}" : text;

    private void RebuildRail()
    {
        FillRail();
        OnPropertyChanged(nameof(NoMatchText));
    }

    private void FillRail()
    {
        Rail.Clear();
        if (Catalog is not XdfCatalog catalog || Document is not CalibrationDocument document)
        {
            return;
        }

        var modified = document.ModifiedItems().ToHashSet();
        var differs = CompareItems ?? new HashSet<XdfItem>();
        RailItem Entry(XdfItem item, bool indented) =>
            new(item, catalog.Kind(item), _settings.IsPinned(item.Title), modified.Contains(item), ReferenceEquals(item, SelectedItem), indented, differs.Contains(item),
                ItemFacts.Line(item, catalog.Kind(item)));
        IReadOnlyList<XdfItem> Sorted(IEnumerable<XdfItem> items) => catalog.Sort(items, Sort, SortDescending);

        if (SearchText.Trim().Length > 0)
        {
            foreach (var item in catalog.Search(SearchText).Where(item => (!ModifiedOnly || modified.Contains(item)) && (!DiffersOnly || differs.Contains(item))))
            {
                Rail.Add(Entry(item, false));
            }

            return;
        }

        if (DiffersOnly)
        {
            var listed = Sorted(differs.Where(item => !ModifiedOnly || modified.Contains(item))).ToList();
            Rail.Add(new RailGroup(listed.Count == 0 ? $"Nothing differs from {CompareFileB}" : $"Differs from {CompareFileB}"));
            listed.ForEach(item => Rail.Add(Entry(item, false)));
            return;
        }

        if (ModifiedOnly)
        {
            Rail.Add(new RailGroup(modified.Count == 0 ? "No changes since the last save" : "Changed since the last save"));
            foreach (var item in Sorted(modified))
            {
                Rail.Add(Entry(item, false));
            }

            return;
        }

        var pinned = _settings.PinnedInOrder().Select(document.Xdf.FindByTitle).OfType<XdfItem>().ToList();
        if (pinned.Count > 0)
        {
            Rail.Add(new RailGroup("Pinned"));
            pinned.ForEach(item => Rail.Add(Entry(item, false)));
        }

        var recent = _settings.Recent.Select(document.Xdf.FindByTitle).OfType<XdfItem>().Except(pinned).Take(5).ToList();
        if (recent.Count > 0)
        {
            Rail.Add(new RailGroup("Recent"));
            recent.ForEach(item => Rail.Add(Entry(item, false)));
        }

        Rail.Add(new RailGroup("Categories"));
        foreach (var (name, count) in catalog.Categories(Sort, SortDescending))
        {
            var expanded = _expanded.Contains(name);
            Rail.Add(new RailCategory(name, count, expanded, catalog.Summary(name)));
            if (expanded)
            {
                foreach (var item in Sorted(catalog.InCategory(name)))
                {
                    Rail.Add(Entry(item, true));
                }
            }
        }
    }

    private void SetPlate<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetField(ref field, value, name))
        {
            OnPropertyChanged(nameof(PlateTip));
        }
    }
}
