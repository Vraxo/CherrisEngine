using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// The session's undo and redo stacks.
/// </summary>
/// <remarks>
/// Written on the render thread only, but read by the UI thread to enable Undo and Redo, so the counts are
/// guarded. A command that is not <see cref="IUndoableCommand"/> is simply never recorded.
/// </remarks>
public sealed class EditorHistory
{
    private const int MaximumDepth = 200;

    private readonly object _gate = new();
    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly LinkedList<IUndoableCommand> _redo = new();

    /// <summary>True when there is something to take back.</summary>
    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count > 0;
            }
        }
    }

    /// <summary>True when something taken back can be re-applied.</summary>
    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _redo.Count > 0;
            }
        }
    }

    /// <summary>What Undo would take back, for the menu's label, or null.</summary>
    public string? UndoLabel
    {
        get
        {
            lock (_gate)
            {
                return _undo.Last?.Value.Describe;
            }
        }
    }

    /// <summary>What Redo would re-apply, for the menu's label, or null.</summary>
    public string? RedoLabel
    {
        get
        {
            lock (_gate)
            {
                return _redo.Last?.Value.Describe;
            }
        }
    }

    /// <summary>
    /// Drops both stacks. A scene switch does this: the commands name objects of the scene that has just closed, so
    /// undoing one of them in another scene would do nothing at best.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
        }
    }

    /// <summary>Records a change that has just been made; a new change ends the redo branch.</summary>
    /// <param name="command">The command that made it.</param>
    public void Record(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            _undo.AddLast(command);
            _redo.Clear();

            while (_undo.Count > MaximumDepth)
            {
                _undo.RemoveFirst();
            }
        }
    }

    /// <summary>Takes the newest change off the undo stack and puts it on the redo stack.</summary>
    public IUndoableCommand? TakeUndo()
    {
        lock (_gate)
        {
            if (_undo.Last is not { } last)
            {
                return null;
            }

            _undo.RemoveLast();
            _redo.AddLast(last);

            return last.Value;
        }
    }

    /// <summary>Takes the newest undone change back off the redo stack.</summary>
    public IUndoableCommand? TakeRedo()
    {
        lock (_gate)
        {
            if (_redo.Last is not { } last)
            {
                return null;
            }

            _redo.RemoveLast();
            _undo.AddLast(last);

            return last.Value;
        }
    }
}
