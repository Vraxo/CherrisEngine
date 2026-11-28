using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// The queue and the history together: what a host owns so its engine can apply edits and take them back.
/// </summary>
/// <remarks>
/// Both halves are per session, because a history that outlived the scene it describes would undo into the
/// wrong one.
/// </remarks>
public sealed class EditorCommandDispatcher(string? projectRoot = null)
{
    private readonly EditorCommandQueue _queue = new();

    /// <summary>The open project's folder, or null in the built-in demo scene.</summary>
    public string? ProjectRoot { get; } = projectRoot;

    /// <summary>How many edits are waiting for the render thread.</summary>
    public int Pending => _queue.Count;

    /// <summary>The session's undo and redo stacks.</summary>
    public EditorHistory History { get; } = new();

    /// <summary>
    /// The project's compiled C# scripts, set when the session compiles them. The built-in catalog knows nothing
    /// about them; a command that names one resolves it here.
    /// </summary>
    public IReadOnlyList<ComponentCatalog.Entry> ScriptComponents { get; set; } = [];

    /// <summary>
    /// Recompiles the project's C# scripts, set by the session that owns the compiler. A command that writes a
    /// script calls it so the type it just created can be attached in the same drain.
    /// </summary>
    public Func<IReadOnlyList<ComponentCatalog.Entry>?>? ReloadScripts { get; set; }

    /// <summary>
    /// Reads a prefab asset into fresh objects, set by the session that owns the engine's loader. A command
    /// that instantiates a prefab reads the asset through it, so the factory-built components come back whole.
    /// </summary>
    public Func<string, List<GameObject>>? LoadPrefabAsset { get; set; }

    /// <summary>Queues an edit. Safe from any thread.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(IEditorCommand command) => _queue.Enqueue(command);

    /// <summary>Runs the waiting edits against the scene, oldest first. Call from the thread that owns it.</summary>
    /// <param name="scene">The active scene.</param>
    /// <param name="log">Receives the lines the commands return, and any failure.</param>
    public int Drain(Scene scene, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        EditorCommandContext context = new(scene, History, ProjectRoot, ScriptComponents, ReloadScripts, LoadPrefabAsset);

        return _queue.Drain(command => command.Execute(context), log);
    }
}
