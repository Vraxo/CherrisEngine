using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.Shadows;
using Overcore.Terrain;
using Overcore.Utils;
using TerrainComponent = Overcore.Components.Terrain;
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
            .WithTypeConverter(new Vector4YamlTypeConverter())
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

            Dictionary<string, object> componentsData = SerializeGameObjectComponents(gameObject, fileIds);
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

    private static Dictionary<string, object> SerializeGameObjectComponents(
        GameObject gameObject,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null)
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

            // A terrain's mesh renderer is the terrain's own doing, rebuilt from its data on load; writing
            // it would add a block naming a mesh no resource manager holds, for the loader to drop.
            if (component is MeshRenderer { MeshName: "Terrain" }
                && gameObject.GetComponent<TerrainComponent>() is not null)
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

                // A list, which is the one shape the generic path cannot compare: its "does this differ from a fresh
                // instance" test is object.Equals, and a float[] is compared by reference, so a clean list would be
                // written on every save and a hand edit would be written indistinguishably from a default.
                Overcore.Rendering.Shadows.Shadows shadows => SerializeShadows(shadows, prefabLocalIds),

                ParticleSystem particleSystem => SerializeParticleSystem(particleSystem, prefabLocalIds),

                // Bulk ground data (heights, alphamap, layers) travels as base64 float bytes beside the
                // scalar settings, which the generic path cannot express.
                TerrainComponent terrain => SerializeTerrain(terrain, prefabLocalIds),

                // All other components can be serialized generically using reflection.
                _ => SerializeComponentProperties(component, prefabLocalIds)
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

    /// <summary>
    /// A <see cref="Shadows"/> component: the scalars, and its split list only when a hand edit has made it differ
    /// from the one <see cref="Shadows.Cascades"/> derives.
    /// </summary>
    /// <remarks>
    /// <see cref="Shadows.Splits"/> is written as a plain float array, which the YAML serializer turns into a flow
    /// sequence, and `ComponentDeserializer.TryConvertFromList` converts a YAML sequence of numbers into a `float[]`
    /// on the way back in. What it must not do is write the list unconditionally: every derived list would then appear
    /// in every scene file, which makes a default look like a decision and hides the one case the list exists for.
    /// </remarks>
    private static Dictionary<string, object> SerializeShadows(
        Overcore.Rendering.Shadows.Shadows shadows,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null)
    {
        Dictionary<string, object> data = SerializeComponentProperties(shadows, prefabLocalIds);

        float[] derived = CascadeSplits.Derive(shadows.Cascades);
        float[] stored = shadows.Splits;

        bool same = stored is not null
            && stored.Length == derived.Length
            && stored.SequenceEqual(derived);

        if (same || stored is null || stored.Length == 0)
        {
            // A one-cascade component has no boundaries to record, so its (empty) list says nothing either.
            data.Remove(nameof(Shadows.Splits));
        }
        else
        {
            data[nameof(Shadows.Splits)] = stored;
        }

        return data;
    }

    private static Dictionary<string, object> SerializeParticleSystem(
        ParticleSystem ps,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null)
    {
        Dictionary<string, object> data = SerializeComponentProperties(ps, prefabLocalIds);

        if (ps.Bursts.Count > 0)
        {
            List<Dictionary<string, object>> burstsData = [];
            foreach (ParticleBurst b in ps.Bursts)
            {
                burstsData.Add(new Dictionary<string, object>
                {
                    ["Time"] = b.Time,
                    ["MinCount"] = b.MinCount,
                    ["MaxCount"] = b.MaxCount,
                    ["CycleCount"] = b.CycleCount,
                    ["RepeatInterval"] = b.RepeatInterval,
                    ["Probability"] = b.Probability
                });
            }

            data["Bursts"] = burstsData;
        }

        return data;
    }

    private static Dictionary<string, object> SerializeTerrain(
        TerrainComponent terrain,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null)
    {
        Dictionary<string, object> data = SerializeComponentProperties(terrain, prefabLocalIds);

        // The material is resolved from the first layer's texture on load; writing the object itself would
        // hand the YAML writer a texture handle to spell.
        data.Remove(nameof(TerrainComponent.Material));

        TerrainData terrainData = terrain.TerrainData;
        data["HeightmapResolution"] = terrainData.HeightmapResolution;
        data["AlphamapResolution"] = terrainData.AlphamapResolution;
        data[nameof(TerrainComponent.Size)] = terrainData.Size;

        List<Dictionary<string, object>> layersData = [];
        foreach (TerrainLayer layer in terrainData.Layers)
        {
            layersData.Add(new Dictionary<string, object>
            {
                ["Name"] = layer.Name,
                ["TextureName"] = layer.TextureName,
                ["Tint"] = layer.Tint,
                ["TileSize"] = layer.TileSize
            });
        }

        data["Layers"] = layersData;
        data["Heights"] = EncodeFloats(terrainData.GetHeightmap());
        data["Alphamap"] = EncodeFloats(terrainData.GetAlphamap());

        // Holes only when there are any: an uncut ground's file says nothing about them.
        if (terrainData.HoleCount > 0)
        {
            data["Holes"] = Convert.ToBase64String(terrainData.GetHolesPacked());
        }

        return data;
    }

    /// <summary>A float array as base64 little-endian bytes: the compact coat bulk ground data wears in YAML.</summary>
    private static string EncodeFloats(float[] floats)
    {
        byte[] bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
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

        // Written only when set, like every other property here. A material with no cutoff is the common case, and
        // `AlphaCutoff: 0` in every block of every scene would read like a decision to be opaque rather than like the
        // absence of one.
        if (mr.Material.AlphaCutoff > 0f)
        {
            materialData["AlphaCutoff"] = mr.Material.AlphaCutoff;
        }

        meshRendererData["Material"] = materialData;

        // Written only when not the default, like every other property here. A renderer that casts and receives is
        // the common case, and saying so in every line of every scene is noise in a file people read by hand.
        if (mr.CastShadows != ShadowCastingMode.On)
        {
            meshRendererData["CastShadows"] = mr.CastShadows;
        }

        if (!mr.ReceiveShadows)
        {
            meshRendererData["ReceiveShadows"] = false;
        }

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

    private static Dictionary<string, object> SerializeComponentProperties(
        Component component,
        IReadOnlyDictionary<GameObject, int>? prefabLocalIds = null)
    {
        Dictionary<string, object> propertiesData = [];
        var properties = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead
                && p.CanWrite
                && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<HideInInspectorAttribute>() is null);

        // This handles components like Camera that have no public properties to serialize.
        if (!properties.Any())
        {
            return [];
        }

        object? defaultComponent = null;
        try
        {
            defaultComponent = Activator.CreateInstance(component.GetType());
        }
        catch
        {
            // No parameterless constructor: serialize non-null values as-is.
        }

        foreach (PropertyInfo prop in properties)
        {
            object? currentValue = prop.GetValue(component);

            if (ObjectReferenceResolver.IsReferenceType(prop.PropertyType))
            {
                Dictionary<string, object>? serializedRef = ObjectReferenceResolver.ToYamlDictionary(
                    currentValue,
                    prefabLocalIds,
                    ProjectFiles.ProjectRoot);

                if (serializedRef is not null)
                {
                    propertiesData[prop.Name] = serializedRef;
                }

                continue;
            }

            if (prop.PropertyType == typeof(LayerMask))
            {
                LayerMask currentMask = currentValue is LayerMask lm ? lm : default;
                LayerMask defaultMask = defaultComponent is not null && prop.GetValue(defaultComponent) is LayerMask dm
                    ? dm
                    : default;

                if (defaultComponent is null || currentMask != defaultMask)
                {
                    propertiesData[prop.Name] = currentMask.Value;
                }

                continue;
            }

            // Skip default values for cleaner YAML output.
            if (defaultComponent is not null)
            {
                object? defaultValue = prop.GetValue(defaultComponent);
                if (currentValue is not null && !currentValue.Equals(defaultValue))
                {
                    propertiesData[prop.Name] = currentValue;
                }
            }
            else if (currentValue is not null)
            {
                propertiesData[prop.Name] = currentValue;
            }
        }

        return propertiesData;
    }
}