using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace Cherris;

public static class ComponentDeserializer
{
    public static void RegisterFactories(SceneLoader sceneLoader, IResourceManager resourceManager)
    {
        sceneLoader.RegisterComponentFactory("MeshRenderer", props => CreateMeshRendererComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("Camera", CreateAndPopulateComponent<Camera>);
        sceneLoader.RegisterComponentFactory("Skybox", props => CreateSkyboxComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("Light", CreateAndPopulateComponent<Light>);
        sceneLoader.RegisterComponentFactory("RigidBody", CreateAndPopulateComponent<RigidBody>);
        sceneLoader.RegisterComponentFactory("AudioSource", props => CreateAudioSourceComponent(props, resourceManager));
        sceneLoader.RegisterComponentFactory("AudioListener", CreateAndPopulateComponent<AudioListener>);
    }

    private static MeshRenderer CreateMeshRendererComponent(object properties, IResourceManager resourceManager)
    {
        if (properties is not Dictionary<object, object> propsDict) return null;

        propsDict.TryGetValue("Mesh", out var meshNameObj);
        var meshName = meshNameObj as string;

        var mesh = resourceManager.GetMesh(meshName);
        if (mesh is null) return null;

        var material = CreateMaterialFromProperties(propsDict, resourceManager);

        return new MeshRenderer(mesh, material, meshName);
    }

    private static AudioSource CreateAudioSourceComponent(object properties, IResourceManager resourceManager)
    {
        var audioSource = new AudioSource();
        if (properties is not Dictionary<object, object> propsDict) return audioSource;

        PopulateComponentProperties(audioSource, propsDict);

        if (propsDict.TryGetValue("ClipName", out var clipNameObj) && clipNameObj is string clipName && !string.IsNullOrEmpty(clipName))
        {
            audioSource.Clip = resourceManager.GetAudioClip(clipName);
        }

        return audioSource;
    }

    private static Material CreateMaterialFromProperties(IReadOnlyDictionary<object, object> componentProps, IResourceManager resourceManager)
    {
        componentProps.TryGetValue("Material", out var materialObj);
        var matProps = materialObj as Dictionary<object, object> ?? (Dictionary<object, object>)componentProps;

        matProps.TryGetValue("Texture", out var textureNameObj);
        var textureName = textureNameObj as string ?? "White";
        var texture = resourceManager.GetTexture(textureName);

        var material = new Material(texture, textureName)
        {
            TextureTiling = GetVector2(matProps, "TextureTiling", Vector2.One),
            EmissiveColor = GetVector3(matProps, "EmissiveColor", Vector3.Zero),
            SpecularIntensity = GetFloat(matProps, "SpecularIntensity", 0.5f),
            Shininess = GetFloat(matProps, "Shininess", 32.0f)
        };

        return material;
    }

    private static Skybox CreateSkyboxComponent(object properties, IResourceManager resourceManager)
    {
        if (properties is Dictionary<object, object> propsDict &&
            propsDict.TryGetValue("CubeMap", out var cubemapNameObj) &&
            cubemapNameObj is string cubemapName)
        {
            return resourceManager.GetSkybox(cubemapName);
        }
        return null;
    }

    private static T CreateAndPopulateComponent<T>(object properties) where T : Component, new()
    {
        var component = new T();
        PopulateComponentProperties(component, properties as Dictionary<object, object>);
        return component;
    }

    private static void PopulateComponentProperties(Component component, IReadOnlyDictionary<object, object> propsDict)
    {
        if (propsDict is null) return;

        var componentType = component.GetType();
        foreach (var (key, value) in propsDict)
        {
            if (key is not string propName) continue;

            var propertyInfo = componentType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (propertyInfo is null || !IsPropertyDeserializable(propertyInfo)) continue;

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
        return propertyInfo.CanWrite && !propertyInfo.IsDefined(typeof(HideInInspectorAttribute), false);
    }

    private static bool TryConvertValue(object yamlValue, Type targetType, out object convertedValue)
    {
        convertedValue = null;
        try
        {
            if (yamlValue is List<object> list)
            {
                return TryConvertFromList(list, targetType, out convertedValue);
            }
            if (targetType.IsEnum)
            {
                convertedValue = Enum.Parse(targetType, (string)yamlValue, true);
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

    private static bool TryConvertFromList(IReadOnlyList<object> list, Type targetType, out object convertedValue)
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
        Console.WriteLine($"[Deserializer] Warning: Could not set property '{propertyName}' on component '{componentName}'.");
    }
}