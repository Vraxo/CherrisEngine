using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Rendering;
using Overcore.Utils;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Overcore.Serialization;

public class SceneLoader
{
    private readonly IResourceManager _resourceManager;
    private readonly Dictionary<string, Func<object, Component>> _componentFactories = [];
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public SceneLoader(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithTypeConverter(new Vector3YamlTypeConverter())
            .WithTypeConverter(new Vector2YamlTypeConverter())
            .WithTypeConverter(new Vector4YamlTypeConverter())
            .Build();
        _serializer = new SerializerBuilder().Build();
    }

    public void RegisterComponentFactory(string typeName, Func<object, Component> factory)
    {
        _componentFactories[typeName] = factory;
    }

    /// <summary>
    /// Reads a prefab from a YAML string, optionally stamping every object that carries a file id with
    /// <paramref name="assetGuid"/>.
    /// </summary>
    public List<GameObject> LoadPrefabFromString(string yamlContent, string? assetGuid = null)
    {
        Dictionary<GameObject, int> fileIds = [];
        using var reader = new StringReader(yamlContent);
        ComponentDeserializer.EnsureRegistered(this, _resourceManager);
        List<GameObject> roots = DeserializeObjects(reader, true, fileIds);

        if (!string.IsNullOrWhiteSpace(assetGuid))
        {
            foreach ((GameObject go, int fileId) in fileIds)
            {
                go.PrefabSource = new PrefabSource(assetGuid, fileId);
            }
        }

        return roots;
    }

    /// <summary>
    /// Reads a prefab asset and returns fresh objects for it: every object is new (two instances never share
    /// ids), and every one keeps its link to the asset — the guid from the file's <c>.meta</c> sidecar, and
    /// the id it carries inside the asset.
    /// </summary>
    /// <param name="filePath">The prefab asset's path.</param>
    /// <returns>The instance's root objects.</returns>
    public List<GameObject> LoadPrefab(string filePath)
    {
        using var stream = ProjectFiles.Open(filePath);
        if (stream is null)
        {
            return [];
        }

        Dictionary<GameObject, int> fileIds = [];

        using (var reader = new StreamReader(stream))
        {
            List<GameObject> roots = DeserializeObjects(reader, true, fileIds);
            string guid = AssetMeta.GetOrCreate(filePath);

            foreach ((GameObject go, int fileId) in fileIds)
            {
                go.PrefabSource = new PrefabSource(guid, fileId);
            }

            return roots;
        }
    }

    public List<GameObject> LoadScene(string filePath)
    {
        using var stream = ProjectFiles.Open(filePath);
        if (stream is null)
        {
            Logger.Error($"[SceneLoader] Scene not found: {filePath}");
            return [];
        }

        using var reader = new StreamReader(stream);
        return DeserializeObjects(reader, false); // False = return all (scene list)
    }

    public List<GameObject> LoadSceneFromYaml(string yamlContent)
    {
        using var reader = new StringReader(yamlContent);
        return DeserializeObjects(reader, false);
    }

    /// <param name="loadAsPrefab">
    /// True for a prefab asset: the objects are rebuilt with fresh ids (an instance is always new objects)
    /// and their file ids are collected, so the caller can stamp the instance's links.
    /// </param>
    /// <param name="fileIds">Receives each object's id within the asset, when the file carries one.</param>
    private List<GameObject> DeserializeObjects(
        TextReader reader,
        bool loadAsPrefab,
        IDictionary<GameObject, int>? fileIds = null)
    {
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(reader);
        if (sceneData is null || !sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return [];
        }

        var objects = new Dictionary<Guid, GameObject>();
        var objectsByLocalId = new Dictionary<int, GameObject>();
        var parentMap = new Dictionary<Guid, Guid>();
        var resultList = new List<GameObject>();
        var pendingReferences = new List<(Component Target, Dictionary<object, object> Properties)>();

        // Pass 1: Create
        foreach (var goData in gameObjectDatas)
        {
            string name = goData.TryGetValue("Name", out var n) ? (string)n : "GameObject";
            Guid id = goData.TryGetValue("Id", out var i) && Guid.TryParse((string)i, out Guid pid) ? pid : Guid.NewGuid();

            // An instance is always fresh objects; the file's ids only key the parent map below.
            var go = loadAsPrefab ? new GameObject(name) : new GameObject(name, id);
            objects[id] = go;
            resultList.Add(go);

            if (goData.TryGetValue("Prefab", out var prefabObj) && prefabObj is Dictionary<object, object> prefab)
            {
                // A scene says the link outright: which asset, and which object in it.
                string asset = prefab.TryGetValue("Asset", out var a) ? Convert.ToString(a) ?? string.Empty : string.Empty;
                int file = prefab.TryGetValue("File", out var f)
                    ? Convert.ToInt32(f, CultureInfo.InvariantCulture)
                    : 0;

                if (asset.Length > 0)
                {
                    go.PrefabSource = new PrefabSource(asset, file);
                }
            }
            else if (goData.TryGetValue("File", out var fileObj))
            {
                // A prefab asset says only the id within itself; the asset guid is the file's own.
                int fileId = Convert.ToInt32(fileObj, CultureInfo.InvariantCulture);
                objectsByLocalId[fileId] = go;
                if (fileIds is not null)
                {
                    fileIds[go] = fileId;
                }
            }

            if (goData.TryGetValue("Parent", out var pObj) && Guid.TryParse((string)pObj, out Guid pId))
            {
                parentMap[id] = pId;
            }

            if (goData.TryGetValue("Components", out var compsObj) && compsObj is Dictionary<object, object> comps)
            {
                if (comps.TryGetValue("Transform", out var tProps))
                {
                    ApplyTransformProperties(go.Transform, tProps);
                }

                foreach (var kvp in comps)
                {
                    if ((string)kvp.Key != "Transform")
                    {
                        AddComponent(go, (string)kvp.Key, kvp.Value, pendingReferences);
                    }
                }
            }
        }

        // Pass 2: Hierarchy
        foreach (var (childId, parentId) in parentMap)
        {
            if (objects.TryGetValue(childId, out var child) && objects.TryGetValue(parentId, out var parent))
            {
                child.Transform.Parent = parent.Transform;
            }
        }

        // Pass 3: Deferred object & asset references (forward, sibling, self, and cyclic links)
        foreach (var (comp, propsDict) in pendingReferences)
        {
            ResolveDeferredReferences(comp, propsDict, objects, objectsByLocalId);
        }

        return loadAsPrefab ? resultList.Where(g => g.Transform.Parent == null).ToList() : resultList;
    }

