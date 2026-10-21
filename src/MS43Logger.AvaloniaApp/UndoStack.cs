using System;
using System.Collections.Generic;

namespace MS43Logger.AvaloniaApp;

/// <summary>One step Ctrl Z can take back and Ctrl Y can do again.</summary>
public sealed record UndoStep(string Label, Action Undo, Action Redo);

/// <summary>
/// One screen's undo history, in the order things were done: calibration edits (each step calls the document's
/// EditHistory) and app actions such as pinning. Tune and Compare share one; Live has its own, so Ctrl Z never changes
/// something on a screen that is not in front.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoLabel => _undo.TryPeek(out var step) ? step.Label : null;

    public string? NextRedoLabel => _redo.TryPeek(out var step) ? step.Label : null;

    /// <summary>True while a step is being undone or redone, so the action it replays does not push itself again.</summary>
    public bool IsReplaying { get; private set; }

    /// <summary>Records an action that has just been done; anything that could be redone is dropped.</summary>
    public void Push(UndoStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _undo.Push(step);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo() => Move(_undo, _redo, step => step.Undo());

    public bool Redo() => Move(_redo, _undo, step => step.Redo());

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Move(Stack<UndoStep> from, Stack<UndoStep> to, Action<UndoStep> replay)
    {
        if (!from.TryPop(out var step))
        {
            return false;
        }

        IsReplaying = true;
        try
        {
            replay(step);
        }
        finally
        {
            IsReplaying = false;
        }

        to.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
