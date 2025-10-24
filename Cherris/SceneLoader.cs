using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Cherris.Rendering;
using Veldrid;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris;

public class SceneLoader
{
    private readonly IResourceManager _resourceManager;
    private readonly Dictionary<string, Func<object, Component>> _componentFactories = new();
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

    public List<GameObject> LoadScene(string filePath)
    {
        var sceneObjects = new List<GameObject>();
        var input = new StringReader(File.ReadAllText(filePath));
        var sceneData = _deserializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(input);

        if (!sceneData.TryGetValue("GameObjects", out var gameObjectDatas))
        {
            return sceneObjects;
        }

        foreach (var goData in gameObjectDatas)
        {
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

            if (componentsDict.TryGetValue("Transform", out var transformProperties))
            {
                ApplyTransformProperties(go.Transform, transformProperties);
            }

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
        var yaml = _serializer.Serialize(properties);
        var props = _deserializer.Deserialize<Dictionary<string, Vector3>>(yaml);

        if (props.TryGetValue("Position", out var pos)) transform.Position = pos;
        if (props.TryGetValue("Scale", out var scale)) transform.Scale = scale;
        if (props.TryGetValue("Rotation", out var rotDegrees))
        {
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