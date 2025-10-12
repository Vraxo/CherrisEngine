using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Veldrid;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris;

public class SceneLoader
{
    private readonly ResourceManager _resourceManager;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<string, Func<object, Component>> _componentFactories = new();
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public SceneLoader(ResourceManager resourceManager, GraphicsDevice graphicsDevice)
    {
        _resourceManager = resourceManager;
        _graphicsDevice = graphicsDevice;

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithTypeConverter(new Vector3YamlTypeConverter())
            .Build();

        // A serializer is used to re-serialize parts of the YAML object graph.
        // This allows us to leverage the deserializer's type conversion capabilities
        // (e.g., for Vector3) without writing manual, reflection-based conversion logic.
        _serializer = new SerializerBuilder().Build();
    }

    public void RegisterComponentFactory(string typeName, Func<object, Component> factory)
    {
        _componentFactories[typeName] = factory;
    }

    public List<GameObject> LoadScene(string filePath)
    {
        var sceneObjects = new List<GameObject>();
        var input = new StringReader(File.ReadAllText(filePath));

        // Instead of custom data classes, deserialize into a generic dictionary structure.
        // This is more flexible and avoids a rigid coupling to the YAML file structure.
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(input);

        if (!sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return sceneObjects; // Return empty list if no game objects are defined
        }

        foreach (var goData in gameObjectDatas)
        {
            // Extract name, defaulting if not present.
            string name = "GameObject";
            if (goData.TryGetValue("Name", out var nameObj) && nameObj is string goName)
            {
                name = goName;
            }
            var go = new GameObject(name);

            if (!goData.TryGetValue("Components", out var componentsObj) || componentsObj is not Dictionary<object, object> componentsDict)
            {
                sceneObjects.Add(go);
                continue;
            }

            // Find and apply the Transform first, as it's fundamental.
            if (componentsDict.TryGetValue("Transform", out var transformProperties))
            {
                ApplyTransformProperties(go.Transform, transformProperties);
            }

            // Create and add all other components
            foreach (var componentKvp in componentsDict)
            {
                if (componentKvp.Key as string == "Transform") continue;
                AddComponent(go, componentKvp.Key as string, componentKvp.Value);
            }

            sceneObjects.Add(go);
        }

        return sceneObjects;
    }

    private void ApplyTransformProperties(Transform transform, object properties)
    {
        // This is a pragmatic way to reuse our Vector3YamlTypeConverter without reflection.
        // We serialize the properties object back to a YAML string and then
        // deserialize it into a dictionary where we know the values will be Vector3.
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
            // Convert Euler angles from degrees to radians for quaternion creation.
            var rotRadians = rotDegrees * (MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(rotRadians.Y, rotRadians.X, rotRadians.Z);
        }
    }

    private void AddComponent(GameObject go, string componentType, object properties)
    {
        if (string.IsNullOrEmpty(componentType)) return;

        if (_componentFactories.TryGetValue(componentType, out var factory))
        {
            var component = factory(properties);
            if (component is not null)
            {
                go.AddComponent(component);
            }
        }
        else
        {
            Console.WriteLine($"[SceneLoader] Warning: No factory registered for component type '{componentType}'.");
        }
    }
}