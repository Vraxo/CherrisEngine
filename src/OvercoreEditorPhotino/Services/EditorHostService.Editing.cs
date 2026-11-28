using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Projects;
using Overcore.Scripting;
using Overcore.Utils;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OvercoreEditorPhotino.Services;

// Undo/redo state and every edit the panels can make to the live scene: each method builds the command the
// render thread applies, never touching the scene itself.
public sealed partial class EditorHostService : IDisposable
{
    /// <summary>True when there is an edit to take back.</summary>
    public bool CanUndo => _session?.History.CanUndo ?? false;

    /// <summary>True when there is an edit to re-apply.</summary>
    public bool CanRedo => _session?.History.CanRedo ?? false;

    /// <summary>What Undo would take back, for the menu's label, or null.</summary>
    public string? UndoLabel => _session?.History.UndoLabel;

    /// <summary>What Redo would re-apply, for the menu's label, or null.</summary>
    public string? RedoLabel => _session?.History.RedoLabel;

    /// <summary>
    /// Adds a component to an object in the live scene. The render thread applies it on its next frame, and
    /// the snapshot the panels read picks it up from there; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to add to.</param>
    /// <param name="componentName">One of <see cref="ComponentTypes"/>' display names.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddComponent(Guid objectId, string componentName) =>
        Enqueue(new AddComponentCommand(objectId, componentName), "add " + componentName);

    /// <summary>
    /// Adds an empty object at the scene root, as the hierarchy's create menu sends: Unity's "Create Empty".
    /// The render thread applies it on its next frame, and the snapshot the panels read picks it up from there;
    /// the outcome is logged to the console.
    /// </summary>
    /// <param name="name">The name the object is born with.</param>
    /// <param name="objectId">
    /// Set to the id the object is created with, so the caller can select it before the snapshot catches up.
    /// </param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool CreateGameObject(string name, out Guid objectId)
    {
        objectId = Guid.NewGuid();
        return Enqueue(new CreateGameObjectCommand(objectId, name), "create " + name);
    }

    /// <summary>
    /// Takes an object and everything under it out of the scene, as the hierarchy's context menu sends:
    /// Unity's "Delete". The subtree comes back whole on Undo. The render thread applies it on its next
    /// frame, and the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to take out.</param>
    /// <param name="name">Its name, so the step can be named.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool DeleteGameObject(Guid objectId, string name) =>
        Enqueue(new DeleteGameObjectCommand(objectId, name), "delete " + name);

    /// <summary>
    /// Saves an object and its children as a prefab asset in the project folder and links the saved objects
    /// to it, as the hierarchy's context menu sends: Unity's "Create Prefab". One undoable step; the outcome
    /// is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to save; its subtree becomes the asset.</param>
    /// <param name="objectName">Its name, which names the file.</param>
    /// <returns>False when no session is running, or there is no project to write to.</returns>
    public bool CreatePrefab(Guid objectId, string objectName)
    {
        if (_projectRoot is not { } projectRoot)
        {
            return false;
        }

        return Enqueue(
            new CreatePrefabFromSelectionCommand(objectId, UniqueAssetPath(projectRoot, objectName)),
            "create prefab");
    }

