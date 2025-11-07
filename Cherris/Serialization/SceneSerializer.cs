using Cherris.Components;
using Cherris.Core;
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
            Dictionary<string, object> goData = new Dictionary<string, object>
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

        string yaml = _serializer.Serialize(root);
        File.WriteAllText(filePath, yaml);
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
            if (component.GetType().Name == "EditorController")
            {
                continue;
            }

            switch (component)
            {
                case MeshRenderer mr:
                    Dictionary<string, object> meshRendererData = new()
                    { 
                        ["Mesh"] = mr.MeshName 
                    };

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

                    break;

                case Camera:
                    componentsData["Camera"] = new Dictionary<string, object>();
                    break;

                case Skybox skybox:
                    componentsData["Skybox"] = new Dictionary<string, object> { ["CubeMap"] = skybox.CubeMapName };
                    break;

                case Light light:
                    Dictionary<string, object> lightData = new()
                    {
                        ["Type"] = light.Type.ToString(),
                        ["Color"] = light.Color,
                        ["Intensity"] = light.Intensity
                    };

                    if (Math.Abs(light.AmbientStrength - 0.3f) > 0.001f)
                    {
                        lightData["AmbientStrength"] = light.AmbientStrength;
                    }

                    if (light.Type is LightType.Point or LightType.Spot)
                    {
                        if (Math.Abs(light.Range - 50.0f) > 0.001f) lightData["Range"] = light.Range;
                    }

                    if (light.Type == LightType.Spot)
                    {
                        if (float.Abs(light.InnerConeAngle - 12.5f) > 0.001f)
                        {
                            lightData["InnerConeAngle"] = light.InnerConeAngle;
                        }

                        if (float.Abs(light.OuterConeAngle - 17.5f) > 0.001f)
                        {
                            lightData["OuterConeAngle"] = light.OuterConeAngle;
                        }
                    }

                    componentsData["Light"] = lightData;

                    break;

                case RigidBody rb:
                    Dictionary<string, object> rigidBodyData = new() 
                    { 
                        ["IsStatic"] = rb.IsStatic 
                    };
                    
                    if (rb.Shape != ColliderType.Box)
                    {
                        rigidBodyData["Shape"] = rb.Shape.ToString();
                    }

                    if (!rb.IsStatic)
                    {
                        rigidBodyData["Mass"] = rb.Mass;
                    }

                    if (rb.Friction != 0.5f)
                    {
                        rigidBodyData["Friction"] = rb.Friction;
                    }

                    if (rb.Bounciness != 0.5f)
                    {
                        rigidBodyData["Bounciness"] = rb.Bounciness;
                    }

                    componentsData["RigidBody"] = rigidBodyData;

                    break;

                case Script script:
                    componentsData[script.GetType().Name] = SerializeScriptProperties(script);
                    break;
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

    private static Dictionary<string, object> SerializeScriptProperties(Script script)
    {
        Dictionary<string, object> propertiesData = [];
        IEnumerable<PropertyInfo> properties = script.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<HideInInspectorAttribute>() is null);

        foreach (PropertyInfo prop in properties)
        {
            propertiesData[prop.Name] = prop.GetValue(script);
        }

        return propertiesData;
    }
}