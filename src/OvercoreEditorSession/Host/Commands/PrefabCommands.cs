using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Saves an object and its children as a prefab asset and makes the saved objects the asset's first
/// instance, as the hierarchy's context menu sends: Unity's "Create Prefab".
/// </summary>
/// <param name="ObjectId">The object to save; its subtree is the asset.</param>
/// <param name="AssetPath">Where the prefab asset is written.</param>
/// <remarks>
/// The written objects keep the ids they were written with, so they are linked to the asset from now on.
/// Undo takes the links back but leaves the asset on disk: the step can be redone, and the file is a
/// project asset from the moment it is written. Creating a prefab from an already-linked object re-links it
/// to the new asset, and Undo puts the old links back.
/// </remarks>
public sealed class CreatePrefabFromSelectionCommand(Guid objectId, string assetPath) : IUndoableCommand
{
    private List<(GameObject GameObject, PrefabSource? Previous)>? _stamped;

    /// <inheritdoc />
    public string Describe => $"create prefab {Path.GetFileNameWithoutExtension(assetPath)}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Find(context.Scene) is not { } root)
        {
            return $"[editor] could not {Describe}: the object is gone.";
        }

        string? directory = Path.GetDirectoryName(assetPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // The old links are remembered before the save stamps the new ones over them.
        _stamped = [];
        Collect(root, _stamped);

        Save(root);

        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] created prefab {Path.GetFileName(assetPath)}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_stamped is null)
        {
            return null;
        }

        foreach ((GameObject go, PrefabSource? previous) in _stamped)
        {
            go.PrefabSource = previous;
        }

        context.Scene.IsDirty = true;

        return $"[editor] unlinked the prefab {Path.GetFileName(assetPath)}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_stamped is null || _stamped.Count == 0)
        {
            return null;
        }

        // The asset is rewritten and the objects relinked at the ids the save gives them — the same ids as
        // the first time, because the save numbers objects the same way twice.
        Save(_stamped[0].GameObject);

        context.Scene.IsDirty = true;

        return $"[editor] created prefab {Path.GetFileName(assetPath)} again.";
    }

    /// <summary>Writes the asset; the save stamps every written object with its link to it.</summary>
    private void Save(GameObject root) => new SceneSerializer().SavePrefab(root, assetPath);

    private static void Collect(GameObject go, List<(GameObject GameObject, PrefabSource? Previous)> into)
    {
        into.Add((go, go.PrefabSource));

        foreach (Transform child in go.Transform.Children.ToList())
        {
            Collect(child.GameObject, into);
        }
    }

    private GameObject? Find(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}

/// <summary>
/// Adds a prefab to the scene as a fresh instance of the asset, as the content browser sends: Unity's
/// "Instantiate Prefab". The instance arrives at the scene root.
/// </summary>
/// <param name="AssetPath">The prefab asset to instantiate.</param>
/// <remarks>
/// The whole subtree arrives together and leaves together on Undo, as a delete would; Undo puts it back at
/// the same place in the scene's list. Every object carries its link to the asset.
/// </remarks>
public sealed class InstantiatePrefabCommand(string assetPath) : IUndoableCommand
{
    /// <param name="GameObject">An object the instance added.</param>
    /// <param name="IndexInScene">Where it landed, so Redo can put it back.</param>
    private sealed record Added(GameObject GameObject, int IndexInScene);

    private List<Added>? _added;

    /// <inheritdoc />
    public string Describe => $"instantiate {Path.GetFileNameWithoutExtension(assetPath)}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.LoadPrefabAsset is not { } load)
        {
            return $"[editor] could not {Describe}: this session cannot read prefabs.";
        }

        List<GameObject> roots = load(assetPath);

        if (roots.Count == 0)
        {
            return $"[editor] could not {Describe}: the prefab could not be read.";
        }

        List<GameObject> all = [];

        foreach (GameObject root in roots)
        {
            Collect(root, all);
        }

        _added = [];
        int index = context.Scene.GameObjects.Count;

        foreach (GameObject go in all)
        {
            _added.Add(new Added(go, index++));
            context.Scene.AddGameObject(go);
        }

        // The instance may bring the camera or the light the scene was waiting for.
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] instantiated {Path.GetFileName(assetPath)}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null)
        {
            return null;
        }

        foreach (Added added in _added)
        {
            context.Scene.GameObjects.Remove(added.GameObject);
        }

        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;

        return $"[editor] removed the {Path.GetFileNameWithoutExtension(assetPath)} instance.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null)
        {
            return null;
        }

        foreach (Added added in _added.OrderBy(added => added.IndexInScene))
        {
            if (added.IndexInScene >= 0 && added.IndexInScene <= context.Scene.GameObjects.Count)
            {
                context.Scene.GameObjects.Insert(added.IndexInScene, added.GameObject);
            }
            else
            {
                context.Scene.GameObjects.Add(added.GameObject);
            }
        }

        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;

        return $"[editor] instantiated {Path.GetFileName(assetPath)} again.";
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
