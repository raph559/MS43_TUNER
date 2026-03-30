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

public sealed record RailItem(XdfItem Item, ItemKind Kind, bool IsPinned, bool IsModified, bool IsCurrent, bool IsIndented) : RailEntry
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
    private string _plateFile = "No bin open";
    private string? _plateFirmware;
    private string? _plateXdf;
    private string? _firmwareWarning;
    private string? _errorMessage;
    private bool _isBusy;

    public TuneViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public event EventHandler? DocumentOpened;

    public AppSettings Settings => _settings;

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
}
