using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public abstract record RailEntry;

public sealed record RailGroup(string Title) : RailEntry;

public sealed record RailCategory(string Name, int Count, bool IsExpanded) : RailEntry;

public sealed record Fact(string Label, string Text);

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

public sealed record RailItem(XdfItem Item, ItemKind Kind, bool IsPinned, bool IsModified, bool IsCurrent, bool IsIndented, bool IsDifferent = false) : RailEntry
{
    public string Title => Item.Title;
}

/// <summary>The Tune screen: the open bin and XDF, the map rail, and the selected item.</summary>
public sealed class TuneViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private CalibrationDocument? _document;
    private EditHistory? _history;
    private XdfCatalog? _catalog;
    private XdfItem? _selectedItem;
    private TableEditorViewModel? _editor;
    private ValueEditorViewModel? _value;
    private IReadOnlyList<Fact> _facts = Array.Empty<Fact>();
    private string _legendMin = string.Empty;
    private string _legendMax = string.Empty;
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
    private bool _isBusy;

    public TuneViewModel(AppSettings settings, TimeProvider? time = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        LiveCursor = new LiveCursorController(time);
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

    public string PlateFile { get => _plateFile; private set => SetField(ref _plateFile, value); }

    public string? PlateFirmware { get => _plateFirmware; private set => SetField(ref _plateFirmware, value); }

    public string? PlateXdf { get => _plateXdf; private set => SetField(ref _plateXdf, value); }

    /// <summary>Set when the XDF was written for other firmware than the bin's.</summary>
    public string? FirmwareWarning { get => _firmwareWarning; private set => SetField(ref _firmwareWarning, value); }

    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

    /// <summary>"Saved", or how many items changed since the last save.</summary>
    public string DirtyText
    {
        get
        {
            var count = Document?.ModifiedItems().Count ?? 0;
            return Document is null ? string.Empty : count == 0 ? "Saved" : $"{count} unsaved {(count == 1 ? "change" : "changes")}";
        }
    }

    /// <summary>The editor for the selected table, map, curve or axis; null for values, switches and flags.</summary>
    public TableEditorViewModel? Editor { get => _editor; private set => SetField(ref _editor, value); }

    /// <summary>The editor for the selected value, switch or flag; null for tables.</summary>
    public ValueEditorViewModel? Value { get => _value; private set => SetField(ref _value, value); }

    public IReadOnlyList<Fact> Facts { get => _facts; private set => SetField(ref _facts, value); }

    public bool ShowsSurface => Editor is { Values: { Rows: > 1, Cols: > 1 } };

    public bool ShowsCurve => Editor is not null && !ShowsSurface;

    public string LegendMin { get => _legendMin; private set => SetField(ref _legendMin, value); }

    public string LegendMax { get => _legendMax; private set => SetField(ref _legendMax, value); }

    /// <summary>Asks whether to save unsaved edits; set by the window. Without it, unsaved edits are never dropped.</summary>
    public Func<string, Task<SaveChoice>>? AskToSave { get; set; }

    /// <summary>The number typed in the toolbar's value box.</summary>
    public string ToolValue { get => _toolValue; set => SetField(ref _toolValue, value ?? string.Empty); }

    public bool CanUndo => History?.CanUndo ?? false;

    public bool CanRedo => History?.CanRedo ?? false;

    public string UndoTip => History?.NextUndoLabel is { } label ? "Undo: " + label : "Nothing to undo";

    /// <summary>Runs a toolbar operation on the selected cells; Set, Add and Scale % use <see cref="ToolValue"/>.</summary>
    public bool RunTool(EditKind kind)
    {
        if (Editor is not { } editor)
        {
            return false;
        }

        var value = 0.0;
        if (kind is EditKind.Set or EditKind.Add or EditKind.Scale &&
            !double.TryParse(ToolValue.Replace('−', '-').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            editor.Message = ToolValue.Trim().Length == 0 ? "Type a value first." : $"'{ToolValue}' is not a number.";
            return false;
        }

        return editor.Apply(new EditRequest(kind, value));
    }

    public void Undo() => History?.Undo();

    public void Redo() => History?.Redo();

    /// <summary>Saves to the open file, or to <paramref name="path"/> (Save As). Problems are shown in <see cref="ErrorMessage"/>.</summary>
    public bool Save(string? path = null)
    {
        if (Document is not { } document)
        {
            return false;
        }

        try
        {
            document.Save(path);
            ErrorMessage = null;
            PlateFile = Path.GetFileName(document.Path ?? PlateFile);
            _settings.LastBin = document.Path;
            _settings.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = "Not saved: " + error.Message;
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
    public void CompareWith(string? fileB, IReadOnlySet<XdfItem>? items)
    {
        CompareFileB = fileB;
        CompareItems = items;
        OnPropertyChanged(nameof(HasCompare));
        if (items is null)
        {
            DiffersOnly = false;
        }

        RebuildRail();
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
                Editor = value is XdfTable table && Document is not null && History is not null
                    ? new TableEditorViewModel(Document, History, table) { Selection = new CellRange(0, 0, 0, 0) }
                    : null;
                Value = value is XdfConstant or XdfFlag && Document is not null && History is not null && Catalog is not null
                    ? new ValueEditorViewModel(Document, History, value, Catalog)
                    : null;

                // Facts read the editors, so rebuild them once the editors have refreshed after an edit.
                if (Editor is { } editor)
                {
                    editor.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(TableEditorViewModel.Values) && ReferenceEquals(editor, Editor))
                        {
                            BuildFacts();
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
                        }
                    };
                }
                LiveCursor.Bind(Document, Editor);
                OnPropertyChanged(nameof(SelectedKindText));
                OnPropertyChanged(nameof(ShowsSurface));
                OnPropertyChanged(nameof(ShowsCurve));
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
    /// <see cref="ErrorMessage"/> and leave the current document open.
    /// </summary>
    public async Task OpenAsync(string binPath, string? xdfPath)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var image = await Task.Run(() => BinImage.Load(binPath));
            var firmware = FirmwareIdentifier.Identify(image);
            xdfPath ??= firmware.SoftwareId is string id && _settings.XdfByFirmware.TryGetValue(id, out var remembered) && File.Exists(remembered) ? remembered : null;
            if (xdfPath is null)
            {
                ErrorMessage = $"Choose the XDF for {(firmware.SoftwareId is null ? "this bin" : "firmware " + firmware.SoftwareId)}.";
                return;
            }

            var xdf = await Task.Run(() => XdfDefinition.LoadFromFile(xdfPath));
            var document = CalibrationDocument.FromParts(xdf, image, Path.GetFullPath(binPath));
            Attach(document, xdfPath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void TogglePin(XdfItem item)
    {
        _settings.TogglePin(item.Title);
        RebuildRail();
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

    private void Attach(CalibrationDocument document, string xdfPath)
    {
        if (Document is not null)
        {
            Document.Changed -= OnDocumentChanged;
        }

        Document = document;
        document.Changed += OnDocumentChanged;
        History = new EditHistory(document);
        History.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(UndoTip));
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
        OnPropertyChanged(nameof(DirtyText));
        RebuildRail();
        DocumentOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DirtyText));
        BuildFacts();
        RebuildRail();
    }

    private void BuildFacts()
    {
        var facts = new List<Fact>();
        LegendMin = LegendMax = string.Empty;
        string Hex(long? address) => address is long a ? "0x" + a.ToString("X5", System.Globalization.CultureInfo.InvariantCulture) : "none";
        string Bits(XdfData data) => $"{data.ElementBits} bit{(data.Signed ? ", signed" : string.Empty)}";
        string Axis(AxisValues axis, int count) =>
            string.Join(", ", new[] { axis.Name, axis.Units, $"{count} points" }.Where(part => !string.IsNullOrWhiteSpace(part)));

        switch (SelectedItem)
        {
            case XdfTable table when Editor is { } editor && Catalog is { } catalog:
                var values = editor.Values;
                facts.Add(new Fact("Address", Hex(table.Z.Data.Address)));
                facts.Add(new Fact("Size", $"{values.Rows} × {values.Cols}, {Bits(table.Z.Data)}"));
                if (table.Z.Units.Length > 0)
                {
                    facts.Add(new Fact("Units", table.Z.Units));
                }

                facts.Add(new Fact("Scaling", table.Z.Math.Equation));
                if (values.Rows > 1)
                {
                    facts.Add(new Fact("Rows", Axis(values.Y, values.Rows)));
                }

                if (values.Cols > 1)
                {
                    facts.Add(new Fact("Columns", Axis(values.X, values.Cols)));
                }

                if (catalog.Kind(table) == ItemKind.Axis)
                {
                    var users = catalog.AxisUsers(table);
                    facts.Add(new Fact("Used by", users.Count == 0 ? "no table in this XDF" : string.Join(", ", users.Take(4).Select(user => user.Title)) + (users.Count > 4 ? $" and {users.Count - 4} more" : string.Empty)));
                }

                if (!table.Z.Data.IsSupported)
                {
                    facts.Add(new Fact("Read-only", table.Z.Data.UnsupportedReason ?? string.Empty));
                }

                var finite = values.Physical.Cast<double>().Where(double.IsFinite).ToList();
                if (finite.Count > 0)
                {
                    var units = table.Z.Units.Length > 0 ? " " + table.Z.Units : string.Empty;
                    LegendMin = ValueCodec.Format(finite.Min(), 0, table.Z.DecimalPlaces, null, 16).Replace('-', '−') + units;
                    LegendMax = ValueCodec.Format(finite.Max(), 0, table.Z.DecimalPlaces, null, 16).Replace('-', '−') + units;
                }

                break;
            case XdfConstant constant when Value is { } value:
                facts.Add(new Fact("Address", Hex(constant.Data.Address)));
                facts.Add(new Fact("Size", Bits(constant.Data)));
                facts.Add(new Fact("Raw", $"{value.Current.Raw} (0x{value.Current.Raw:X})"));
                facts.Add(new Fact("Scaling", constant.Math.Equation));
                if (constant.RangeLow is not null || constant.RangeHigh is not null)
                {
                    facts.Add(new Fact("Range", $"{constant.RangeLow?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "…"} to {constant.RangeHigh?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "…"}"));
                }

                break;
            case XdfFlag flag:
                facts.Add(new Fact("Address", Hex(flag.Data.Address)));
                facts.Add(new Fact("Bit mask", "0x" + flag.Mask.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
                break;
        }

        Facts = facts;
    }

    private void RebuildRail()
    {
        Rail.Clear();
        if (Catalog is not XdfCatalog catalog || Document is not CalibrationDocument document)
        {
            return;
        }

        var modified = document.ModifiedItems().ToHashSet();
        var differs = CompareItems ?? new HashSet<XdfItem>();
        RailItem Entry(XdfItem item, bool indented) =>
            new(item, catalog.Kind(item), _settings.IsPinned(item.Title), modified.Contains(item), ReferenceEquals(item, SelectedItem), indented, differs.Contains(item));

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
            var listed = differs.Where(item => !ModifiedOnly || modified.Contains(item)).OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();
            Rail.Add(new RailGroup(listed.Count == 0 ? $"Nothing differs from {CompareFileB}" : $"Differs from {CompareFileB}"));
            listed.ForEach(item => Rail.Add(Entry(item, false)));
            return;
        }

        if (ModifiedOnly)
        {
            Rail.Add(new RailGroup(modified.Count == 0 ? "No changes since the last save" : "Changed since the last save"));
            foreach (var item in modified)
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
        foreach (var (name, count) in catalog.Categories())
        {
            var expanded = _expanded.Contains(name);
            Rail.Add(new RailCategory(name, count, expanded));
            if (expanded)
            {
                foreach (var item in catalog.InCategory(name))
                {
                    Rail.Add(Entry(item, true));
                }
            }
        }
    }
}
