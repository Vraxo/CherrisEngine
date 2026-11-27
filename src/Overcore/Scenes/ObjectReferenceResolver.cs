using Overcore.Components;
using Overcore.Core;
using Overcore.Utils;
using System.Globalization;
using System.Reflection;

namespace Overcore.Serialization;

/// <summary>
/// Shared encoding, YAML representation, lookup, and clone-remapping helpers for serialized scene-object and
/// prefab-asset references (<see cref="GameObject"/>, <see cref="Transform"/>, <see cref="Component"/> subclasses
/// including <see cref="Script"/>, and <see cref="PrefabAsset"/>).
/// </summary>
public static class ObjectReferenceResolver
{
    public const string ObjectPrefix = "obj:";
    public const string PrefabPrefix = "prefab:";

    private const string YamlObjectKey = "Object";
    private const string YamlComponentKey = "Component";
    private const string YamlIndexKey = "Index";
    private const string YamlPrefabKey = "Prefab";
    private const string YamlPathKey = "Path";

    /// <summary>
    /// True when <paramref name="type"/> is an inspector-editable and YAML-serializable scene object or prefab
    /// asset reference (<see cref="GameObject"/>, <see cref="Transform"/>, any concrete or base <see cref="Component"/>,
    /// or <see cref="PrefabAsset"/>).
    /// </summary>
    public static bool IsReferenceType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type == typeof(GameObject)
            || type == typeof(Transform)
            || type == typeof(PrefabAsset)
            || typeof(Component).IsAssignableFrom(type);
    }

    public static bool IsSceneObjectReferenceType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type == typeof(GameObject)
            || type == typeof(Transform)
            || typeof(Component).IsAssignableFrom(type);
    }

    /// <summary>
    /// Formats a live reference value into a deterministic token used by snapshots, undo commands, play-mode
    /// capture/restore, and prefab override diffs. Empty string means <c>null</c> (<c>None</c>).
    /// </summary>
    public static string FormatToken(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;

            case GameObject go:
                return $"{ObjectPrefix}{go.Id:D}";

            case Transform transform when transform.GameObject is { } owner:
                return $"{ObjectPrefix}{owner.Id:D}";

            case Component component when component.GameObject is { } owner:
            {
                int index = IndexOfSameType(owner, component);
                return index > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{ObjectPrefix}{owner.Id:D}:{component.GetType().Name}:{index}")
                    : $"{ObjectPrefix}{owner.Id:D}:{component.GetType().Name}";
            }

            case PrefabAsset prefab:
                return $"{PrefabPrefix}{prefab.AssetGuid}|{prefab.AssetPath}";

            default:
                return string.Empty;
        }
    }

    /// <summary>Human-readable label for an assigned reference or <c>None (&lt;TypeName&gt;)</c> when unassigned.</summary>
    public static string FormatDisplayLabel(object? value, Type declaredType)
    {
        ArgumentNullException.ThrowIfNull(declaredType);

        return value switch
        {
            GameObject go => go.Name,
            Transform { GameObject: { } owner } => $"{owner.Name} (Transform)",
            Component { GameObject: { } owner } component => $"{owner.Name} ({component.GetType().Name})",
            PrefabAsset prefab => !string.IsNullOrWhiteSpace(prefab.AssetPath)
                ? $"{prefab.Name} ({prefab.AssetPath})"
                : prefab.Name,
            _ => $"None ({declaredType.Name})"
        };
    }

    /// <summary>
    /// Resolves a string token (or bare GUID / prefab path) against <paramref name="candidates"/> and/or
    /// <paramref name="projectRoot"/> into an instance assignable to <paramref name="targetType"/>.
    /// </summary>
    public static bool TryResolveToken(
        Type targetType,
        string? text,
        IEnumerable<GameObject>? candidates,
        string? projectRoot,
        out object? resolved,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        resolved = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string trimmed = text.Trim();

        if (targetType == typeof(PrefabAsset))
        {
            if (trimmed.StartsWith(PrefabPrefix, StringComparison.Ordinal))
            {
                string payload = trimmed[PrefabPrefix.Length..];
                int pipe = payload.IndexOf('|');
                string guid = pipe >= 0 ? payload[..pipe] : payload;
                string path = pipe >= 0 ? payload[(pipe + 1)..] : string.Empty;

                PrefabAsset? asset = PrefabAsset.FromGuidOrPath(guid, path, projectRoot);
                if (asset is null)
                {
                    error = $"expected a prefab reference, got '{text}'";
                    return false;
                }

                resolved = asset;
                return true;
            }

            if (trimmed.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                resolved = PrefabAsset.FromPath(trimmed, projectRoot);
                return true;
            }

            error = $"expected a prefab reference for {targetType.Name}, got '{text}'";
            return false;
        }

        if (!IsSceneObjectReferenceType(targetType))
        {
            error = $"unsupported reference type {targetType.Name}";
            return false;
        }

        string body = trimmed.StartsWith(ObjectPrefix, StringComparison.Ordinal)
            ? trimmed[ObjectPrefix.Length..]
            : trimmed;

        string[] parts = body.Split(':');
        if (parts.Length == 0 || !Guid.TryParse(parts[0], out Guid objectId))
        {
            error = $"expected an object reference GUID for {targetType.Name}, got '{text}'";
            return false;
        }

        string? componentTypeName = parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : null;
        int index = parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIdx)
            ? parsedIdx
            : 0;

        GameObject? targetObject = FindGameObject(objectId, candidates);
        if (targetObject is null)
        {
            // Missing scene target (e.g. referenced object was deleted or belongs to an unloaded scene):
            // leave unassigned without failing hard when restoring, unless no candidates exist at all.
            if (candidates is null)
            {
                error = $"no scene context available to resolve '{text}'";
                return false;
            }

            error = $"object '{objectId}' is not in the scene";
            return false;
        }

        if (!TryExtractTargetFromGameObject(targetType, targetObject, componentTypeName, index, out resolved))
        {
            error = $"'{targetObject.Name}' has no {targetType.Name} component";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts a live reference value into a YAML-serializable dictionary, or <c>null</c> when unassigned.
    /// When <paramref name="prefabLocalIds"/> is provided (saving a <c>.prefab</c> asset) and the referenced
    /// object lives inside the prefab hierarchy, its stable integer file ID is written so prefab assets stay
    /// self-contained across reloads.
    /// </summary>
    public static Dictionary<string, object>? ToYamlDictionary(
        object? value,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null,
        string? projectRoot = null)
    {
        switch (value)
        {
            case null:
                return null;

            case GameObject go:
                return new Dictionary<string, object>
                {
                    [YamlObjectKey] = FormatObjectIdentity(go, prefabLocalIds)
                };

            case Transform { GameObject: { } owner }:
                return new Dictionary<string, object>
                {
                    [YamlObjectKey] = FormatObjectIdentity(owner, prefabLocalIds),
                    [YamlComponentKey] = nameof(Transform)
                };

            case Component { GameObject: { } owner } component:
            {
                Dictionary<string, object> map = new()
                {
                    [YamlObjectKey] = FormatObjectIdentity(owner, prefabLocalIds),
                    [YamlComponentKey] = component.GetType().Name
                };

                int index = IndexOfSameType(owner, component);
                if (index > 0)
                {
                    map[YamlIndexKey] = index;
                }

                return map;
            }

            case PrefabAsset prefab:
            {
                string guid = prefab.AssetGuid;
                string path = prefab.AssetPath;

                if (string.IsNullOrWhiteSpace(guid) && !string.IsNullOrWhiteSpace(path))
                {
                    string? existing = prefab.ResolveExistingFilePath(projectRoot);
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        guid = AssetMeta.GetOrCreate(existing);
                    }
                }

                Dictionary<string, object> map = [];
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    map[YamlPrefabKey] = guid;
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    map[YamlPathKey] = path;
                }

                return map.Count > 0 ? map : null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a YAML scalar or mapping node into an instance assignable to <paramref name="targetType"/>.
    /// Returns <c>true</c> when the YAML node had the shape of a reference (including <c>null</c> or an
    /// unresolvable missing reference, which resolves to <c>null</c>).
    /// </summary>
    public static bool TryFromYamlValue(
        Type targetType,
        object? yamlValue,
        IReadOnlyDictionary<Guid, GameObject>? objectsByGuid,
        IReadOnlyDictionary<int, GameObject>? objectsByLocalId,
        string? projectRoot,
        out object? resolved)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        resolved = null;

        if (yamlValue is null)
        {
            return true;
        }

        if (yamlValue is string scalar)
        {
            if (string.IsNullOrWhiteSpace(scalar) || string.Equals(scalar, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            IEnumerable<GameObject>? allObjects = EnumerateDistinctObjects(objectsByGuid, objectsByLocalId);
            if (TryResolveToken(targetType, scalar, allObjects, projectRoot, out resolved, out _))
            {
                return true;
            }

            if (targetType == typeof(PrefabAsset))
            {
                resolved = PrefabAsset.FromGuidOrPath(null, scalar, projectRoot);
                return true;
            }

            return false;
        }

        if (yamlValue is not IDictionary<object, object> rawDict)
        {
            return false;
        }

        if (targetType == typeof(PrefabAsset))
        {
            string? guid = ReadDictString(rawDict, YamlPrefabKey);
            string? path = ReadDictString(rawDict, YamlPathKey);
            resolved = PrefabAsset.FromGuidOrPath(guid, path, projectRoot);
            return true;
        }

        if (!IsSceneObjectReferenceType(targetType))
        {
            return false;
        }

        string? objectKey = ReadDictString(rawDict, YamlObjectKey);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return true;
        }

        GameObject? targetObject = null;
        if (Guid.TryParse(objectKey, out Guid guidId) && objectsByGuid is not null)
        {
            objectsByGuid.TryGetValue(guidId, out targetObject);
        }
        else if (int.TryParse(objectKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int localId)
                 && objectsByLocalId is not null)
        {
            objectsByLocalId.TryGetValue(localId, out targetObject);
        }

        if (targetObject is null)
        {
            return true;
        }

        string? componentName = ReadDictString(rawDict, YamlComponentKey);
        int index = 0;
        if (ReadDictString(rawDict, YamlIndexKey) is { } indexText
            && int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            index = parsed;
        }

        TryExtractTargetFromGameObject(targetType, targetObject, componentName, index, out resolved);
        return true;
    }

    /// <summary>
    /// Remaps any serialized scene-object references on cloned components so internal references within an
    /// instantiated hierarchy point at the corresponding cloned <see cref="GameObject"/> / <see cref="Transform"/> /
    /// <see cref="Component"/> rather than the original template, while external scene references remain intact.
    /// </summary>
    public static void RemapClonedHierarchy(IReadOnlyDictionary<GameObject, GameObject> originalToClone)
    {
        ArgumentNullException.ThrowIfNull(originalToClone);

        if (originalToClone.Count == 0)
        {
            return;
        }

        foreach ((_, GameObject cloneGo) in originalToClone)
        {
            foreach (Component cloneComp in cloneGo.Components)
            {
                RemapComponentReferences(cloneComp, originalToClone);
            }
        }
    }

    /// <summary>
    /// Collects candidate <c>(Token, Label)</c> options for an inspector picker targeting <paramref name="targetType"/>.
    /// </summary>
    public static (IReadOnlyList<string> Tokens, IReadOnlyList<string> Labels) BuildChoices(
        Type targetType,
        IEnumerable<GameObject>? sceneObjects,
        string? projectRoot)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        List<string> tokens = [string.Empty];
        List<string> labels = [$"None ({targetType.Name})"];

        if (targetType == typeof(PrefabAsset))
        {
            string? root = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : ProjectFiles.ProjectRoot;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*.prefab", SearchOption.AllDirectories)
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        PrefabAsset asset = PrefabAsset.FromPath(file, root);
                        tokens.Add(FormatToken(asset));
                        labels.Add(FormatDisplayLabel(asset, targetType));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unreadable directory: leave None only.
                }
            }

            return (tokens, labels);
        }

        if (sceneObjects is null)
        {
            return (tokens, labels);
        }

        foreach (GameObject go in sceneObjects)
        {
            if (targetType == typeof(GameObject))
            {
                tokens.Add(FormatToken(go));
                labels.Add(go.Name);
            }
            else if (targetType == typeof(Transform))
            {
                tokens.Add(FormatToken(go.Transform));
                labels.Add($"{go.Name} (Transform)");
            }
            else if (typeof(Component).IsAssignableFrom(targetType))
            {
                foreach (Component comp in go.Components)
                {
                    if (targetType.IsInstanceOfType(comp))
                    {
                        tokens.Add(FormatToken(comp));
                        labels.Add($"{go.Name} ({comp.GetType().Name})");
                    }
                }
            }
        }

        return (tokens, labels);
    }

    /// <summary>
    /// Extracts an assignable value of <paramref name="targetType"/> from a dropped <see cref="GameObject"/>
    /// (for example when dragging a hierarchy node onto a <c>Transform</c>, <c>Light</c>, or <c>Script</c> field).
    /// </summary>
    public static bool TryExtractTargetFromGameObject(
        Type targetType,
        GameObject targetObject,
        string? preferredComponentTypeName,
        int index,
        out object? resolved)
    {
        resolved = null;

        if (targetType == typeof(GameObject))
        {
            resolved = targetObject;
            return true;
        }

        if (targetType == typeof(Transform))
        {
            resolved = targetObject.Transform;
            return true;
        }

        if (typeof(Component).IsAssignableFrom(targetType))
        {
            List<Component> matches = targetObject.Components
                .Where(comp => targetType.IsInstanceOfType(comp)
                    && (string.IsNullOrWhiteSpace(preferredComponentTypeName)
                        || string.Equals(comp.GetType().Name, preferredComponentTypeName, StringComparison.Ordinal)))
                .ToList();

            if (matches.Count == 0 && !string.IsNullOrWhiteSpace(preferredComponentTypeName))
            {
                // Fallback if a base type or renamed subclass still matches targetType.
                matches = targetObject.Components.Where(targetType.IsInstanceOfType).ToList();
            }

            if (matches.Count == 0)
            {
                return false;
            }

            int clamped = Math.Clamp(index, 0, matches.Count - 1);
            resolved = matches[clamped];
            return true;
        }

        return false;
    }

    private static void RemapComponentReferences(
        Component cloneComponent,
        IReadOnlyDictionary<GameObject, GameObject> originalToClone)
    {
        PropertyInfo[] properties = cloneComponent.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (prop.GetCustomAttribute<HideInInspectorAttribute>() is not null)
            {
                continue;
            }

            if (!IsSceneObjectReferenceType(prop.PropertyType))
            {
                continue;
            }

            object? current = prop.GetValue(cloneComponent);
            if (current is null)
            {
                continue;
            }

            switch (current)
            {
                case GameObject origGo when originalToClone.TryGetValue(origGo, out GameObject? mappedGo):
                    prop.SetValue(cloneComponent, mappedGo);
                    break;

                case Transform { GameObject: { } origTransformOwner }
                    when originalToClone.TryGetValue(origTransformOwner, out GameObject? mappedTransformOwner):
                    prop.SetValue(cloneComponent, mappedTransformOwner.Transform);
                    break;

                case Component { GameObject: { } origCompOwner } origComp
                    when originalToClone.TryGetValue(origCompOwner, out GameObject? mappedCompOwner):
                {
                    int idx = IndexOfSameType(origCompOwner, origComp);
                    if (TryExtractTargetFromGameObject(
                            prop.PropertyType,
                            mappedCompOwner,
                            origComp.GetType().Name,
                            idx,
                            out object? mappedComp)
                        && mappedComp is not null)
                    {
                        prop.SetValue(cloneComponent, mappedComp);
                    }

                    break;
                }
            }
        }
    }

    private static int IndexOfSameType(GameObject owner, Component target)
    {
        int index = 0;
        Type exactType = target.GetType();

        foreach (Component comp in owner.Components)
        {
            if (ReferenceEquals(comp, target))
            {
                return index;
            }

            if (comp.GetType() == exactType)
            {
                index++;
            }
        }

        return 0;
    }

    private static object FormatObjectIdentity(
        GameObject target,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds)
    {
        if (prefabLocalIds is not null && prefabLocalIds.TryGetValue(target, out int localId))
        {
            return localId;
        }

        return target.Id.ToString("D");
    }

    private static GameObject? FindGameObject(Guid id, IEnumerable<GameObject>? candidates)
    {
        if (candidates is not null)
        {
            foreach (GameObject go in candidates)
            {
                if (go.Id == id)
                {
                    return go;
                }
            }
        }

        if (Scene.Active is { } active)
        {
            foreach (GameObject go in active.GameObjects)
            {
                if (go.Id == id)
                {
                    return go;
                }
            }
        }

        return null;
    }

    private static IEnumerable<GameObject>? EnumerateDistinctObjects(
        IReadOnlyDictionary<Guid, GameObject>? byGuid,
        IReadOnlyDictionary<int, GameObject>? byLocalId)
    {
        if (byGuid is null && byLocalId is null)
        {
            return Scene.Active?.GameObjects;
        }

        HashSet<GameObject> set = [];
        if (byGuid is not null)
        {
            foreach (GameObject go in byGuid.Values)
            {
                set.Add(go);
            }
        }

        if (byLocalId is not null)
        {
            foreach (GameObject go in byLocalId.Values)
            {
                set.Add(go);
            }
        }

        return set;
    }

    private static string? ReadDictString(IDictionary<object, object> dict, string key)
    {
        foreach (KeyValuePair<object, object> kvp in dict)
        {
            if (string.Equals(kvp.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value?.ToString();
            }
        }

        return null;
    }
}
