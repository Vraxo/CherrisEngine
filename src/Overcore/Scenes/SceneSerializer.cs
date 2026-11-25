using Overcore.Components;
using Overcore.Core;
using Overcore.Utils;
using System.Numerics;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Overcore.Serialization;

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

    /// <summary>Writes one subtree as a prefab asset. See <see cref="SavePrefab(IEnumerable{GameObject}, string)"/>.</summary>
    /// <param name="rootGo">The subtree's root.</param>
    /// <param name="filePath">Where the asset is written.</param>
    /// <returns>The written objects, root first.</returns>
    public List<GameObject> SavePrefab(GameObject rootGo, string filePath) =>
        SavePrefab([rootGo], filePath);

    /// <summary>
    /// Writes objects as a prefab asset and stamps every one of them with its link to it. File ids are
    /// stable: an object already linked to this asset keeps its id for ever, and only objects new to the
    /// asset are numbered — so applying an override never renumbers the asset out from under an instance.
    /// </summary>
    /// <param name="roots">The subtrees to write; a prefab usually has exactly one.</param>
    /// <param name="filePath">Where the asset is written.</param>
    /// <returns>The written objects, roots first, in the file's order.</returns>
    public List<GameObject> SavePrefab(IEnumerable<GameObject> roots, string filePath)
    {
        List<GameObject> hierarchy = [];

        foreach (GameObject rootGo in roots)
        {
            GetHierarchy(rootGo, hierarchy);
        }

        string guid = AssetMeta.GetOrCreate(filePath);
        Dictionary<GameObject, int> fileIds = [];
        HashSet<int> taken = [];

        // Ids the asset already gave out are kept; a second object claiming the same one is treated as new.
        foreach (GameObject gameObject in hierarchy)
        {
            if (gameObject.PrefabSource is { } source
                && source.AssetGuid == guid
                && source.FileId > 0
                && taken.Add(source.FileId))
            {
                fileIds[gameObject] = source.FileId;
            }
        }

        int next = 1;

        foreach (GameObject gameObject in hierarchy)
        {
            if (!fileIds.ContainsKey(gameObject))
            {
                while (!taken.Add(next))
                {
                    next++;
                }

                fileIds[gameObject] = next;
            }
        }

        List<Dictionary<string, object>> gameObjectsData = [];

        foreach (GameObject gameObject in hierarchy)
        {
            gameObject.PrefabSource = new PrefabSource(guid, fileIds[gameObject]);

            Dictionary<string, object> goData = new()
            {
                ["Id"] = gameObject.Id.ToString(),
                ["Name"] = gameObject.Name,
                ["File"] = fileIds[gameObject]
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

        return hierarchy;
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

            // An object of a prefab instance says where in which asset it lives, so the link survives a save.
            if (gameObject.PrefabSource is { } source)
            {
                goData["Prefab"] = new Dictionary<string, object>
                {
                    ["Asset"] = source.AssetGuid,
                    ["File"] = source.FileId
                };
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

            Dictionary<string, object> data = component switch
            {
                // MeshRenderer requires special handling for its nested Material object.
                MeshRenderer mr => SerializeMeshRenderer(mr),

                // Named properties rather than reflection, and only the script's own: the editor's hold on it
                // (Script.IsSuspended) is never written, so it cannot disable the script in a saved scene or a
                // play-mode snapshot. The author's toggle is written below, with everyone else's.
                LanguageScript languageScript => new Dictionary<string, object>
                {
                    [nameof(LanguageScript.Language)] = languageScript.Language,
                    [nameof(LanguageScript.SourcePath)] = languageScript.SourcePath
                },

                // All other components can be serialized generically using reflection.
                _ => SerializeComponentProperties(component)
            };

            // The loader reads the enable toggle back for every component, whatever its factory (SceneLoader),
            // so it is written here once for all of them. Like every other default, "on" is left out.
            if (!component.Enabled)
            {
                data["Enabled"] = false;
            }

            componentsData[component.GetType().Name] = data;
        }
        return componentsData;
    }

    private static Dictionary<string, object> SerializeMeshRenderer(MeshRenderer mr)
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
        return meshRendererData;
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