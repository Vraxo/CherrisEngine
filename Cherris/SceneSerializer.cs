using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris;

public class SceneSerializer
{
    private readonly ISerializer _serializer;

    public SceneSerializer()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithTypeConverter(new Vector3YamlTypeConverter())
            .WithTypeConverter(new Vector2YamlTypeConverter())
            .Build();
    }

    public void SaveScene(IEnumerable<GameObject> gameObjects, string filePath)
    {
        var gameObjectsData = new List<Dictionary<string, object>>();

        foreach (var go in gameObjects)
        {
            // Don't save the editor-only camera controller if it exists
            if (go.Components.Any(c => c.GetType().Name == "EditorController")) continue;

            var goData = new Dictionary<string, object> { ["Name"] = go.Name };

            var componentsData = new Dictionary<string, object>();

            // --- Transform ---
            var transformData = new Dictionary<string, object>
            {
                ["Position"] = go.Transform.Position
            };

            var eulerDegrees = EngineMath.ToEulerAngles(go.Transform.Rotation) * (180.0f / MathF.PI);
            // Loader expects Pitch(X), Yaw(Y), Roll(Z). Our math gives Roll(X), Pitch(Y), Yaw(Z).
            // So we create a new Vector3 with components in the correct order for the loader.
            transformData["Rotation"] = new Vector3(eulerDegrees.Y, eulerDegrees.Z, eulerDegrees.X);
            transformData["Scale"] = go.Transform.Scale;
            componentsData["Transform"] = transformData;

            // --- Other Components ---
            foreach (var component in go.Components)
            {
                switch (component)
                {
                    case MeshRenderer mr:
                        var mrData = new Dictionary<string, object>
                        {
                            ["Mesh"] = mr.MeshName,
                            ["Texture"] = mr.TextureName
                        };
                        if (mr.TextureTiling != Vector2.One)
                            mrData["TextureTiling"] = mr.TextureTiling;
                        if (mr.EmissiveColor != Vector3.Zero)
                            mrData["EmissiveColor"] = mr.EmissiveColor;
                        componentsData["MeshRenderer"] = mrData;
                        break;

                    case Camera:
                        componentsData["Camera"] = new Dictionary<string, object>(); // No properties yet
                        break;

                    case Skybox skybox:
                        var skyboxData = new Dictionary<string, object> { ["CubeMap"] = skybox.CubeMapName };
                        componentsData["Skybox"] = skyboxData;
                        break;

                    case Script script:
                        // Properties of scripts are not yet serialized, just their presence on the GameObject.
                        componentsData[script.GetType().Name] = new Dictionary<string, object>();
                        break;
                }
            }
            goData["Components"] = componentsData;
            gameObjectsData.Add(goData);
        }

        var root = new Dictionary<string, object>
        {
            { "GameObjects", gameObjectsData }
        };

        var yaml = _serializer.Serialize(root);
        File.WriteAllText(filePath, yaml);
    }
}