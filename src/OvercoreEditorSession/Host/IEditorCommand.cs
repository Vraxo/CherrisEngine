using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// A change the editor UI asks the engine to make.
/// </summary>
/// <remarks>
/// The UI thread never touches the scene: <see cref="Execute"/> is called by the render thread, which owns
/// it. That is why the web editor can edit at all while its panels render from immutable snapshots.
/// </remarks>
public interface IEditorCommand
{
    /// <summary>One line naming the change, for logs and diagnostics.</summary>
    string Describe { get; }

    /// <summary>Applies the change to the scene the render thread owns.</summary>
    /// <param name="context">The scene, and the history an undoable command records itself in.</param>
    /// <returns>A line to log, or null when there is nothing worth saying.</returns>
    string? Execute(EditorCommandContext context);
}

/// <summary>A command that can be taken back.</summary>
/// <remarks>
/// <see cref="Execute"/> records the command in <see cref="EditorCommandContext.History"/> when it actually
/// changes something; <see cref="Undo"/> and <see cref="Redo"/> re-apply the values the command captured and
/// never record, so the stacks keep their meaning.
/// </remarks>
public interface IUndoableCommand : IEditorCommand
{
    /// <summary>Puts back what <see cref="IEditorCommand.Execute"/> replaced.</summary>
    /// <param name="context">The scene and history.</param>
    string? Undo(EditorCommandContext context);

    /// <summary>Re-applies the change after an <see cref="Undo"/>.</summary>
    /// <param name="context">The scene and history.</param>
    string? Redo(EditorCommandContext context);
}
