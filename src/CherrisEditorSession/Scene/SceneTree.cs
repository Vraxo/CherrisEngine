namespace CherrisEditorSession;

/// <summary>A scene object and its children, as the hierarchy panel draws them.</summary>
/// <param name="Node">The object.</param>
/// <param name="Children">Direct children, in scene order.</param>
public sealed record SceneTreeNode(SceneNodeSnapshot Node, IReadOnlyList<SceneTreeNode> Children);

/// <summary>
/// Turns the flat object list of a <see cref="SceneSnapshot"/> back into the tree the engine keeps in
/// <c>Transform.Parent</c> / <c>Transform.Children</c>.
/// </summary>
public static class SceneTree
{
    /// <summary>
    /// Builds the roots of the scene. Objects whose parent is missing from the snapshot, or that sit in a
    /// parent cycle, are promoted to roots so a damaged scene still renders a complete hierarchy.
    /// </summary>
    public static IReadOnlyList<SceneTreeNode> Build(IReadOnlyList<SceneNodeSnapshot> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, SceneNodeSnapshot> byId = new(nodes.Count);

        foreach (SceneNodeSnapshot node in nodes)
        {
            byId[node.Id] = node;
        }

        HashSet<Guid> roots = [];
        Dictionary<Guid, List<SceneNodeSnapshot>> childrenByParent = [];

        foreach (SceneNodeSnapshot node in nodes)
        {
            if (IsRoot(node, byId))
            {
                roots.Add(node.Id);
            }
        }

        foreach (SceneNodeSnapshot node in nodes)
        {
            if (roots.Contains(node.Id) || node.ParentId is not Guid parentId)
            {
                continue;
            }

            if (!childrenByParent.TryGetValue(parentId, out List<SceneNodeSnapshot>? children))
            {
                children = [];
                childrenByParent[parentId] = children;
            }

            children.Add(node);
        }

        List<SceneTreeNode> built = [];

        foreach (SceneNodeSnapshot node in nodes)
        {
            if (roots.Contains(node.Id))
            {
                built.Add(BuildNode(node, childrenByParent, depth: 0));
            }
        }

        return built;
    }

    /// <summary>
    /// Flattens a tree into the rows a collapsed/expanded view shows, skipping the children of collapsed
    /// nodes.
    /// </summary>
    /// <param name="roots">Tree roots from <see cref="Build"/>.</param>
    /// <param name="isExpanded">Predicate deciding whether a node's children are visible.</param>
    public static IReadOnlyList<(SceneNodeSnapshot Node, int Depth)> Flatten(
        IReadOnlyList<SceneTreeNode> roots,
        Func<SceneNodeSnapshot, bool> isExpanded)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(isExpanded);

        List<(SceneNodeSnapshot Node, int Depth)> rows = [];
        Walk(roots, 0);

        return rows;

        void Walk(IReadOnlyList<SceneTreeNode> level, int depth)
        {
            foreach (SceneTreeNode entry in level)
            {
                rows.Add((entry.Node, depth));

                if (entry.Children.Count > 0 && isExpanded(entry.Node))
                {
                    Walk(entry.Children, depth + 1);
                }
            }
        }
    }

    private static bool IsRoot(SceneNodeSnapshot node, Dictionary<Guid, SceneNodeSnapshot> byId)
    {
        if (node.ParentId is not Guid parentId || parentId == node.Id || !byId.ContainsKey(parentId))
        {
            return true;
        }

        return IsInCycle(node, byId);
    }

    private static bool IsInCycle(SceneNodeSnapshot node, Dictionary<Guid, SceneNodeSnapshot> byId)
    {
        HashSet<Guid> seen = [];

        SceneNodeSnapshot? current = node;

        while (current?.ParentId is Guid parentId && byId.TryGetValue(parentId, out SceneNodeSnapshot? parent))
        {
            if (!seen.Add(parent.Id))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static SceneTreeNode BuildNode(
        SceneNodeSnapshot node,
        Dictionary<Guid, List<SceneNodeSnapshot>> childrenByParent,
        int depth)
    {
        // Every cycle member is already a root, so the edges left form a forest; the depth guard only
        // protects against a snapshot that lies.
        if (depth > 64 || !childrenByParent.TryGetValue(node.Id, out List<SceneNodeSnapshot>? children))
        {
            return new SceneTreeNode(node, []);
        }

        List<SceneTreeNode> built = new(children.Count);

        foreach (SceneNodeSnapshot child in children)
        {
            built.Add(BuildNode(child, childrenByParent, depth + 1));
        }

        return new SceneTreeNode(node, built);
    }
}