    /// <summary>
    /// Adds a prefab to the scene as a fresh instance of the asset, as the content browser sends: Unity's
    /// "Instantiate Prefab". One undoable step; the outcome is logged to the console.
    /// </summary>
    /// <param name="path">The prefab asset's path.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool InstantiatePrefab(string path) =>
        Enqueue(new InstantiatePrefabCommand(path), "instantiate " + Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// Puts one drifted value of a prefab instance back to what the asset holds, as the inspector's
    /// Overrides list sends: Unity's "Revert". One undoable step; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The linked object.</param>
    /// <param name="propertyPath">What to put back.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool RevertOverride(Guid objectId, string propertyPath) =>
        Enqueue(new RevertPrefabOverrideCommand(objectId, propertyPath), "revert " + propertyPath);

    /// <summary>
    /// Puts every drifted value of an object and its subtree back to what the assets hold: Unity's
    /// "Revert All". One undoable step; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object whose subtree is put back.</param>
    /// <param name="name">Its name, so the step can be named.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool RevertAllOverrides(Guid objectId, string name) =>
        Enqueue(new RevertAllPrefabOverridesCommand(objectId, name), "revert overrides on " + name);

    /// <summary>
    /// Writes one drifted value into the prefab asset: Unity's "Apply". The asset moves to the instance —
    /// every other instance of it now differs. One undoable step; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The linked object.</param>
    /// <param name="propertyPath">What to write into the asset.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool ApplyOverride(Guid objectId, string propertyPath) =>
        Enqueue(new ApplyPrefabOverrideCommand(objectId, propertyPath), "apply " + propertyPath);

    /// <summary>
    /// Writes every drifted value of an object and its subtree into the prefab assets: Unity's "Apply All".
    /// One undoable step; Undo puts the asset files back byte for byte. The outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object whose subtree is applied.</param>
    /// <param name="name">Its name, so the step can be named.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool ApplyAllOverrides(Guid objectId, string name) =>
        Enqueue(new ApplyAllPrefabOverridesCommand(objectId, name), "apply overrides on " + name);

    /// <summary>
    /// Switches a component on or off, as the inspector's header checkbox sends. Off stops the component — a
    /// renderer stops drawing, a light stops lighting, a script stops ticking — without taking it off the object,
    /// and the state is saved with the scene. One click is one undo step.
    /// </summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="enabled">Where the checkbox goes.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetComponentEnabled(Guid objectId, string componentTypeName, bool enabled, Action<long>? completed = null)
    {
        // A drag that is still running ends here, as it does before any other discrete edit.
        EndLiveEdit();

        EditorSessionHost? host = _session;
        if (host is null)
        {
            WriteLog($"[editor] cannot {(enabled ? "enable" : "disable")} {componentTypeName}: no session is running.", LogLevel.Warning);
            return false;
        }

        // This runs before the frame's snapshot is published. The UI can ignore older snapshots until it
        // sees one newer than this frame, including when the command was refused or threw.
        IEditorCommand command = WrapForCompletion(
            new SetComponentEnabledCommand(objectId, componentTypeName, enabled), host, completed);

        host.Enqueue(command);
        return true;
    }

    /// <summary>
    /// Writes one property of a component while the author drags it. Consecutive calls for the same property
    /// feed a single command, so the scene follows the pointer every frame while the whole drag stays one undo
    /// step. The panel calls <see cref="EndLiveEdit"/> when a drag begins, a keystroke or a menu edit arrives, so
    /// neither two drags nor a drag and the value typed after it are ever the same step.
    /// </summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="text">The value the pointer is on, in the inspector's text format.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetPropertyLive(Guid objectId, string componentTypeName, string propertyName, string text, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog("[editor] cannot set a property: no session is running.", LogLevel.Warning);
            return false;
        }

        string key = $"property:{objectId:N}:{componentTypeName}.{propertyName}";

        if (_liveCommand is SetPropertyCommand live && String.Equals(_liveKey, key, StringComparison.Ordinal))
        {
            live.Text = text;
            host.Enqueue(WrapForCompletion(live, host, completed));
            return true;
        }

        SetPropertyCommand command = new(objectId, componentTypeName, propertyName, text);

        _liveKey = key;
        _liveCommand = command;
        host.Enqueue(WrapForCompletion(command, host, completed));

