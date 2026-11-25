using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using Overcore.Utils;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Puts one drifted value back to what the prefab asset holds, as the inspector's Overrides list sends:
/// Unity's "Revert". One value is one undoable step.
/// </summary>
/// <param name="ObjectId">The linked object.</param>
/// <param name="PropertyPath">What to put back, as <see cref="PrefabPaths"/> names it.</param>
public sealed class RevertPrefabOverrideCommand(Guid objectId, string propertyPath) : IUndoableCommand
{
    private string? _before;
    private string? _after;

    /// <inheritdoc />
    public string Describe => $"revert {propertyPath}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (bool ok, GameObject? found, string? failure) = PrefabOverrideWork.Target(context, objectId, Describe);

        if (!ok || found is not { } target)
        {
            return failure;
        }

        if (PrefabOverrideWork.Diff(context, target)
                .FirstOrDefault(entry => String.Equals(entry.PropertyPath, propertyPath, StringComparison.Ordinal))
            is not { } overrideEntry)
        {
            return $"[editor] could not {Describe}: it is not overridden.";
        }

        string? before = PrefabValues.Read(target, propertyPath);
        string? error = null;

        if (before is null
            || !PrefabValues.Write(target, propertyPath, overrideEntry.SourceText, out error))
        {
            return $"[editor] could not {Describe}: {error ?? "no such value"}";
        }

        PrefabOverrideWork.AfterWrite(context, propertyPath);
        _before = before;
        _after = overrideEntry.SourceText;
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] reverted {propertyPath}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context) => Rewind(context, _before, "restored");

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context) => Rewind(context, _after, "reverted");

    private string? Rewind(EditorCommandContext context, string? value, string verb)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null
            || context.Scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId) is not { } target
            || !PrefabValues.Write(target, propertyPath, value, out _))
        {
            return null;
        }

        PrefabOverrideWork.AfterWrite(context, propertyPath);
        context.Scene.IsDirty = true;

        return $"[editor] {verb} {propertyPath} = {value}";
    }
}

/// <summary>
/// Puts every drifted value of an object and its subtree back to what the prefab assets hold, as the
/// inspector's "Revert All" sends. One undoable step, however many values move.
/// </summary>
/// <param name="ObjectId">The object whose subtree is put back.</param>
/// <param name="Name">Its name, so the step can be named before it runs.</param>
public sealed class RevertAllPrefabOverridesCommand(Guid objectId, string name) : IUndoableCommand
{
    private sealed record Change(GameObject GameObject, string Path, string Before, string After);

    private List<Change>? _changes;

    /// <inheritdoc />
    public string Describe => $"revert all overrides on {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (bool ok, GameObject? found, string? failure) = PrefabOverrideWork.Target(context, objectId, Describe);

        if (!ok || found is not { } target)
        {
            return failure;
        }

        _changes = [];

        foreach (GameObject go in PrefabOverrideWork.Subtree(target))
        {
            foreach (PrefabOverrideSnapshot entry in PrefabOverrideWork.Diff(context, go))
            {
                string? before = PrefabValues.Read(go, entry.PropertyPath);

                if (before is not null
                    && PrefabValues.Write(go, entry.PropertyPath, entry.SourceText, out _))
                {
                    _changes.Add(new Change(go, entry.PropertyPath, before, entry.SourceText));
                }
            }
        }

        if (_changes.Count == 0)
        {
            // Nothing drifted, so nothing happened — and nothing is put on the history.
            return null;
        }

        PrefabOverrideWork.AfterWrite(context, "*");
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] reverted {_changes.Count} override(s) on {name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context) => Rewind(context, change => change.Before, "restored");

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context) => Rewind(context, change => change.After, "reverted");

    private string? Rewind(EditorCommandContext context, Func<Change, string> value, string verb)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_changes is null)
        {
            return null;
        }

        foreach (Change change in _changes)
        {
            PrefabValues.Write(change.GameObject, change.Path, value(change), out _);
        }

        PrefabOverrideWork.AfterWrite(context, "*");
        context.Scene.IsDirty = true;

        return $"[editor] {verb} {_changes.Count} override(s) on {name}.";
    }
}

/// <summary>
/// Writes one drifted value into the prefab asset, as the inspector's Overrides list sends: Unity's
/// "Apply". The asset moves to the instance — every other instance of it now differs, which is what an
/// apply means.
/// </summary>
/// <param name="ObjectId">The linked object.</param>
/// <param name="PropertyPath">What to write into the asset, as <see cref="PrefabPaths"/> names it.</param>
public sealed class ApplyPrefabOverrideCommand(Guid objectId, string propertyPath) : IUndoableCommand
{
    private string? _assetPath;
    private byte[]? _before;
    private byte[]? _after;

