using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>What a command runs against: the scene, the history it may record itself in, and the project.</summary>
/// <param name="scene">The active scene, owned by the render thread.</param>
/// <param name="history">The undo stack for this session.</param>
/// <param name="projectRoot">
/// Absolute folder of the open project, or null in the built-in demo scene. A command that writes a file —
/// a new script — needs it; nothing else does.
/// </param>
/// <param name="scriptComponents">The project's compiled C# scripts, as menu entries, so a command quoted one of
/// their names can add it.</param>
/// <param name="reloadScripts">Recompiles the project's C# scripts on the render thread and returns the refreshed
/// entries, or null when no session is compiling them. Only a command that has just written a script needs it: a
/// scene may only name a type the session knows, so the file has to be compiled before anything is attached.</param>
public sealed class EditorCommandContext(
    Scene scene,
    EditorHistory history,
    string? projectRoot = null,
    IReadOnlyList<ComponentCatalog.Entry>? scriptComponents = null,
    Func<IReadOnlyList<ComponentCatalog.Entry>?>? reloadScripts = null,
    Func<string, List<GameObject>>? loadPrefabAsset = null)
{
    /// <summary>The active scene.</summary>
    public Scene Scene { get; } = scene ?? throw new ArgumentNullException(nameof(scene));

    /// <summary>The session's history.</summary>
    public EditorHistory History { get; } = history ?? throw new ArgumentNullException(nameof(history));

    /// <summary>The open project's folder, or null when there is no project to write to.</summary>
    public string? ProjectRoot { get; } = projectRoot;

    /// <summary>
    /// The open project's compiled C# scripts, as menu entries. They cannot be in
    /// <see cref="ComponentCatalog.BuiltIn"/> — a project's scripts only exist once that project has been compiled —
    /// but a command that adds one treats them as any other component.
    /// </summary>
    public IReadOnlyList<ComponentCatalog.Entry> ScriptComponents { get; internal set; } = scriptComponents ?? [];

    /// <summary>
    /// Compiles the project's C# scripts again and returns the entries they make now, or null when this session has
    /// no compiler. The render thread is the only caller: it is the thread that owns the scene the new types are
    /// registered for.
    /// </summary>
    public Func<IReadOnlyList<ComponentCatalog.Entry>?>? ReloadScripts { get; } = reloadScripts;

    /// <summary>Reads a prefab asset into fresh objects, or null when this session cannot read prefabs.</summary>
    public Func<string, List<GameObject>>? LoadPrefabAsset { get; } = loadPrefabAsset;
}