        return true;
    }

    /// <summary>
    /// Writes one transform row while the author drags it, feeding a single command the way
    /// <see cref="SetPropertyLive"/> does for a property.
    /// </summary>
    /// <param name="objectId">The object.</param>
    /// <param name="transformField">Position, rotation (Euler degrees) or scale.</param>
    /// <param name="x">The row's first component, which is the one the pointer is usually on.</param>
    /// <param name="y">The row's second component.</param>
    /// <param name="z">The row's third component.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetTransformLive(Guid objectId, TransformField transformField, float x, float y, float z, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog("[editor] cannot move the object: no session is running.", LogLevel.Warning);
            return false;
        }

        System.Numerics.Vector3 value = new(x, y, z);
        string key = $"transform:{objectId:N}:{transformField}";

        if (_liveCommand is SetTransformCommand live && String.Equals(_liveKey, key, StringComparison.Ordinal))
        {
            live.Value = value;
            host.Enqueue(WrapForCompletion(live, host, completed));
            return true;
        }

        SetTransformCommand command = new(objectId, transformField, value);

        _liveKey = key;
        _liveCommand = command;
        host.Enqueue(WrapForCompletion(command, host, completed));

        return true;
    }

    /// <summary>
    /// Ends the drag: the value the pointer is on is already queued, so this only closes the session, and the
    /// next edit of that property records a new step instead of joining the drag that just finished.
    /// </summary>
    public void EndLiveEdit()
    {
        _liveKey = null;
        _liveCommand = null;
    }

    /// <summary>Writes one property of a component from the inspector's text.</summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="text">The new value, in the inspector's text format.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetProperty(Guid objectId, string componentTypeName, string propertyName, string text, Action<long>? completed = null)
    {
        // A field that is no longer being dragged starts its own step.
        EndLiveEdit();

        return Enqueue(
            new SetPropertyCommand(objectId, componentTypeName, propertyName, text),
            $"set {componentTypeName}.{propertyName}",
            completed);
    }

    /// <summary>
    /// Adds a lua script to an object, creating the file in the project's <c>Scripts/</c> folder when it is not
    /// there yet. The render thread does the writing, so the UI thread never touches the project folder.
    /// </summary>
    /// <param name="objectId">The object to add the script to.</param>
    /// <param name="scriptName">The name the author typed; a <c>.lua</c> extension is forgiven.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddScript(Guid objectId, string scriptName) =>
        Enqueue(new AddScriptCommand(objectId, scriptName), "add script " + scriptName);

    /// <summary>
    /// Adds a C# script to an object: the file is written, the project is compiled and the component is attached, in
    /// that order and on the render thread, because a scene may only name a type the session has compiled.
    /// </summary>
    /// <param name="objectId">The object to add the script to.</param>
    /// <param name="scriptName">The name the author typed; a <c>.cs</c> extension is forgiven.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddCSharpScript(Guid objectId, string scriptName) =>
        Enqueue(new AddCSharpScriptCommand(objectId, scriptName), "add C# script " + scriptName);

    /// <summary>True when the open project already holds a script file with this name, in this language.</summary>
    /// <param name="scriptName">The name the author typed.</param>
    /// <param name="kind">Which language's file is looked for.</param>
    public bool ScriptExists(string? scriptName, ScriptKind kind) =>
        ScriptTemplate.Exists(_projectRoot, scriptName, kind);

    /// <summary>
    /// Compiles the project's <c>Scripts/</c> folder again, inside the running session, so a script edited in another
    /// window is what Play runs and a type it just declared is in the Add Component menu.
    /// </summary>
    /// <remarks>
    /// The compile is queued rather than done here: the scene, its factories and the load context belong to the
    /// render thread. What came of it is reported the way every compile is — the count on success, one line per
    /// error — because the console is the only place a script error can be shown.
    /// </remarks>
    /// <returns>False when no session is running to compile them.</returns>
    public bool ReloadScripts()
    {
        if (_session?.RequestScriptReload() != true)
        {
            return false;
        }

        WriteLog("[editor] recompiling the project's C# scripts");

        return true;
    }

    /// <summary>Writes one transform row of an object.</summary>
    /// <param name="objectId">The object.</param>
    /// <param name="transformField">Position, rotation (Euler degrees) or scale.</param>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetTransform(Guid objectId, TransformField transformField, float x, float y, float z, Action<long>? completed = null)
    {
        EndLiveEdit();

        return Enqueue(
            new SetTransformCommand(objectId, transformField, new System.Numerics.Vector3(x, y, z)),
            "move the object",
            completed);
    }

    /// <summary>Renames an object.</summary>
    /// <param name="objectId">The object.</param>
    /// <param name="name">The new name.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool RenameObject(Guid objectId, string name) =>
        Enqueue(new RenameObjectCommand(objectId, name), "rename the object");

    /// <summary>Takes the newest edit back.</summary>
    /// <returns>False when there is nothing to undo, or no session.</returns>
    public bool Undo()
    {
        EndLiveEdit();

        if (!CanUndo)
        {
            return false;
        }

        return Enqueue(new UndoCommand(), "undo");
    }

    /// <summary>Re-applies the newest undone edit.</summary>
    /// <returns>False when there is nothing to redo, or no session.</returns>
    public bool Redo()
    {
        EndLiveEdit();

        if (!CanRedo)
        {
            return false;
        }

        return Enqueue(new RedoCommand(), "redo");
    }

    /// <summary>Hands an edit to the render thread, or says why it could not.</summary>
    private bool Enqueue(OvercoreEditorSession.Host.IEditorCommand command, string action, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog($"[editor] cannot {action}: no session is running.", LogLevel.Warning);
            return false;
        }

        host.Enqueue(WrapForCompletion(command, host, completed));
        return true;
    }

    /// <summary>
    /// Wraps a command so the caller learns the last snapshot frame before it ran, letting the UI ignore older
    /// snapshots until a newer one confirms the edit. Without a callback the command goes as it is.
    /// </summary>
    private static OvercoreEditorSession.Host.IEditorCommand WrapForCompletion(
        OvercoreEditorSession.Host.IEditorCommand command, EditorSessionHost host, Action<long>? completed)
    {
        return completed is null
            ? command
            : new CompletionCommand(command, () => completed(host.LatestScene?.Frame ?? -1));
    }
}
