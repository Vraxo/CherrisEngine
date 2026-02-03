using Cherris.Components;
using Cherris.Core;
using Cherris.Utils;
using System.Numerics;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris.Serialization;

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

    public void SavePrefab(GameObject rootGo, string filePath)
    {
        List<GameObject> hierarchy = [];
        GetHierarchy(rootGo, hierarchy);

        List<Dictionary<string, object>> gameObjectsData = [];

        foreach (GameObject gameObject in hierarchy)
        {
            Dictionary<string, object> goData = new()
            {
                ["Id"] = gameObject.Id.ToString(),
                ["Name"] = gameObject.Name
            };

            // Only save parent if it's part of the prefab hierarchy
            if (gameObject.Transform.Parent is not null && hierarchy.Contains(gameObject.Transform.Parent.GameObject))
            {
                goData["Parent"] = gameObject.Transform.Parent.GameObject.Id.ToString();
            }

            Dictionary<string, object> componentsData = SerializeGameObjectComponents(gameObject);
            goData["Components"] = componentsData;
            gameObjectsData.Add(goData);
        }

        Dictionary<string, object> root = new()
        {
            ["GameObjects"] = gameObjectsData
        };

        string yaml = _serializer.Serialize(root);
        File.WriteAllText(filePath, yaml);
    }

    public void SaveScene(IEnumerable<GameObject> gameObjects, string filePath)
    {
        string yaml = SerializeSceneToString(gameObjects);
        File.WriteAllText(filePath, yaml);
    }

    public string SerializeSceneToString(IEnumerable<GameObject> gameObjects)
    {
        List<Dictionary<string, object>> gameObjectsData = [];

        foreach (GameObject gameObject in gameObjects)
        {
            Dictionary<string, object> goData = new()
            {
                ["Id"] = gameObject.Id.ToString(),
                ["Name"] = gameObject.Name
            };

            if (gameObject.Transform.Parent is not null)
            {
                goData["Parent"] = gameObject.Transform.Parent.GameObject.Id.ToString();
            }

            Dictionary<string, object> componentsData = SerializeGameObjectComponents(gameObject);
            goData["Components"] = componentsData;
            gameObjectsData.Add(goData);
        }

        Dictionary<string, object> root = new()
        {
            ["GameObjects"] = gameObjectsData
        };

        return _serializer.Serialize(root);
    }

    private static Dictionary<string, object> SerializeGameObjectComponents(GameObject gameObject)
    {
        Dictionary<string, object> componentsData = [];

        Dictionary<string, object> transformData = new()
        {
            ["Position"] = gameObject.Transform.Position
        };

        Vector3 eulerDegrees = EngineMath.ToEulerAngles(gameObject.Transform.Rotation) * (180.0f / MathF.PI);
        transformData["Rotation"] = eulerDegrees;
        transformData["Scale"] = gameObject.Transform.Scale;
        componentsData["Transform"] = transformData;

        foreach (Component component in gameObject.Components)
        {
            // The editor controller should not be serialized.
            if (component.GetType().Name == "EditorController")
            {
                continue;
            }

            // MeshRenderer requires special handling for its nested Material object.
            if (component is MeshRenderer mr)
            {
                Dictionary<string, object> meshRendererData = new() { ["Mesh"] = mr.MeshName };
                Dictionary<string, object> materialData = new() { ["Texture"] = mr.Material.TextureName };

                if (mr.Material.TextureTiling != Vector2.One)
                {
                    materialData["TextureTiling"] = mr.Material.TextureTiling;
                }

                if (mr.Material.EmissiveColor != Vector3.Zero)
                {
                    materialData["EmissiveColor"] = mr.Material.EmissiveColor;
                }

                if (Math.Abs(mr.Material.SpecularIntensity - 0.5f) > 0.001f)
                {
                    materialData["SpecularIntensity"] = mr.Material.SpecularIntensity;
                }

                if (Math.Abs(mr.Material.Shininess - 32.0f) > 0.001f)
                {
                    materialData["Shininess"] = mr.Material.Shininess;
                }

                meshRendererData["Material"] = materialData;
                componentsData["MeshRenderer"] = meshRendererData;
            }
            else // All other components can be serialized generically using reflection.
            {
                componentsData[component.GetType().Name] = SerializeComponentProperties(component);
            }
        }
        return componentsData;
    }

    private static void GetHierarchy(GameObject go, List<GameObject> list)
    {
        list.Add(go);

        foreach (Transform child in go.Transform.Children)
        {
            GetHierarchy(child.GameObject, list);
        }
    }

    private static Dictionary<string, object> SerializeComponentProperties(Component component)
    {
        Dictionary<string, object> propertiesData = [];
        var properties = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<HideInInspectorAttribute>() is null);

        // This handles components like Camera that have no public properties to serialize.
        if (!properties.Any())
        {
            return [];
        }

        foreach (PropertyInfo prop in properties)
        {
            // Skip default values for cleaner YAML output.
            // This is an optional step but preserves the behavior of the old serializer.
            // A new component instance is created to get default values.
            try
            {
                var defaultComponent = Activator.CreateInstance(component.GetType());
                object defaultValue = prop.GetValue(defaultComponent);
                object currentValue = prop.GetValue(component);

                if (currentValue is not null && !currentValue.Equals(defaultValue))
                {
                    propertiesData[prop.Name] = currentValue;
                }
            }
            catch
            {
                // If a component doesn't have a parameterless constructor, just serialize all properties.
                propertiesData[prop.Name] = prop.GetValue(component);
            }
        }

        return propertiesData;
    }
}