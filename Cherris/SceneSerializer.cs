using Cherris.Rendering;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using Vortice.Direct3D;
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
                ["Position"] = new List<float> { go.Transform.Position.X, go.Transform.Position.Y, go.Transform.Position.Z }
            };

            var eulerDegrees = EngineMath.ToEulerAngles(go.Transform.Rotation) * (180.0f / MathF.PI);
            // Loader expects Pitch(X), Yaw(Y), Roll(Z). Our math gives Roll(X), Pitch(Y), Yaw(Z).
            // So we write them out in the correct order for the loader.
            transformData["Rotation"] = new List<float> { eulerDegrees.Y, eulerDegrees.Z, eulerDegrees.X };
            transformData["Scale"] = new List<float> { go.Transform.Scale.X, go.Transform.Scale.Y, go.Transform.Scale.Z };
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
                            mrData["TextureTiling"] = new List<float> { mr.TextureTiling.X, mr.TextureTiling.Y };
                        if (mr.EmissiveColor != Vector3.Zero)
                            mrData["EmissiveColor"] = new List<float> { mr.EmissiveColor.X, mr.EmissiveColor.Y, mr.EmissiveColor.Z };
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