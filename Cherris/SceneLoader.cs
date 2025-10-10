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
    private readonly Dictionary<string, Func<ComponentData, Component>> _componentFactories = new();

    public SceneLoader(ResourceManager resourceManager, GraphicsDevice graphicsDevice)
    {
        _resourceManager = resourceManager;
        _graphicsDevice = graphicsDevice;
    }

    public void RegisterComponentFactory(string typeName, Func<ComponentData, Component> factory)
    {
        _componentFactories[typeName] = factory;
    }

    public List<GameObject> LoadScene(string filePath)
    {
        var sceneObjects = new List<GameObject>();
        var input = new StringReader(File.ReadAllText(filePath));

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithTypeConverter(new Vector3YamlTypeConverter())
            .Build();

        var sceneData = deserializer.Deserialize<SceneData>(input);

        foreach (var goData in sceneData.GameObjects)
        {
            var go = new GameObject(goData.Name);

            // --- Transform is now treated as a component in the data ---
            // Find the transform component data first, as it's required to exist.
            var transformDataComponent = goData.Components?.FirstOrDefault(c => c.Type == "Transform");
            if (transformDataComponent != null)
            {
                // We need to re-serialize and deserialize this specific part to get it into our TransformData class.
                // This is a common technique when dealing with loosely typed dictionaries from deserializers.
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(transformDataComponent.Properties);

                // --- FIX: The new DeserializerBuilder must also know about the Vector3 converter ---
                var transformDeserializer = new DeserializerBuilder()
                    .WithTypeConverter(new Vector3YamlTypeConverter())
                    .Build();
                var transformData = transformDeserializer.Deserialize<TransformData>(yaml);

                go.Transform.Position = transformData.Position;
                go.Transform.Scale = transformData.Scale;

                // Convert Euler angles (degrees) to Quaternion
                var rotRadians = transformData.Rotation * (MathF.PI / 180.0f);
                go.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(rotRadians.Y, rotRadians.X, rotRadians.Z);
            }

            // Create and add all other components
            if (goData.Components != null)
            {
                foreach (var componentData in goData.Components.Where(c => c.Type != "Transform"))
                {
                    AddComponent(go, componentData);
                }
            }

            sceneObjects.Add(go);
        }

        return sceneObjects;
    }

    private void AddComponent(GameObject go, ComponentData componentData)
    {
        if (_componentFactories.TryGetValue(componentData.Type, out var factory))
        {
            var component = factory(componentData);
            if (component != null)
            {
                go.AddComponent(component);
            }
        }
        else
        {
            Console.WriteLine($"[SceneLoader] Warning: No factory registered for component type '{componentData.Type}'.");
        }
    }
}