    /// <inheritdoc />
    public string Describe => $"apply {propertyPath}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (bool ok, GameObject? found, string? failure) = PrefabOverrideWork.Target(context, objectId, Describe);

        if (!ok || found is not { } target)
        {
            return failure;
        }

        string? value = PrefabValues.Read(target, propertyPath);

        if (value is null)
        {
            return $"[editor] could not {Describe}: no such value.";
        }

        (string? assetPath, string? error) = PrefabOverrideWork.AssetPathFor(context, target);

        if (assetPath is null)
        {
            return $"[editor] could not {Describe}: {error}";
        }

        (List<GameObject>? roots, string? loadError) = PrefabOverrideWork.LoadAsset(context, assetPath);

        if (roots is null
            || PrefabOverrideWork.FindFileObject(roots, target.PrefabSource!.FileId) is not { } source)
        {
            return $"[editor] could not {Describe}: {loadError ?? "the source object is gone."}";
        }

        // The file's bytes are taken before the save, so Undo can put the asset back exactly.
        _before = File.ReadAllBytes(assetPath);

        if (!PrefabValues.Write(source, propertyPath, value, out string? writeError))
        {
            return $"[editor] could not {Describe}: {writeError}";
        }

        new SceneSerializer().SavePrefab(roots, assetPath);
        _after = File.ReadAllBytes(assetPath);
        _assetPath = assetPath;
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] applied {propertyPath} to {Path.GetFileName(assetPath)}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context) => Rewind(context, _before, "undid");

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context) => Rewind(context, _after, "applied");

    private string? Rewind(EditorCommandContext context, byte[]? bytes, string verb)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (bytes is null || _assetPath is null)
        {
            return null;
        }

        File.WriteAllBytes(_assetPath, bytes);
        context.Scene.IsDirty = true;

        return $"[editor] {verb} {propertyPath} in {Path.GetFileName(_assetPath)}.";
    }
}

/// <summary>
/// Writes every drifted value of an object and its subtree into the prefab assets, as the inspector's
/// "Apply All" sends. One undoable step; Undo puts the asset files back byte for byte.
/// </summary>
/// <param name="ObjectId">The object whose subtree is applied.</param>
/// <param name="Name">Its name, so the step can be named before it runs.</param>
public sealed class ApplyAllPrefabOverridesCommand(Guid objectId, string name) : IUndoableCommand
{
    /// <param name="AssetPath">The asset file that was rewritten.</param>
    /// <param name="Before">Its bytes before the apply.</param>
    /// <param name="After">Its bytes after.</param>
    private sealed record AssetWrite(string AssetPath, byte[] Before, byte[] After);

    private List<AssetWrite>? _writes;

    /// <inheritdoc />
    public string Describe => $"apply all overrides on {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (bool ok, GameObject? found, string? failure) = PrefabOverrideWork.Target(context, objectId, Describe);

        if (!ok || found is not { } target)
        {
            return failure;
        }

        List<(GameObject Linked, PrefabOverrideSnapshot Entry)> drifted = [];

        foreach (GameObject go in PrefabOverrideWork.Subtree(target))
        {
            foreach (PrefabOverrideSnapshot entry in PrefabOverrideWork.Diff(context, go))
            {
                drifted.Add((go, entry));
            }
        }

        if (drifted.Count == 0)
        {
            return null;
        }

        // One asset is loaded once and written once, so values that share an asset share one rewrite.
        Dictionary<string, List<GameObject>> rootsByAsset = [];
        Dictionary<string, byte[]> befores = [];
        int applied = 0;
        string? firstError = null;

        foreach ((GameObject linked, PrefabOverrideSnapshot entry) in drifted)
        {
            string? value = PrefabValues.Read(linked, entry.PropertyPath);

            if (value is null || linked.PrefabSource is not { } link)
            {
                continue;
            }

            (string? assetPath, string? error) = PrefabOverrideWork.AssetPathFor(context, linked);

            if (assetPath is null)
            {
                firstError ??= error;
                continue;
            }

            if (!rootsByAsset.TryGetValue(assetPath, out List<GameObject>? roots))
            {
                (List<GameObject>? loaded, string? loadError) = PrefabOverrideWork.LoadAsset(context, assetPath);

                if (loaded is null)
                {
                    firstError ??= loadError;
                    continue;
                }

                roots = loaded;
                rootsByAsset[assetPath] = roots;
                befores[assetPath] = File.ReadAllBytes(assetPath);
            }

            if (PrefabOverrideWork.FindFileObject(roots, link.FileId) is { } source
                && PrefabValues.Write(source, entry.PropertyPath, value, out _))
            {
                applied++;
            }
        }