    private void ApplyTransformProperties(Transform transform, object properties)
    {
        var yaml = _serializer.Serialize(properties);
        var props = _deserializer.Deserialize<Dictionary<string, Vector3>>(yaml);

        if (props.TryGetValue("Position", out var pos))
        {
            transform.Position = pos;
        }

        if (props.TryGetValue("Scale", out var scale))
        {
            transform.Scale = scale;
        }

        if (props.TryGetValue("Rotation", out var rot))
        {
            var rad = rot * (MathF.PI / 180f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(rad.Y, rad.X, rad.Z);
        }
    }

    private void AddComponent(
        GameObject go,
        string componentType,
        object properties,
        List<(Component Target, Dictionary<object, object> Properties)> pendingReferences)
    {
        if (componentType == "Script" && properties is Dictionary<object, object> d && d.TryGetValue("Type", out var t))
        {
            componentType = t.ToString()!;
        }

        if (!_componentFactories.TryGetValue(componentType, out var factory))
        {
            Logger.Warning($"[SceneLoader] Unknown component type: '{componentType}' on GameObject '{go.Name}'");
            return;
        }

        var comp = factory(properties);
        if (comp != null)
        {
            go.AddComponent(comp);

            // The serializer writes the enable toggle for every component, whatever its factory reads
            // (SceneSerializer), so it is read back here for all of them, after the factory has had its say.
            if (properties is Dictionary<object, object> propsDict)
            {
                if (TryReadEnabled(propsDict, out bool enabled))
                {
                    comp.Enabled = enabled;
                }

                if (comp is Script s)
                {
                    ApplyScriptProperties(s, propsDict);
                }

                pendingReferences.Add((comp, propsDict));
            }
        }
    }

    private static void ResolveDeferredReferences(
        Component component,
        Dictionary<object, object> propsDict,
        IReadOnlyDictionary<Guid, GameObject> objectsByGuid,
        IReadOnlyDictionary<int, GameObject> objectsByLocalId)
    {
        Type type = component.GetType();
        foreach (var (key, value) in propsDict)
        {
            if (key is not string propName)
            {
                continue;
            }

            PropertyInfo? prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null
                || !prop.CanWrite
                || prop.GetIndexParameters().Length > 0
                || prop.IsDefined(typeof(HideInInspectorAttribute), false)
                || !ObjectReferenceResolver.IsReferenceType(prop.PropertyType))
            {
                continue;
            }

            if (ObjectReferenceResolver.TryFromYamlValue(
                    prop.PropertyType,
                    value,
                    objectsByGuid,
                    objectsByLocalId,
                    ProjectFiles.ProjectRoot,
                    out object? resolved))
            {
                prop.SetValue(component, resolved);
            }
        }
    }

    /// <summary>
    /// The enable toggle as the YAML gives it: a bool, or its name when the scalar came through as text.
    /// </summary>
    private static bool TryReadEnabled(Dictionary<object, object> props, out bool enabled)
    {
        enabled = true;

        if (!props.TryGetValue("Enabled", out var value))
        {
            return false;
        }

        if (value is bool flag)
        {
            enabled = flag;
            return true;
        }

        return value is string text && Boolean.TryParse(text, out enabled);
    }

    private static void ApplyScriptProperties(Script script, Dictionary<object, object> propsDict)
    {
        var type = script.GetType();
        foreach (var kvp in propsDict)
        {
            if (kvp.Key is not string name)
            {
                continue;
            }

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            // [HideInInspector] is the engine's own state, not the scene's: Component.Enabled is read above,
            // for every component, and Script.IsSuspended is never saved at all.
            if (prop == null
                || !prop.CanWrite
                || prop.GetIndexParameters().Length > 0
                || prop.IsDefined(typeof(HideInInspectorAttribute), false)
                || ObjectReferenceResolver.IsReferenceType(prop.PropertyType))
            {
                continue;
            }

            if (ComponentDeserializer.TryConvertPropertyValue(kvp.Value, prop.PropertyType, out object? converted))
            {
                prop.SetValue(script, converted);
            }
        }
    }
}