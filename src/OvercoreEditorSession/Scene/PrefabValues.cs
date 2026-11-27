using Overcore;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace OvercoreEditorSession;

/// <summary>
/// Reads and writes one object's editable values as text, by the paths <see cref="PrefabPaths"/> names.
/// The same code reads both sides of an override's comparison — the instance and its prefab source — so two
/// values that look the same are the same, and a revert writes exactly what the inspector shows.
/// </summary>
public static class PrefabValues
{
    private const string PrefabLinkPrefix = "prefablink:";

    /// <summary>Every value the editor can override on an object, as text, keyed by path.</summary>
    /// <param name="gameObject">The object to read.</param>
    /// <returns>Name, the three Transform rows, each first-per-type component's switch and properties.</returns>
    public static Dictionary<string, string> Collect(GameObject gameObject)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            [PrefabPaths.Name] = gameObject.Name,
            [PrefabPaths.Transform(TransformField.Position)] = TransformRow.Format(gameObject.Transform.Position),
            [PrefabPaths.Transform(TransformField.Rotation)] = TransformRow.Format(RotationDegrees(gameObject)),
            [PrefabPaths.Transform(TransformField.Scale)] = TransformRow.Format(gameObject.Transform.Scale)
        };

        string? ownAssetGuid = gameObject.PrefabSource?.AssetGuid;

        // The first component of a type is the one the editor's rows edit (the same convention every command
        // follows), so it is the one an override can name.
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (Component component in gameObject.Components)
        {
            string typeName = component.GetType().Name;

            if (!seen.Add(typeName))
            {
                continue;
            }

            values[PrefabPaths.Enabled(typeName)] = component.Enabled ? "true" : "false";

            foreach (PropertySnapshot property in ComponentProperties.Read(component))
            {
                string propText = property.Kind == PropertyKind.ObjectReference
                    ? NormalizePrefabReferenceToken(gameObject, component, property.Name, property.Text, ownAssetGuid)
                    : property.Text;

                values[PrefabPaths.Property(typeName, property.Name)] = propText;
            }
        }

        return values;
    }

    /// <summary>The value one path names, as text, or null when the object has no such thing.</summary>
    /// <param name="gameObject">The object to read.</param>
    /// <param name="path">What to read, as <see cref="PrefabPaths"/> names it.</param>
    public static string? Read(GameObject gameObject, string path) =>
        Collect(gameObject).TryGetValue(path, out string? text) ? text : null;

    /// <summary>Writes one path's value from text, the way the inspector's fields write it.</summary>
    /// <param name="gameObject">The object to write.</param>
    /// <param name="path">What to write, as <see cref="PrefabPaths"/> names it.</param>
    /// <param name="text">The value to write.</param>
    /// <param name="error">Why the write could not happen, when it could not.</param>
    /// <returns>True when something was written.</returns>
    public static bool Write(GameObject gameObject, string path, string text, out string? error)
    {
        error = null;

        if (String.Equals(path, PrefabPaths.Name, StringComparison.Ordinal))
        {
            gameObject.Name = text;
            return true;
        }

        if (path.StartsWith("Transform.", StringComparison.Ordinal))
        {
            return WriteTransform(gameObject, path, text, out error);
        }

        bool enabledOnly = path.StartsWith("Enabled:", StringComparison.Ordinal);
        string typeName = enabledOnly ? path["Enabled:".Length..] : path.Split('.')[0];
        Component? component = gameObject.Components.FirstOrDefault(
            candidate => String.Equals(candidate.GetType().Name, typeName, StringComparison.Ordinal));

        if (component is null)
        {
            error = "no such component";
            return false;
        }

        if (enabledOnly)
        {
            if (!Boolean.TryParse(text, out bool enabled))
            {
                error = "not a switch value";
                return false;
            }

            component.Enabled = enabled;
            return true;
        }

        string propertyName = path[(typeName.Length + 1)..];
        string resolvedText = ExpandPrefabReferenceToken(gameObject, text);

        return ComponentProperties.TryApply(component, propertyName, resolvedText, out error);
    }

    private static string NormalizePrefabReferenceToken(
        GameObject owner,
        Component component,
        string propertyName,
        string rawToken,
        string? ownAssetGuid)
    {
        if (string.IsNullOrWhiteSpace(ownAssetGuid)
            || !rawToken.StartsWith(ObjectReferenceResolver.ObjectPrefix, StringComparison.Ordinal))
        {
            return rawToken;
        }

        PropertyInfo? prop = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        object? liveValue = prop?.GetValue(component);
        GameObject? targetGo = liveValue switch
        {
            GameObject go => go,
            Transform t => t.GameObject,
            Component c => c.GameObject,
            _ => null
        };

        if (targetGo?.PrefabSource is not { } targetSource
            || !String.Equals(targetSource.AssetGuid, ownAssetGuid, StringComparison.Ordinal)
            || !SharesPrefabInstanceSubtree(owner, targetGo, ownAssetGuid))
        {
            return rawToken;
        }

        string rest = rawToken[ObjectReferenceResolver.ObjectPrefix.Length..];
        int colon = rest.IndexOf(':');
        string suffix = colon >= 0 ? rest[colon..] : string.Empty;
        return $"{PrefabLinkPrefix}{targetSource.FileId.ToString(CultureInfo.InvariantCulture)}{suffix}";
    }

    private static string ExpandPrefabReferenceToken(GameObject owner, string text)
    {
        if (!text.StartsWith(PrefabLinkPrefix, StringComparison.Ordinal)
            || owner.PrefabSource is not { } ownSource)
        {
            return text;
        }

        string rest = text[PrefabLinkPrefix.Length..];
        int colon = rest.IndexOf(':');
        string idText = colon >= 0 ? rest[..colon] : rest;
        string suffix = colon >= 0 ? rest[colon..] : string.Empty;

        if (!Int32.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fileId))
        {
            return text;
        }

        GameObject root = FindPrefabInstanceRoot(owner, ownSource.AssetGuid);
        GameObject? match = FindInSubtreeByFileId(root, ownSource.AssetGuid, fileId);
        return match is not null
            ? $"{ObjectReferenceResolver.ObjectPrefix}{match.Id:D}{suffix}"
            : string.Empty;
    }

    private static bool SharesPrefabInstanceSubtree(GameObject a, GameObject b, string assetGuid) =>
        ReferenceEquals(FindPrefabInstanceRoot(a, assetGuid), FindPrefabInstanceRoot(b, assetGuid));

    private static GameObject FindPrefabInstanceRoot(GameObject node, string assetGuid)
    {
        GameObject current = node;
        while (current.Transform.Parent?.GameObject is { } parent
               && parent.PrefabSource is { } parentSource
               && String.Equals(parentSource.AssetGuid, assetGuid, StringComparison.Ordinal))
        {
            current = parent;
        }

        return current;
    }

    private static GameObject? FindInSubtreeByFileId(GameObject root, string assetGuid, int fileId)
    {
        if (root.PrefabSource is { } source
            && String.Equals(source.AssetGuid, assetGuid, StringComparison.Ordinal)
            && source.FileId == fileId)
        {
            return root;
        }

        foreach (Transform child in root.Transform.Children)
        {
            if (FindInSubtreeByFileId(child.GameObject, assetGuid, fileId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The rotation as the Euler degrees the inspector shows.</summary>
    /// <param name="gameObject">The object to read.</param>
    public static Vector3 RotationDegrees(GameObject gameObject) =>
        EngineMath.ToEulerAngles(gameObject.Transform.Rotation) * (180f / MathF.PI);

    private static bool WriteTransform(GameObject gameObject, string path, string text, out string? error)
    {
        error = null;

        string[] parts = text.Split(',');

        if (parts.Length != 3
            || !Single.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !Single.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !Single.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            error = "not three numbers";
            return false;
        }

        Vector3 value = new(x, y, z);

        switch (path["Transform.".Length..])
        {
            case nameof(TransformField.Position):
                gameObject.Transform.Position = value;
                return true;

            case nameof(TransformField.Scale):
                gameObject.Transform.Scale = value;
                return true;

            case nameof(TransformField.Rotation):
                Vector3 radians = value * (MathF.PI / 180f);
                gameObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
                return true;

            default:
                error = "no such transform row";
                return false;
        }
    }
}
