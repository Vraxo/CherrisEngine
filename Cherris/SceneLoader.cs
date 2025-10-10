using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Veldrid;
using YamlDotNet.Core;
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

        // A serializer is needed for our new, efficient conversion method.
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

        var sceneData = _deserializer.Deserialize<SceneData>(input);

        foreach (var goData in sceneData.GameObjects)
        {
            var go = new GameObject(goData.Name);

            if (goData.Components == null)
            {
                sceneObjects.Add(go);
                continue;
            }

            // Find and apply the Transform first, as it's fundamental.
            if (goData.Components.TryGetValue("Transform", out var transformProperties))
            {
                // This is the new, efficient way. We avoid creating strings by passing the
                // object graph directly from the serializer to the deserializer.
                var yaml = _serializer.Serialize(transformProperties);
                var transformData = _deserializer.Deserialize<TransformData>(yaml);

                go.Transform.Position = transformData.Position;
                go.Transform.Scale = transformData.Scale;

                var rotRadians = transformData.Rotation * (MathF.PI / 180.0f);
                go.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(rotRadians.Y, rotRadians.X, rotRadians.Z);
            }

            // Create and add all other components
            foreach (var componentKvp in goData.Components)
            {
                if (componentKvp.Key == "Transform") continue;
                AddComponent(go, componentKvp.Key, componentKvp.Value);
            }

            sceneObjects.Add(go);
        }

        return sceneObjects;
    }

    private void AddComponent(GameObject go, string componentType, object properties)
    {
        if (_componentFactories.TryGetValue(componentType, out var factory))
        {
            var component = factory(properties);
            if (component != null)
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