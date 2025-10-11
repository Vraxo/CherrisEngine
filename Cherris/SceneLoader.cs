using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            .WithTypeConverter(new RgbaFloatYamlTypeConverter())
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithTypeConverter(new RgbaFloatYamlTypeConverter())
            .Build();
    }

    public void RegisterComponentFactory(string typeName, Func<object, Component> factory)
    {
        _componentFactories[typeName] = factory;
    }

    public List<GameObject> LoadScene(string filePath)
    {
        var sw = Stopwatch.StartNew();
        var sceneObjects = new List<GameObject>();
        var input = new StringReader(File.ReadAllText(filePath));

        var sceneData = _deserializer.Deserialize<Dictionary<string, object>>(input);

        // Load outline profiles first, so they are available when components need them.
        if (sceneData.TryGetValue("OutlineProfiles", out var profilesObj))
        {
            // Re-serialize and deserialize to get strong types.
            var yaml = _serializer.Serialize(profilesObj);
            var profiles = _deserializer.Deserialize<List<OutlineProfile>>(new StringReader(yaml));
            _resourceManager.AddOutlineProfiles(profiles);
        }

        if (sceneData.TryGetValue("GameObjects", out var gameObjectsObj) && gameObjectsObj is List<object> gameObjectDataList)
        {
            var gameObjectDatas = gameObjectDataList.Cast<Dictionary<object, object>>();

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
        }
        sw.Stop();
        Console.WriteLine($"[PROFILE] SceneLoader.LoadScene('{filePath}') completed in {sw.ElapsedMilliseconds}ms");
        return sceneObjects;
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