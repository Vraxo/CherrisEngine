using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
            var goData = new Dictionary<string, object>
            {
                ["Id"] = go.Id.ToString(),
                ["Name"] = go.Name
            };

            if (go.Transform.Parent is not null)
            {
                goData["Parent"] = go.Transform.Parent.GameObject.Id.ToString();
            }

            var componentsData = new Dictionary<string, object>();

            var transformData = new Dictionary<string, object>
            {
                ["Position"] = go.Transform.Position
            };

            // ToEulerAngles returns (Pitch, Yaw, Roll).
            // The loader expects this order to create the quaternion.
            // We save it directly in this logical order.
            var eulerDegrees = EngineMath.ToEulerAngles(go.Transform.Rotation) * (180.0f / MathF.PI);
            transformData["Rotation"] = eulerDegrees;
            transformData["Scale"] = go.Transform.Scale;
            componentsData["Transform"] = transformData;

            foreach (var component in go.Components)
            {
                // This is the critical fix: skip serializing the EditorController component,
                // but not the entire GameObject it's attached to.
                if (component.GetType().Name == "EditorController") continue;

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
                        componentsData["Camera"] = new Dictionary<string, object>();
                        break;

                    case Skybox skybox:
                        var skyboxData = new Dictionary<string, object> { ["CubeMap"] = skybox.CubeMapName };
                        componentsData["Skybox"] = skyboxData;
                        break;

                    case Script script:
                        componentsData[script.GetType().Name] = SerializeScriptProperties(script);
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

    private static Dictionary<string, object> SerializeScriptProperties(Script script)
    {
        var propertiesData = new Dictionary<string, object>();
        var properties = script.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<HideInInspectorAttribute>() == null);

        foreach (var prop in properties)
        {
            propertiesData[prop.Name] = prop.GetValue(script);
        }

        return propertiesData;
    }
}