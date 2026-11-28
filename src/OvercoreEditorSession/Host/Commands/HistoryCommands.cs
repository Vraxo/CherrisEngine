using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>Takes the newest change back.</summary>
public sealed class UndoCommand : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => "undo";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.History.TakeUndo() is { } command
            ? command.Undo(context)
            : "[editor] nothing to undo.";
    }
}

/// <summary>Re-applies the newest undone change.</summary>
public sealed class RedoCommand : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => "redo";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.History.TakeRedo() is { } command
            ? command.Redo(context)
            : "[editor] nothing to redo.";
    }
}
