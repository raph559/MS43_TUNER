using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public enum EditKind
{
    Set,
    Add,
    Scale,
    Interpolate,
    Smooth,
    Step,
    Paste,
}

public sealed record EditRequest(EditKind Kind, double Value = 0, string? Text = null);

public enum CellMark
{
    Changed,
    Differs,
}

/// <summary>One open table (map, curve or axis): its values, the selection, and edits through the undo history.</summary>
public sealed class TableEditorViewModel : ViewModelBase, IDisposable
{
    public const string ReadOnlyMessage = "This grid is read-only. Edit A on the Tune tab, or copy from B.";

    private readonly CalibrationDocument? _document;
    private readonly EditHistory? _history;
    private TableValues _values;
    private CellRange? _selection;
    private bool _showRaw;
    private string? _message;
    private LiveCursorState? _live;
    private (int Row, int Col) _active;
    private MapColourRange _colourRange;
    private string _typedText = string.Empty;
    private IReadOnlySet<(int Row, int Col)> _changedCells = new HashSet<(int, int)>();

    public TableEditorViewModel(CalibrationDocument document, EditHistory history, XdfTable table)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _values = document.Reader.ReadTable(table);
        _colourRange = MapColourRange.Compute(_values);
        _changedCells = FindChangedCells();
        _document.Changed += OnDocumentChanged;
    }

    private TableEditorViewModel(TableValues values, IReadOnlySet<(int Row, int Col)> marked, bool isDelta, CellMark mark)
    {
        Table = values.Table;
        _values = values;
        _colourRange = MapColourRange.Compute(values);
        _changedCells = marked;
        IsDelta = isDelta;
        Mark = mark;
    }

    /// <summary>A read-only grid of fixed values (a comparison's A, B or B − A); <paramref name="marked"/> get the changed-cell dot.</summary>
    public static TableEditorViewModel Snapshot(TableValues values, IReadOnlySet<(int Row, int Col)> marked, bool isDelta = false, CellMark mark = CellMark.Changed) =>
        new(values ?? throw new ArgumentNullException(nameof(values)), marked ?? throw new ArgumentNullException(nameof(marked)), isDelta, mark);

    /// <summary>What the marked cells wear: the changed triangle, or the differs ring on Compare's A and B (system 2.4).</summary>
    public CellMark Mark { get; } = CellMark.Changed;

    /// <summary>The working cells' range the map colours span, and the cells hatched at the storage limit (system 2.2).</summary>
    public MapColourRange ColourRange { get => _colourRange; private set => SetField(ref _colourRange, value); }

    /// <summary>The digits typed into the active cell; Enter sets the selection to them, Esc drops them.</summary>
    public string TypedText
    {
        get => _typedText;
        set
        {
            if (SetField(ref _typedText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(TypingHint));
                OnPropertyChanged(nameof(IsTyping));
            }
        }
    }

    public bool IsTyping => TypedText.Length > 0;

    /// <summary>The key line while typing: "Enter sets 12 cells to 27.8 °CRK. Esc cancels."</summary>
    public string? TypingHint => TypedText.Length == 0 || Selection is not CellRange range
        ? null
        : $"Enter sets {Count(range.Rows * range.Cols, "cell")} to {WithUnits(TypedText, Units)}. Esc cancels.";

    /// <summary>An axis's breakpoints are numbered 1 to n instead of labelled by an axis of their own.</summary>
    public bool NumberRows { get; init; }

    /// <summary>A row's header as the grid draws it, with an ASCII minus.</summary>
    public string RowLabel(int row) => NumberRows ? (row + 1).ToString(CultureInfo.InvariantCulture) : Point(Values.Y, row);

    /// <summary>True for snapshots: edits are refused with <see cref="ReadOnlyMessage"/>.</summary>
    public bool IsReadOnly => _document is null;

    /// <summary>True when the values are B − A differences, drawn on the diverging scale.</summary>
    public bool IsDelta { get; }

    /// <summary>Raised after an edit was written, with the cells it touched.</summary>
    public event EventHandler<IReadOnlyList<(int Row, int Col)>>? Edited;

    public XdfTable Table { get; }

    public TableValues Values
    {
        get => _values;
        private set
        {
            if (ReferenceEquals(_values, value))
            {
                return;
            }

            // The range is ready before Values is announced: listeners to Values read both.
            _colourRange = MapColourRange.Compute(value);
            SetField(ref _values, value);
            OnPropertyChanged(nameof(ColourRange));
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    public CellRange? Selection
    {
        get => _selection;
        set
        {
            if (!SetField(ref _selection, value))
            {
                return;
            }

            // The active cell stays put while it is inside the new selection; the grid moves it within a range.
            if (value is CellRange range && !range.Contains(_active.Row, _active.Col))
            {
                ActiveCell = (range.Row0, range.Col0);
            }

            OnPropertyChanged(nameof(SelectionText));
            OnPropertyChanged(nameof(CanInterpolate));
            OnPropertyChanged(nameof(InterpolateTip));
            OnPropertyChanged(nameof(TypingHint));
        }
    }

    /// <summary>The cell the inspector describes: inside the selection, its first cell unless the grid puts it elsewhere.</summary>
    public (int Row, int Col) ActiveCell { get => _active; set => SetField(ref _active, value); }

    public bool ShowRaw { get => _showRaw; set => SetField(ref _showRaw, value); }

    public string? Message { get => _message; set => SetField(ref _message, value); }

    public IReadOnlySet<(int Row, int Col)> ChangedCells
    {
        get => _changedCells;
        private set
        {
            if (SetField(ref _changedCells, value))
            {
                OnPropertyChanged(nameof(ChangedText));
            }
        }
    }

    /// <summary>The engine's position on this table while the logger runs; null when there is none to show.</summary>
    public LiveCursorState? Live { get => _live; set => SetField(ref _live, value); }

    /// <summary>The value's units; a one-column table of this XDF keeps them on its column axis when Z has none.</summary>
    public string Units => Table.Z.Units.Length > 0 ? Table.Z.Units : Values.Cols == 1 ? Values.X.Units : string.Empty;

    /// <summary>
    /// The key line's selection (system 8): "4 rows × 3 columns: 3750 to 4500 rpm, 130.0 to 200.0 mg/stk", or for one
    /// cell "4000 rpm × 165.0 mg/stk: 33.4 °CRK, raw 152 (0x98)".
    /// </summary>
    public string SelectionText
    {
        get
        {
            if (Selection is not CellRange range)
            {
                return string.Empty;
            }

            if (range.Rows == 1 && range.Cols == 1)
            {
                var position = Position(range.Row0, range.Col0);
                return position.Length == 0 ? CellText(range.Row0, range.Col0) : $"{position}: {CellText(range.Row0, range.Col0)}";
            }

            var size = new List<string>();
            var spans = new List<string>();
            if (Values.Rows > 1)
            {
                size.Add(Count(range.Rows, "row"));
                spans.Add(NumberRows ? $"breakpoints {RowLabel(range.Row0)} to {RowLabel(range.Row1)}" : Span(Values.Y, range.Row0, range.Row1));
            }

            if (Values.Cols > 1)
            {
                size.Add(Count(range.Cols, "column"));
                spans.Add(Span(Values.X, range.Col0, range.Col1));
            }

            return $"{string.Join(" × ", size)}: {string.Join(", ", spans)}";
        }
    }

    /// <summary>"2 cells changed since the last save" (system 9.3: cells in the grid, items in the rail); null when none.</summary>
    public string? ChangedText => ChangedCells.Count switch
    {
        0 => null,
        1 => "1 cell changed since the last save",
        var count => $"{count} cells changed since the last save",
    };

    /// <summary>Interpolate fills between the corners of a range, so it needs more than one cell.</summary>
    public bool CanInterpolate => !IsReadOnly && Selection is CellRange range && (range.Rows > 1 || range.Cols > 1);

    public string InterpolateTip => CanInterpolate
        ? "Fill between the corners of the selection"
        : "Select a range first: Interpolate fills between its corners.";

    /// <summary>Where a cell sits on the table's axes: "4000 rpm × 165.0 mg/stk", "Breakpoint 3" on an axis; empty for a single value.</summary>
    public string Position(int row, int col)
    {
        var parts = new List<string>();
        if (Values.Rows > 1)
        {
            parts.Add(NumberRows ? $"Breakpoint {RowLabel(row)}" : WithUnits(Point(Values.Y, row), Values.Y.Units));
        }

        if (Values.Cols > 1)
        {
            parts.Add(WithUnits(Point(Values.X, col), Values.X.Units));
        }

        return string.Join(" × ", parts);
    }

    /// <summary>A cell's value as the grid writes it, then its raw value: "33.4 °CRK, raw 152 (0x98)".</summary>
    public string CellText(int row, int col)
    {
        var z = Table.Z;
        var raw = Values.Raw[row, col];
        var mask = z.Data.ElementBits >= 64 ? -1L : (1L << z.Data.ElementBits) - 1;
        var value = ValueCodec.Format(Values.Physical[row, col], raw, z.DecimalPlaces, z.OutputType, z.Data.ElementBits);
        return $"{WithUnits(value, Units)}, raw {raw.ToString(CultureInfo.InvariantCulture)} (0x{(raw & mask).ToString("X", CultureInfo.InvariantCulture)})";
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private static string Point(AxisValues axis, int index) =>
        index < axis.Labels.Count ? axis.Labels[index] : index.ToString(CultureInfo.InvariantCulture);

    private static string Span(AxisValues axis, int first, int last) =>
        WithUnits(first == last ? Point(axis, first) : $"{Point(axis, first)} to {Point(axis, last)}", axis.Units);

    private static string WithUnits(string text, string units) => units.Length > 0 && units != "-" ? $"{text} {units}" : text;

    /// <summary>Writes the edit through the history; a refused edit leaves the bin unchanged and explains why in <see cref="Message"/>.</summary>
    public bool Apply(EditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_document is not { } document || _history is not { } history)
        {
            Message = ReadOnlyMessage;
            return false;
        }

        if (Selection is not CellRange range)
        {
            Message = "Select cells first.";
            return false;
        }

        try
        {
            var plan = request.Kind switch
            {
                EditKind.Set => TableEdits.Set(document, Table, range, request.Value),
                EditKind.Add => TableEdits.Add(document, Table, range, request.Value),
                EditKind.Scale => TableEdits.Scale(document, Table, range, request.Value),
                EditKind.Interpolate => TableEdits.Interpolate(document, Table, range),
                EditKind.Smooth => TableEdits.Smooth(document, Table, range),
                EditKind.Step => TableEdits.Step(document, Table, range, (int)request.Value),
                EditKind.Paste => TableEdits.Paste(document, Table, range.Row0, range.Col0, TableClipboard.Parse(request.Text ?? string.Empty)),
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };

            history.Apply(plan, Label(request, plan));
            Message = plan.Warnings.Count > 0 ? string.Join(" ", plan.Warnings) : plan.Changes.Count == 0 ? "Nothing changed: the cells already hold those values." : null;
            if (plan.Changes.Count > 0)
            {
                Edited?.Invoke(this, plan.Changes.Select(change => (change.Row, change.Col)).ToList());
            }

            return plan.Changes.Count > 0;
        }
        catch (Exception error) when (error is EditRefusedException or FormatException)
        {
            Message = error.Message;
            return false;
        }
    }

    public string Copy() => Selection is CellRange range ? TableClipboard.Format(Values, range) : string.Empty;

    public void Dispose()
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Values = _document!.Reader.ReadTable(Table);
        ChangedCells = FindChangedCells();
    }

    private IReadOnlySet<(int Row, int Col)> FindChangedCells()
    {
        var data = Table.Z.Data;
        var changed = new HashSet<(int, int)>();
        if (_document is null || !_document.Saved.Contains(data))
        {
            return changed;
        }

        for (var r = 0; r < data.Rows; r++)
        {
            for (var c = 0; c < data.Cols; c++)
            {
                if (_document.Saved.ReadElement(data, r * data.Cols + c) != Values.Raw[r, c])
                {
                    changed.Add((r, c));
                }
            }
        }

        return changed;
    }

    private string Label(EditRequest request, EditPlan plan)
    {
        var cells = $"{plan.Changes.Count} {(plan.Changes.Count == 1 ? "cell" : "cells")} of {Table.Title}";
        var value = request.Value.ToString("0.###", CultureInfo.InvariantCulture);
        return request.Kind switch
        {
            EditKind.Set => $"Set {cells} to {value} {Units}".TrimEnd(),
            EditKind.Add => $"Add {value} {Units} to {cells}",
            EditKind.Scale => $"Scale {cells} by {value} %",
            EditKind.Step => $"Step {cells} by {value} raw",
            _ => $"{request.Kind} {cells}",
        };
    }
}