        _writes = [];

        foreach ((string assetPath, List<GameObject> roots) in rootsByAsset)
        {
            new SceneSerializer().SavePrefab(roots, assetPath);
            _writes.Add(new AssetWrite(assetPath, befores[assetPath], File.ReadAllBytes(assetPath)));
        }

        if (_writes.Count == 0)
        {
            return $"[editor] could not {Describe}: {firstError ?? "nothing could be written"}";
        }

        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] applied {applied} override(s) on {name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context) => Rewind(context, write => write.Before, "undid");

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context) => Rewind(context, write => write.After, "applied");

    private string? Rewind(EditorCommandContext context, Func<AssetWrite, byte[]> bytes, string verb)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_writes is null)
        {
            return null;
        }

        foreach (AssetWrite write in _writes)
        {
            File.WriteAllBytes(write.AssetPath, bytes(write));
        }

        context.Scene.IsDirty = true;

        return $"[editor] {verb} {name}.";
    }
}

/// <summary>The plumbing the four override commands share: finding, diffing, and rewriting assets.</summary>
internal static class PrefabOverrideWork
{
    /// <summary>
    /// The object the step is about, checked: it exists and is linked to a prefab. Callers proceed only
    /// when ok is true; failure carries the line to log when it is not.
    /// </summary>
    public static (bool Ok, GameObject? Target, string? Failure) Target(
        EditorCommandContext context,
        Guid objectId,
        string describe)
    {
        GameObject? target = context.Scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);

        if (target is null)
        {
            return (false, null, $"[editor] could not {describe}: the object is gone.");
        }

        if (target.PrefabSource is null)
        {
            return (false, null, $"[editor] could not {describe}: it is not linked to a prefab.");
        }

        return (true, target, null);
    }

    /// <summary>The object and everything under it.</summary>
    public static List<GameObject> Subtree(GameObject root)
    {
        List<GameObject> all = [];
        Collect(root, all);
        return all;
    }

    /// <summary>The same diff the inspector shows, computed against the assets through the session's loader.</summary>
    public static IReadOnlyList<PrefabOverrideSnapshot> Diff(EditorCommandContext context, GameObject linked) =>
        context.LoadPrefabAsset is { } load
            ? new PrefabSourceIndex(load, context.ProjectRoot).Diff(linked)
            : [];

    /// <summary>Where the linked object's asset lives, or (null, why) when it cannot be found.</summary>
    public static (string? AssetPath, string? Error) AssetPathFor(EditorCommandContext context, GameObject linked)
    {
        if (linked.PrefabSource is not { } link)
        {
            return (null, "it is not linked to a prefab.");
        }

        return context.ProjectRoot is { } root && AssetMeta.FindByGuid(root, link.AssetGuid) is { } assetPath
            ? (assetPath, null)
            : (null, "the prefab asset is gone.");
    }

    /// <summary>An asset's objects, freshly loaded through the session's loader, or (null, why).</summary>
    public static (List<GameObject>? Roots, string? Error) LoadAsset(EditorCommandContext context, string assetPath) =>
        context.LoadPrefabAsset is { } load && load(assetPath) is { Count: > 0 } roots
            ? (roots, null)
            : (null, "the prefab source could not be read.");

    /// <summary>The object in an asset's freshly loaded objects that a link points at.</summary>
    public static GameObject? FindFileObject(IEnumerable<GameObject> roots, int fileId)
    {
        foreach (GameObject root in roots)
        {
            foreach (GameObject go in Subtree(root))
            {
                if (go.PrefabSource is { } link && link.FileId == fileId)
                {
                    return go;
                }
            }
        }

        return null;
    }

    /// <summary>What to do after a write may have touched a light or a camera: the scene's lists follow.</summary>
    public static void AfterWrite(EditorCommandContext context, string propertyPath)
    {
        if (propertyPath == "*" || propertyPath.StartsWith("Enabled:", StringComparison.Ordinal))
        {
            context.Scene.FindMainComponents();
        }
    }

    private static void Collect(GameObject go, List<GameObject> into)
    {
        into.Add(go);

        foreach (Transform child in go.Transform.Children.ToList())
        {
            Collect(child.GameObject, into);
        }
    }
}
