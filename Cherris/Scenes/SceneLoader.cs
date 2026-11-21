using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Rendering;
using Cherris.Utils;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris.Serialization;

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
            .Build();
        _serializer = new SerializerBuilder().Build();
    }

    public void RegisterComponentFactory(string typeName, Func<object, Component> factory)
    {
        _componentFactories[typeName] = factory;
    }

    public List<GameObject> LoadPrefab(string filePath)
    {
        using var stream = ProjectFiles.Open(filePath);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return DeserializeObjects(reader, true); // True = return only roots
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

    private List<GameObject> DeserializeObjects(TextReader reader, bool returnRootsOnly)
    {
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(reader);
        if (sceneData is null || !sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return [];
        }

        var objects = new Dictionary<Guid, GameObject>();
        var parentMap = new Dictionary<Guid, Guid>();
        var resultList = new List<GameObject>();

        // Pass 1: Create
        foreach (var goData in gameObjectDatas)
        {
            string name = goData.TryGetValue("Name", out var n) ? (string)n : "GameObject";
            Guid id = goData.TryGetValue("Id", out var i) && Guid.TryParse((string)i, out Guid pid) ? pid : Guid.NewGuid();

            var go = new GameObject(name, id);
            objects[id] = go;
            resultList.Add(go);

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
                        AddComponent(go, (string)kvp.Key, kvp.Value);
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

        return returnRootsOnly ? resultList.Where(g => g.Transform.Parent == null).ToList() : resultList;
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

    private void AddComponent(GameObject go, string componentType, object properties)
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
            if (comp is Script s && properties is Dictionary<object, object> pd)
            {
                ApplyScriptProperties(s, pd);
            }
        }
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
            if (prop == null || !prop.CanWrite)
            {
                continue;
            }

            try
            {
                object val = prop.PropertyType.IsEnum && kvp.Value is string s
                    ? Enum.Parse(prop.PropertyType, s, true)
                    : Convert.ChangeType(kvp.Value, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(script, val);
            }
            catch { }
        }
    }
}