using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Rendering;
using Overcore.Rendering.Shadows;
using Overcore.Utils;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace Overcore.Serialization;

public static class ComponentDeserializer
{
    public static void EnsureRegistered(SceneLoader sceneLoader, IResourceManager resourceManager)
    {
        RegisterFactories(sceneLoader, resourceManager);
    }

    public static void RegisterFactories(SceneLoader sceneLoader, IResourceManager resourceManager)
    {
        sceneLoader.RegisterComponentFactory("MeshRenderer", props => CreateMeshRendererComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("Camera", CreateAndPopulateComponent<Camera>);
        sceneLoader.RegisterComponentFactory("Canvas", CreateAndPopulateComponent<Canvas>);
        sceneLoader.RegisterComponentFactory("RectTransform", CreateAndPopulateComponent<RectTransform>);
        sceneLoader.RegisterComponentFactory("RectMask2D", CreateAndPopulateComponent<RectMask2D>);
        sceneLoader.RegisterComponentFactory("HorizontalLayoutGroup", CreateAndPopulateComponent<HorizontalLayoutGroup>);
        sceneLoader.RegisterComponentFactory("VerticalLayoutGroup", CreateAndPopulateComponent<VerticalLayoutGroup>);
        sceneLoader.RegisterComponentFactory("Image", CreateAndPopulateComponent<Image>);
        sceneLoader.RegisterComponentFactory("Text", CreateAndPopulateComponent<Text>);
        sceneLoader.RegisterComponentFactory("Button", CreateAndPopulateComponent<Button>);
        sceneLoader.RegisterComponentFactory("Toggle", CreateAndPopulateComponent<Toggle>);
        sceneLoader.RegisterComponentFactory("Slider", CreateAndPopulateComponent<Slider>);
        sceneLoader.RegisterComponentFactory("InputField", CreateAndPopulateComponent<InputField>);
        sceneLoader.RegisterComponentFactory("Skybox", props => CreateSkyboxComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("Light", CreateAndPopulateComponent<Light>);

        // Registered rather than left to a root-level block in the scene file: SceneLoader parses the document as
        // Dictionary<string, List<...>> and looks up "GameObjects", so a sibling mapping would fail the whole load.
        sceneLoader.RegisterComponentFactory("Shadows", props => CreateShadowsComponent(props));
        sceneLoader.RegisterComponentFactory("RigidBody", CreateAndPopulateComponent<RigidBody>);
        sceneLoader.RegisterComponentFactory("CharacterController", CreateAndPopulateComponent<CharacterController>);
        sceneLoader.RegisterComponentFactory("BoxCollider", CreateAndPopulateComponent<BoxCollider>);
        sceneLoader.RegisterComponentFactory("SphereCollider", CreateAndPopulateComponent<SphereCollider>);
        sceneLoader.RegisterComponentFactory("CapsuleCollider", CreateAndPopulateComponent<CapsuleCollider>);
        sceneLoader.RegisterComponentFactory("LanguageScript", _ => new LanguageScript());
        sceneLoader.RegisterComponentFactory("AudioSource", props => CreateAudioSourceComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("AudioListener", CreateAndPopulateComponent<AudioListener>);
        sceneLoader.RegisterComponentFactory("ParticleSystem", props => CreateParticleSystemComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("NavMeshSurface", CreateAndPopulateComponent<NavMeshSurface>);
        sceneLoader.RegisterComponentFactory("NavMeshAgent", CreateAndPopulateComponent<NavMeshAgent>);
        sceneLoader.RegisterComponentFactory("NavMeshObstacle", CreateAndPopulateComponent<NavMeshObstacle>);
    }

    private static MeshRenderer? CreateMeshRendererComponent(object properties, IResourceManager resourceManager)
    {
        if (properties is not Dictionary<object, object> propsDict)
        {
            return null;
        }

        propsDict.TryGetValue("Mesh", out var meshNameObj);
        string? meshName = meshNameObj as string;

        Mesh? mesh = resourceManager.GetMesh(meshName);

        if (mesh is null)
        {
            return null;
        }

        Material material = CreateMaterialFromProperties(propsDict, resourceManager);

        MeshRenderer renderer = new()
        {
            Mesh = mesh,
            Material = material,
            MeshName = meshName
        };

        // Mesh and Material come from nested blocks that the generic path cannot express, so the rest of the component
        // is populated here. Without this a scene's cast/receive choice would serialise and then be dropped on load —
        // the file would say one thing and the running scene would do another.
        //
        // The two exclusions are not a optimisation: the generic path would overwrite the material just built with a
        // null from a block it cannot parse, and would try to convert a mesh name into a Mesh.
        PopulateComponentProperties(renderer, propsDict, nameof(MeshRenderer.Material), nameof(MeshRenderer.Mesh));

        return renderer;
    }

    private static AudioSource CreateAudioSourceComponent(object properties, IResourceManager resourceManager)
    {
        AudioSource audioSource = new();

        if (properties is not Dictionary<object, object> propsDict)
        {
            return audioSource;
        }

        PopulateComponentProperties(audioSource, propsDict);

        if (propsDict.TryGetValue("ClipName", out var clipNameObj) && clipNameObj is string clipName && !string.IsNullOrEmpty(clipName))
        {
            audioSource.Clip = resourceManager.GetAudioClip(clipName);
        }

        return audioSource;
    }

    private static ParticleSystem CreateParticleSystemComponent(object properties, IResourceManager resourceManager)
    {
        ParticleSystem ps = new();

        if (properties is not Dictionary<object, object> propsDict)
        {
            return ps;
        }

        PopulateComponentProperties(ps, propsDict, "Bursts");

        if (!string.IsNullOrWhiteSpace(ps.TextureName) && resourceManager is not null)
        {
            ps.Texture = resourceManager.GetTexture(ps.TextureName);
        }

        if (propsDict.TryGetValue("Bursts", out var burstsObj) && burstsObj is List<object> burstList)
        {
            List<ParticleBurst> bursts = [];
            foreach (object item in burstList)
            {
                if (item is Dictionary<object, object> bDict)
                {
                    float time = GetFloat(bDict, "Time", 0f);
                    int minCount = bDict.TryGetValue("MinCount", out var minObj)
                        ? Convert.ToInt32(minObj, CultureInfo.InvariantCulture)
                        : 10;
                    int maxCount = bDict.TryGetValue("MaxCount", out var maxObj)
                        ? Convert.ToInt32(maxObj, CultureInfo.InvariantCulture)
                        : minCount;
                    int cycleCount = bDict.TryGetValue("CycleCount", out var cycObj)
                        ? Convert.ToInt32(cycObj, CultureInfo.InvariantCulture)
                        : 1;
                    float repeatInterval = GetFloat(bDict, "RepeatInterval", 0.01f);
                    float probability = GetFloat(bDict, "Probability", 1f);

                    bursts.Add(new ParticleBurst(time, minCount, maxCount, cycleCount, repeatInterval, probability));
                }
            }

            ps.SetBursts(bursts);
        }

        return ps;
    }

    private static Material CreateMaterialFromProperties(IReadOnlyDictionary<object, object> componentProps, IResourceManager resourceManager)
    {
        _ = componentProps.TryGetValue("Material", out var materialObj);
        var matProps = materialObj as Dictionary<object, object> ?? (Dictionary<object, object>)componentProps;

        _ = matProps.TryGetValue("Texture", out var textureNameObj);
        var textureName = textureNameObj as string ?? "White";
        var texture = resourceManager.GetTexture(textureName);

        var material = new Material(texture, textureName)
        {
            TextureTiling = GetVector2(matProps, "TextureTiling", Vector2.One),
            EmissiveColor = GetVector3(matProps, "EmissiveColor", Vector3.Zero),
            SpecularIntensity = GetFloat(matProps, "SpecularIntensity", 0.5f),
            Shininess = GetFloat(matProps, "Shininess", 32.0f),
            AlphaCutoff = GetFloat(matProps, "AlphaCutoff", 0f)
        };

        return material;
    }

    private static Skybox? CreateSkyboxComponent(object properties, IResourceManager resourceManager)
    {
        return properties is Dictionary<object, object> propsDict &&
            propsDict.TryGetValue("CubeMap", out var cubemapNameObj) &&
            cubemapNameObj is string cubemapName
            ? resourceManager.GetSkybox(cubemapName)
            : null;
    }

    private static T CreateAndPopulateComponent<T>(object properties) where T : Component, new()
    {
        var component = new T();
        PopulateComponentProperties(component, properties as Dictionary<object, object>);
        return component;
    }

    /// <summary>
    /// A <see cref="Shadows"/> component, with <see cref="Shadows.Cascades"/> applied before
    /// <see cref="Shadows.Splits"/>.
    /// </summary>
    /// <remarks>
    /// The order is not a detail. The <c>Cascades</c> setter re-derives <c>Splits</c> — which is what makes a fresh
    /// instance compare equal to a loaded one — so a hand-tuned split applied first would be overwritten by the count,
    /// and the file would load with the split the author did not choose. The property order in a YAML mapping is
    /// whatever the writer emitted, so the load path states the dependency rather than trusting the file to.
    /// </remarks>
    private static Shadows CreateShadowsComponent(object properties)
    {
        Shadows component = new();

        if (properties is not Dictionary<object, object> propsDict)
        {
            return component;
        }

        PopulateComponentProperties(component, propsDict, nameof(Shadows.Splits));
        PopulateComponentProperties(component, propsDict);

        return component;
    }

    private static void PopulateComponentProperties(
        Component component,
        IReadOnlyDictionary<object, object> propsDict,
        params string[] skip)
    {
        if (propsDict is null)
        {
            return;
        }

        var componentType = component.GetType();
        foreach (var (key, value) in propsDict)
        {
            if (key is not string propName || skip.Contains(propName))
            {
                continue;
            }

            var propertyInfo = componentType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (propertyInfo is null
                || !IsPropertyDeserializable(propertyInfo)
                || ObjectReferenceResolver.IsReferenceType(propertyInfo.PropertyType))
            {
                continue;
            }

            if (TryConvertValue(value, propertyInfo.PropertyType, out object convertedValue))
            {
                propertyInfo.SetValue(component, convertedValue);
            }
            else
            {
                LogPropertyValueConversionWarning(propName, componentType.Name);
            }
        }
    }

    private static bool IsPropertyDeserializable(PropertyInfo propertyInfo)
    {
        return propertyInfo.CanWrite
            && propertyInfo.GetIndexParameters().Length == 0
            && !propertyInfo.IsDefined(typeof(HideInInspectorAttribute), false);
    }

    internal static bool TryConvertPropertyValue(object yamlValue, Type targetType, out object? convertedValue) =>
        TryConvertValue(yamlValue, targetType, out convertedValue);

    private static bool TryConvertValue(object yamlValue, Type targetType, out object? convertedValue)
    {
        convertedValue = null;
        try
        {
            if (yamlValue is List<object> list)
            {
                return TryConvertFromList(list, targetType, out convertedValue);
            }
            if (targetType == typeof(LayerMask))
            {
                convertedValue = new LayerMask(Convert.ToInt32(yamlValue, CultureInfo.InvariantCulture));
                return true;
            }
            if (targetType.IsEnum)
            {
                convertedValue = Enum.Parse(targetType, yamlValue.ToString()!, true);
                return true;
            }
            convertedValue = Convert.ChangeType(yamlValue, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertFromList(IReadOnlyList<object> list, Type targetType, out object? convertedValue)
    {
        convertedValue = null;
        if (targetType == typeof(Vector2) && list.Count == 2)
        {
            convertedValue = new Vector2(ToSingle(list[0]), ToSingle(list[1]));
            return true;
        }
        if (targetType == typeof(Vector3) && list.Count == 3)
        {
            convertedValue = new Vector3(ToSingle(list[0]), ToSingle(list[1]), ToSingle(list[2]));
            return true;
        }
        if (targetType == typeof(Vector4) && list.Count == 4)
        {
            convertedValue = new Vector4(ToSingle(list[0]), ToSingle(list[1]), ToSingle(list[2]), ToSingle(list[3]));
            return true;
        }

        // A list of floats, of any length: `Shadows.Splits` is `n - 1` numbers whose count is data rather than a
        // type, so it gets a general conversion rather than a third fixed arity. An empty sequence is a legitimate
        // value here — one cascade has no boundaries — and converts to an empty array.
        if (targetType == typeof(float[]) || targetType == typeof(List<float>))
        {
            float[] numbers = new float[list.Count];

            for (int index = 0; index < list.Count; index++)
            {
                numbers[index] = ToSingle(list[index]);
            }

            convertedValue = targetType == typeof(List<float>) ? new List<float>(numbers) : numbers;
            return true;
        }

        return false;
    }

    private static Vector2 GetVector2(IReadOnlyDictionary<object, object> props, string key, Vector2 defaultValue)
    {
        if (props.TryGetValue(key, out var value) && value is List<object> list && list.Count == 2)
        {
            try { return new Vector2(ToSingle(list[0]), ToSingle(list[1])); }
            catch { }
        }
        return defaultValue;
    }

    private static Vector3 GetVector3(IReadOnlyDictionary<object, object> props, string key, Vector3 defaultValue)
    {
        if (props.TryGetValue(key, out var value) && value is List<object> list && list.Count == 3)
        {
            try { return new Vector3(ToSingle(list[0]), ToSingle(list[1]), ToSingle(list[2])); }
            catch { }
        }
        return defaultValue;
    }

    private static float GetFloat(IReadOnlyDictionary<object, object> props, string key, float defaultValue)
    {
        if (!props.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        try
        {
            return ToSingle(value);
        }

        catch { }
        return defaultValue;
    }

    private static float ToSingle(object value)
    {
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static void LogPropertyValueConversionWarning(string propertyName, string componentName)
    {
        Logger.Warning($"[Deserializer] Warning: Could not set property '{propertyName}' on component '{componentName}'.");
    }
}