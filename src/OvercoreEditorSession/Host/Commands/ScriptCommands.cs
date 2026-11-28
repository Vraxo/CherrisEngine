using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Adds a lua script to an object — what the inspector's Add Component menu sends when the typed name matches no
/// component, the way Unity offers to make a script.
/// </summary>
/// <param name="ObjectId">The object to add the script to.</param>
/// <param name="ScriptName">The name the user typed; a <c>.lua</c> extension and surrounding space are forgiven.</param>
/// <remarks>
/// The file is written once and then left alone: undoing removes the component, not the script, so work done in
/// an external editor survives. Attaching a name whose file already exists reuses that file.
/// </remarks>
public sealed class AddScriptCommand(Guid objectId, string scriptName) : IUndoableCommand
{
    private LanguageScript? _added;
    private string _path = string.Empty;

    /// <inheritdoc />
    public string Describe => $"add script {scriptName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return $"[editor] could not {Describe}: that object is no longer in the scene.";
        }

        if (ScriptTemplate.Normalize(scriptName) is { } name)
        {
            string candidate = ScriptTemplate.RelativePath(name, ScriptKind.Lua);

            if (target.Components.OfType<LanguageScript>().Any(script =>
                    String.Equals(script.SourcePath, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return $"[editor] {target.Name} already has the script {candidate}.";
            }
        }

        if (!ScriptTemplate.TryCreate(context.ProjectRoot, scriptName, ScriptKind.Lua, out string path, out string? error))
        {
            return $"[editor] could not {Describe}: {error}";
        }

        _path = path;
        _added = new LanguageScript { Language = ScriptTemplate.LuaLanguage, SourcePath = _path };
        target.AddComponent(_added);

        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] added script {_path} to {target.Name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.RemoveComponent(_added);
        context.Scene.IsDirty = true;

        // The file stays: it is the user's, and another object may use it.
        return $"[editor] removed script {_path} from {target.Name}; the file is still there.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.AddComponent(_added);
        context.Scene.IsDirty = true;

        return $"[editor] added script {_path} back to {target.Name}.";
    }

    private GameObject? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}

/// <summary>
/// Adds a C# script to an object, creating the file when it is not there and compiling the project before anything
/// is attached — the other half of the inspector's Add Component menu, where Unity offers a C# script.
/// </summary>
/// <param name="ObjectId">The object to add the script to.</param>
/// <param name="ScriptName">The name the user typed; a <c>.cs</c> extension and surrounding space are forgiven.</param>
/// <remarks>
/// <para>
/// The order is the whole point. A C# script is a component only once it has been compiled: the session registers
/// a factory for its class name and puts it in the catalog, and until then <see cref="AddComponentCommand"/> has
/// nothing to resolve the name against. So this writes the file, asks for a compile, and only then attaches.
/// </para>
/// <para>
/// The attach records itself in the history, so Undo takes the component off. The file stays, as it does for a Lua
/// script: it is the user's, another object may use it, and deleting a file behind an undo is not something a click
/// in a menu should be able to do.
/// </para>
/// </remarks>
public sealed class AddCSharpScriptCommand(Guid objectId, string scriptName) : IEditorCommand
{
    private AddComponentCommand? _attach;

    /// <inheritdoc />
    public string Describe => $"add C# script {scriptName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ScriptTemplate.TryCreate(
                context.ProjectRoot, scriptName, ScriptKind.CSharp, out string path, out string? error))
        {
            return $"[editor] could not {Describe}: {error}";
        }

        if (context.ReloadScripts is not { } reload)
        {
            return $"[editor] wrote {path}, but this session has no script compiler; reopen the project to attach it.";
        }

        // Compile first: the name is resolved against what the session knows, and it does not know this type until
        // the project has been compiled with the file in it.
        context.ScriptComponents = reload() ?? [];

        // The entry is matched on the type name, not the menu name: the menu says "Player Controller" for a class
        // called PlayerController, and it is the class name the file and the scene use.
        string className = ScriptTemplate.Normalize(scriptName) ?? scriptName;

        ComponentCatalog.Entry? entry = context.ScriptComponents.FirstOrDefault(candidate =>
            String.Equals(candidate.ComponentType.Name, className, StringComparison.Ordinal));

        if (entry is null)
        {
            return $"[editor] wrote {path}, but it declares no class named '{className}', so there is nothing to " +
                "attach; name the class after its file and try again.";
        }

        _attach = new AddComponentCommand(objectId, entry.DisplayName);

        return _attach.Execute(context);
    }

    /// <inheritdoc />
    /// <remarks>Nothing to reverse here: the attach recorded itself, and the file is deliberately left alone.</remarks>
    public string? Undo(EditorCommandContext context) => null;

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context) => null;
}
