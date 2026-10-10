using System;
using System.ComponentModel;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// A map, curve or axis open in a window of its own, as TunerPro opens its tables: the editor, the toolbar's value and a
/// live cursor of its own. Edits go through the bin's one history, so Undo here and in the main window take back the
/// same steps, and an edit shows at once wherever the same table is open.
/// </summary>
public sealed class MapWindowViewModel : ViewModelBase
{
    private readonly TuneViewModel _tune;
    private string _toolValue = string.Empty;

    internal MapWindowViewModel(TuneViewModel tune, XdfTable table, TimeProvider? time)
    {
        _tune = tune;
        Table = table;
        Kind = tune.Catalog!.Kind(table);
        Editor = new TableEditorViewModel(tune.Document!, tune.History!, table)
        {
            Selection = new CellRange(0, 0, 0, 0),
            ShowRaw = tune.Settings.OpenMapsRaw,
            NumberRows = Kind == ItemKind.Axis,
            Axis = tune.EditAxis,
        };

        // Shown whatever tab the main window is on: the window is on screen for as long as it is open.
        LiveCursor = new LiveCursorController(time)
        {
            AutoTick = tune.LiveCursor.AutoTick, Trail = tune.LiveCursor.Trail, Coordinator = tune.LiveCursor.Coordinator, IsVisible = true,
        };
        LiveCursor.Bind(tune.Document, Editor);
        tune.Undos.Changed += OnUndosChanged;
        tune.PropertyChanged += OnTuneChanged;
    }

    /// <summary>Raised once, when the window is closed from either side: its own close button or the bin going away.</summary>
    public event EventHandler? Closed;

    /// <summary>The table was opened again: its window comes to the front.</summary>
    public event EventHandler? ActivateRequested;

    public XdfTable Table { get; }

    public ItemKind Kind { get; }

    public string Title => Table.Title;

    public string Description => Table.Description;

    public TableEditorViewModel Editor { get; }

    public LiveCursorController LiveCursor { get; }

    public bool IsClosed { get; private set; }

    /// <summary>True while another bin or XDF is being chosen and read: this window takes no edits, and closes with the old bin.</summary>
    public bool IsHeld => _tune.MapWindowsHeld;

    /// <summary>The Tune tab's problem line, such as a save that failed: Ctrl S works from here, so its outcome shows here too.</summary>
    public string? ErrorMessage => _tune.HasErrorBanner ? _tune.ErrorMessage : null;

    /// <summary>"'colmajor' is read-only: …" for storage the app cannot write, as on the Tune tab; null when editable.</summary>
    public string? ReadOnlyReason => Table.Z.Data.UnsupportedReason is { } reason ? $"'{Table.Title}' is read-only: {reason}." : null;

    public bool IsReadOnlyItem => ReadOnlyReason is not null;

    public bool IsAxis => Kind == ItemKind.Axis;

    /// <summary>A one-column table (a curve or an axis) sits centred with its key line, as on the Tune tab.</summary>
    public bool CentreGrid => Editor.Values.Cols == 1;

    /// <summary>X, Y and XY show for a map; on a curve the tools work along it.</summary>
    public bool ShowsEditAxis => Editor.IsMap;

    /// <summary>The number typed in this window's value box.</summary>
    public string ToolValue { get => _toolValue; set => SetField(ref _toolValue, value ?? string.Empty); }

    /// <summary>Which way Interpolate and Smooth work: one remembered choice with the Tune tab and the other windows.</summary>
    public bool IsEditAxisX { get => _tune.EditAxis == EditAxis.X; set { if (value) { _tune.EditAxis = EditAxis.X; } else { OnPropertyChanged(); } } }

    public bool IsEditAxisY { get => _tune.EditAxis == EditAxis.Y; set { if (value) { _tune.EditAxis = EditAxis.Y; } else { OnPropertyChanged(); } } }

    public bool IsEditAxisXY { get => _tune.EditAxis == EditAxis.XY; set { if (value) { _tune.EditAxis = EditAxis.XY; } else { OnPropertyChanged(); } } }

    public bool CanUndo => _tune.CanUndo;

    public bool CanRedo => _tune.CanRedo;

    /// <summary>The bin's next undo, which may belong to another map: its label names the map ("Undo: Add 1 to 4 cells of …").</summary>
    public string UndoTip => _tune.UndoTip;

    public string RedoTip => _tune.RedoTip;

    /// <summary>Runs a toolbar operation on this window's selection; Set, Add and Scale % use <see cref="ToolValue"/>.</summary>
    public bool RunTool(EditKind kind) => Editor.ApplyTool(kind, ToolValue);

    public void Undo() => _tune.Undo();

    public void Redo() => _tune.Redo();

    /// <summary>Asks the window to come to the front.</summary>
    public void Activate() => ActivateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Lets go of the bin and tells the window and the Tune tab; a second call does nothing.</summary>
    public void Close()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        _tune.Undos.Changed -= OnUndosChanged;
        _tune.PropertyChanged -= OnTuneChanged;
        LiveCursor.Clear();
        LiveCursor.Bind(null, null);
        Editor.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUndosChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
    }

    private void OnTuneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TuneViewModel.MapWindowsHeld))
        {
            OnPropertyChanged(nameof(IsHeld));
        }

        if (e.PropertyName is nameof(TuneViewModel.ErrorMessage) or nameof(TuneViewModel.HasErrorBanner))
        {
            OnPropertyChanged(nameof(ErrorMessage));
        }

        if (e.PropertyName == nameof(TuneViewModel.EditAxis))
        {
            Editor.Axis = _tune.EditAxis;
            OnPropertyChanged(nameof(IsEditAxisX));
            OnPropertyChanged(nameof(IsEditAxisY));
            OnPropertyChanged(nameof(IsEditAxisXY));
        }
    }
}
