using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Adds an empty object at the scene root, as the hierarchy's create menu sends: Unity's "Create Empty".
/// </summary>
/// <param name="ObjectId">The id the object is created with, so the hierarchy can select it at once.</param>
/// <param name="Name">Its name — "GameObject", the way Unity names an empty.</param>
/// <remarks>
/// One click is one undo step: <see cref="Undo"/> takes the object back out and <see cref="Redo"/> puts the
/// same object back, with whatever the author gave it in between.
/// </remarks>
public sealed class CreateGameObjectCommand(Guid objectId, string name) : IUndoableCommand
{
    private GameObject? _created;

    /// <inheritdoc />
    public string Describe => $"create {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The first run makes the object; Redo re-adds the same one.
        _created ??= new GameObject(name, objectId);

        context.Scene.AddGameObject(_created);
        context.History.Record(this);

        return $"[editor] created {name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_created is null)
        {
            return null;
        }

        context.Scene.RemoveGameObject(_created);

        return $"[editor] removed {name}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_created is null)
        {
            return null;
        }

        context.Scene.AddGameObject(_created);

        return $"[editor] created {name} again.";
    }
}

/// <summary>
/// Takes an object and everything under it out of the scene, as the hierarchy's context menu sends:
/// Unity's "Delete".
/// </summary>
/// <param name="ObjectId">The object to take out.</param>
/// <param name="Name">Its name, so the step can be named before it runs.</param>
/// <remarks>
/// The subtree leaves together and comes back together. Nothing is disposed on the way out — the same
/// objects return on Undo, renderers intact — and Undo puts every object back at its old index, with the
/// root under its old parent at its old place among the siblings.
/// </remarks>
public sealed class DeleteGameObjectCommand(Guid objectId, string name) : IUndoableCommand
{
    /// <param name="GameObject">The object taken out.</param>
    /// <param name="IndexInScene">Where it sat in the scene's flat list, so Undo can put it back.</param>
    /// <param name="Parent">The parent the root was under; null for the rest of the subtree.</param>
    /// <param name="SiblingIndex">Where the root sat among its parent's children.</param>
    private sealed record Removed(GameObject GameObject, int IndexInScene, Transform? Parent, int SiblingIndex);

    private List<Removed>? _removed;

    /// <inheritdoc />
    public string Describe => $"delete {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Remove(context);
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_removed is null)
        {
            return null;
        }

        foreach (Removed removed in _removed.OrderBy(removed => removed.IndexInScene))
        {
            GameObject go = removed.GameObject;

            if (removed.IndexInScene >= 0 && removed.IndexInScene <= context.Scene.GameObjects.Count)
            {
                context.Scene.GameObjects.Insert(removed.IndexInScene, go);
            }
            else
            {
                context.Scene.GameObjects.Add(go);
            }
        }

        // The root goes back under its old parent; the Parent setter appends, so the child list is walked
        // back to where the root sat.
        foreach (Removed removed in _removed)
        {
            if (removed.Parent is not null && removed.SiblingIndex >= 0)
            {
                removed.GameObject.Transform.Parent = removed.Parent;
                removed.Parent.Children.RemoveAt(removed.Parent.Children.Count - 1);
                removed.Parent.Children.Insert(
                    Math.Min(removed.SiblingIndex, removed.Parent.Children.Count),
                    removed.GameObject.Transform);
            }
        }

        // A restored camera or light may be the main one again.
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;

        return $"[editor] restored {name}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Remove(context);
    }

    /// <summary>The subtree leaves: remembered, unlinked from its parent, and taken out of the scene's lists.</summary>
    private string? Remove(EditorCommandContext context)
    {
        if (context.Scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId) is not { } target)
        {
            return $"[editor] could not {Describe}: the object is gone.";
        }

        List<GameObject> subtree = [];
        Collect(target, subtree);

        Transform? parent = target.Transform.Parent;
        int siblingIndex = parent is null ? -1 : parent.Children.IndexOf(target.Transform);

        // Every index is read before anything is removed, so Undo knows where things sat.
        _removed = subtree
            .Select(go => new Removed(
                go,
                context.Scene.GameObjects.IndexOf(go),
                go == target ? parent : null,
                go == target ? siblingIndex : -1))
            .ToList();

        foreach (Removed removed in _removed)
        {
            context.Scene.GameObjects.Remove(removed.GameObject);
        }

        target.Transform.Parent = null;

        // The lights the subtree carried are gone with it, and a deleted camera or light may have been the
        // main one; the next in the scene takes over.
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] deleted {name}.";
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

/// <summary>Renames an object.</summary>
/// <param name="ObjectId">The object.</param>
/// <param name="Name">The new name.</param>
public sealed class RenameObjectCommand(Guid objectId, string name) : IUndoableCommand
{
    private string? _before;

    /// <inheritdoc />
    public string Describe => $"rename to {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return "[editor] could not rename: that object is no longer in the scene.";
        }

        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return "[editor] could not rename: a name cannot be empty.";
        }

        _before = target.Name;
        target.Name = trimmed;
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] renamed {_before} to {trimmed}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_before is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.Name = _before;
        context.Scene.IsDirty = true;

        return $"[editor] renamed {name} back to {_before}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.Name = name.Trim();
        context.Scene.IsDirty = true;

        return $"[editor] renamed to {name.Trim()}.";
    }

    private GameObject? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}
