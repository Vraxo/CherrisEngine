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

    public SceneLoader(ResourceManager resourceManager, GraphicsDevice graphicsDevice)
    {
        _resourceManager = resourceManager;
        _graphicsDevice = graphicsDevice;
    }

    public List<GameObject> LoadScene(string filePath)
    {
        var sceneObjects = new List<GameObject>();
        var input = new StringReader(File.ReadAllText(filePath));

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            // --- MODIFICATION: Register our custom converter ---
            .WithTypeConverter(new Vector3YamlTypeConverter())
            .Build();

        var sceneData = deserializer.Deserialize<SceneData>(input);

        foreach (var goData in sceneData.GameObjects)
        {
            var go = new GameObject(goData.Name);

            // Set Transform
            go.Transform.Position = goData.Transform.Position;
            go.Transform.Scale = goData.Transform.Scale;

            // Convert Euler angles (degrees) to Quaternion
            var rotRadians = goData.Transform.Rotation * (MathF.PI / 180.0f);
            go.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(rotRadians.Y, rotRadians.X, rotRadians.Z);

            // Create and add components
            foreach (var componentData in goData.Components)
            {
                AddComponent(go, componentData);
            }

            sceneObjects.Add(go);
        }

        return sceneObjects;
    }

    private void AddComponent(GameObject go, ComponentData componentData)
    {
        switch (componentData.Type)
        {
            case "MeshRenderer":
                string meshName = componentData.Properties["Mesh"];
                Mesh mesh = _resourceManager.GetMesh(meshName);
                if (mesh != null)
                {
                    var renderer = new MeshRenderer(mesh, _graphicsDevice);
                    go.AddComponent(renderer);
                }
                break;
                // Add cases for other components here in the future
        }
    }
}