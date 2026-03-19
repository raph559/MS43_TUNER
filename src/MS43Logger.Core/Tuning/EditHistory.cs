namespace MS43Logger.Core.Tuning;

/// <summary>Applies edit plans to a document, one undo step per plan.</summary>
public sealed class EditHistory
{
    private readonly CalibrationDocument _document;
    private readonly Stack<Step> _undo = new();
    private readonly Stack<Step> _redo = new();

    public EditHistory(CalibrationDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public string? NextUndoLabel => _undo.TryPeek(out var step) ? step.Label : null;

    /// <summary>Writes the plan; a plan without changes adds no step.</summary>
    public void Apply(EditPlan plan, string label)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Changes.Count == 0)
        {
            return;
        }

        var data = DataOf(plan.Item);
        var cols = plan.Item is XdfTable table ? table.Z.Data.Cols : 1;
        var writes = plan.Changes.Select(change => new Write(data, change.Row * cols + change.Col, change.OldRaw, change.NewRaw)).ToList();
        var step = new Step(label, writes);
        Run(step, forward: true);
        _undo.Push(step);
        _redo.Clear();
        Notify();
    }

    public bool Undo() => Move(_undo, _redo, forward: false);

    public bool Redo() => Move(_redo, _undo, forward: true);

    private bool Move(Stack<Step> from, Stack<Step> to, bool forward)
    {
        if (!from.TryPop(out var step))
        {
            return false;
        }

        Run(step, forward);
        to.Push(step);
        Notify();
        return true;
    }

    private void Run(Step step, bool forward)
    {
        var writes = forward ? step.Writes : step.Writes.AsEnumerable().Reverse();
        foreach (var write in writes)
        {
            _document.Image.WriteElement(write.Data, write.Index, forward ? write.New : write.Old);
        }
    }

    private void Notify()
    {
        _document.RaiseChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static XdfData DataOf(XdfItem item) => item switch
    {
        XdfTable table => table.Z.Data,
        XdfConstant constant => constant.Data,
        XdfFlag flag => flag.Data,
        _ => throw new ArgumentException($"Unknown item type {item.GetType().Name}.", nameof(item)),
    };

    private sealed record Write(XdfData Data, int Index, long Old, long New);

    private sealed record Step(string Label, IReadOnlyList<Write> Writes);
}
