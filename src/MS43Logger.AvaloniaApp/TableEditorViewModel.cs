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

/// <summary>One open table (map, curve or axis): its values, the selection, and edits through the undo history.</summary>
public sealed class TableEditorViewModel : ViewModelBase, IDisposable
{
    private readonly CalibrationDocument _document;
    private readonly EditHistory _history;
    private TableValues _values;
    private CellRange? _selection;
    private bool _showRaw;
    private string? _message;
    private LiveCursorState? _live;
    private IReadOnlySet<(int Row, int Col)> _changedCells = new HashSet<(int, int)>();

    public TableEditorViewModel(CalibrationDocument document, EditHistory history, XdfTable table)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _values = document.Reader.ReadTable(table);
        _changedCells = FindChangedCells();
        _document.Changed += OnDocumentChanged;
    }

    /// <summary>Raised after an edit was written, with the cells it touched.</summary>
    public event EventHandler<IReadOnlyList<(int Row, int Col)>>? Edited;

    public XdfTable Table { get; }

    public TableValues Values { get => _values; private set => SetField(ref _values, value); }

    public CellRange? Selection { get => _selection; set => SetField(ref _selection, value); }

    public bool ShowRaw { get => _showRaw; set => SetField(ref _showRaw, value); }

    public string? Message { get => _message; set => SetField(ref _message, value); }

    public IReadOnlySet<(int Row, int Col)> ChangedCells { get => _changedCells; private set => SetField(ref _changedCells, value); }

    /// <summary>The engine's position on this table while the logger runs; null when there is none to show.</summary>
    public LiveCursorState? Live { get => _live; set => SetField(ref _live, value); }

    public string Units => Table.Z.Units;

    /// <summary>Writes the edit through the history; a refused edit leaves the bin unchanged and explains why in <see cref="Message"/>.</summary>
    public bool Apply(EditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Selection is not CellRange range)
        {
            Message = "Select cells first.";
            return false;
        }

        try
        {
            var plan = request.Kind switch
            {
                EditKind.Set => TableEdits.Set(_document, Table, range, request.Value),
                EditKind.Add => TableEdits.Add(_document, Table, range, request.Value),
                EditKind.Scale => TableEdits.Scale(_document, Table, range, request.Value),
                EditKind.Interpolate => TableEdits.Interpolate(_document, Table, range),
                EditKind.Smooth => TableEdits.Smooth(_document, Table, range),
                EditKind.Step => TableEdits.Step(_document, Table, range, (int)request.Value),
                EditKind.Paste => TableEdits.Paste(_document, Table, range.Row0, range.Col0, TableClipboard.Parse(request.Text ?? string.Empty)),
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };

            _history.Apply(plan, Label(request, plan));
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

    public void Dispose() => _document.Changed -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Values = _document.Reader.ReadTable(Table);
        ChangedCells = FindChangedCells();
    }

    private IReadOnlySet<(int Row, int Col)> FindChangedCells()
    {
        var data = Table.Z.Data;
        var changed = new HashSet<(int, int)>();
        if (!_document.Saved.Contains(data))
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
