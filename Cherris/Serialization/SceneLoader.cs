using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Rendering;
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
        var input = new StringReader(File.ReadAllText(filePath));
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(input);

        if (!sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return [];
        }

        var createdGameObjects = new List<GameObject>();
        var oldToNewIdMap = new Dictionary<Guid, Guid>();
        var newIdToGameObjectMap = new Dictionary<Guid, GameObject>();
        var childToParentMap = new Dictionary<Guid, Guid>(); // <new_child_id, old_parent_id>

        // Pass 1: Create all GameObjects with new GUIDs and deserialize their components
        foreach (var goData in gameObjectDatas)
        {
            string name = "GameObject";
            if (goData.TryGetValue("Name", out var nameObj) && nameObj is string goName)
            {
                name = goName;
            }

            Guid oldId = Guid.Empty;
            if (goData.TryGetValue("Id", out var idObj) && Guid.TryParse(idObj as string, out Guid parsedId))
            {
                oldId = parsedId;
            }

            Guid newId = Guid.NewGuid();
            oldToNewIdMap[oldId] = newId;

            var go = new GameObject(name, newId);
            newIdToGameObjectMap[newId] = go;
            createdGameObjects.Add(go);

            if (goData.TryGetValue("Parent", out var parentIdObj) && Guid.TryParse(parentIdObj as string, out Guid parentId))
            {
                childToParentMap[newId] = parentId;
            }

            if (goData.TryGetValue("Components", out var componentsObj) && componentsObj is Dictionary<object, object> componentsDict)
            {
                if (componentsDict.TryGetValue("Transform", out var transformProperties))
                {
                    ApplyTransformProperties(go.Transform, transformProperties);
                }

                foreach (var componentKvp in componentsDict)
                {
                    if ((componentKvp.Key as string) == "Transform")
                    {
                        continue;
                    }

                    AddComponent(go, componentKvp.Key as string, componentKvp.Value);
                }
            }
        }

        // Pass 2: Hook up parent-child relationships using the remapped GUIDs
        foreach (var (newChildId, oldParentId) in childToParentMap)
        {
            if (oldToNewIdMap.TryGetValue(oldParentId, out Guid newParentId))
            {
                var child = newIdToGameObjectMap[newChildId];
                var parent = newIdToGameObjectMap[newParentId];
                child.Transform.Parent = parent.Transform;
            }
        }

        return createdGameObjects.Where(go => go.Transform.Parent is null).ToList();
    }


    public List<GameObject> LoadScene(string filePath)
    {
        var input = new StringReader(File.ReadAllText(filePath));
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(input);

        if (!sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return [];
        }

        var createdGameObjects = new Dictionary<Guid, GameObject>();
        var parentMap = new Dictionary<Guid, Guid>();

        // Pass 1: Create all GameObjects and components, storing parent relationships
        foreach (var goData in gameObjectDatas)
        {
            string name = "GameObject";
            if (goData.TryGetValue("Name", out var nameObj) && nameObj is string goName)
            {
                name = goName;
            }

            Guid id = Guid.NewGuid();
            if (goData.TryGetValue("Id", out var idObj) && Guid.TryParse(idObj as string, out Guid parsedId))
            {
                id = parsedId;
            }

            var go = new GameObject(name, id);
            createdGameObjects[id] = go;

            if (goData.TryGetValue("Parent", out var parentIdObj) && Guid.TryParse(parentIdObj as string, out Guid parentId))
            {
                parentMap[id] = parentId;
            }

            if (goData.TryGetValue("Components", out var componentsObj) && componentsObj is Dictionary<object, object> componentsDict)
            {
                if (componentsDict.TryGetValue("Transform", out var transformProperties))
                {
                    ApplyTransformProperties(go.Transform, transformProperties);
                }

                foreach (var componentKvp in componentsDict)
                {
                    if ((componentKvp.Key as string) == "Transform")
                    {
                        continue;
                    }

                    AddComponent(go, componentKvp.Key as string, componentKvp.Value);
                }
            }
        }

        // Pass 2: Hook up parent-child relationships
        foreach (var (childId, parentId) in parentMap)
        {
            if (createdGameObjects.TryGetValue(childId, out var child) && createdGameObjects.TryGetValue(parentId, out var parent))
            {
                child.Transform.Parent = parent.Transform;
            }
        }

        return createdGameObjects.Values.ToList();
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

        if (props.TryGetValue("Rotation", out var rotDegrees))
        {
            var rotRadians = rotDegrees * (MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(rotRadians.Y, rotRadians.X, rotRadians.Z);
        }
    }

    private void AddComponent(GameObject go, string componentType, object properties)
    {
        // Support the generic "Script" key where the actual type is defined in the properties
        if (componentType == "Script" && properties is Dictionary<object, object> dict && dict.TryGetValue("Type", out var typeObj))
        {
            componentType = typeObj.ToString() ?? "";
        }

        if (string.IsNullOrEmpty(componentType))
        {
            return;
        }

        if (!_componentFactories.TryGetValue(componentType, out var factory))
        {
            Logger.Warning($"[SceneLoader] Warning: No factory registered for component type '{componentType}'.");
            return;
        }

        var component = factory(properties);
        if (component is null)
        {
            return;
        }

        _ = go.AddComponent(component);

        if (component is Script script && properties is Dictionary<object, object> propsDict)
        {
            ApplyScriptProperties(script, propsDict);
        }
    }

    private static void ApplyScriptProperties(Script script, Dictionary<object, object> propsDict)
    {
        var scriptType = script.GetType();
        foreach (var propKvp in propsDict)
        {
            if (propKvp.Key is not string propName)
            {
                continue;
            }

            PropertyInfo? propertyInfo = scriptType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (propertyInfo is null || !propertyInfo.CanWrite)
            {
                continue;
            }

            try
            {
                object convertedValue;
                var propType = propertyInfo.PropertyType;
                var yamlValue = propKvp.Value;

                convertedValue = propType.IsEnum && yamlValue is string stringValue
                    ? Enum.Parse(propType, stringValue, true)
                    : Convert.ChangeType(yamlValue, propType, CultureInfo.InvariantCulture);
                propertyInfo.SetValue(script, convertedValue);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[SceneLoader] Warning: Could not set property '{propName}' on component '{scriptType.Name}'. Reason: {ex.Message}");
            }
        }
    }
}