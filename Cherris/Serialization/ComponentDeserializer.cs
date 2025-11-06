using Cherris.Components;
using Cherris.Rendering;
using System.Globalization;
using System.Numerics;

namespace Cherris;

/// <summary>
/// Handles the registration of component factories used by the SceneLoader.
/// </summary>
public static class ComponentDeserializer
{
    public static void RegisterFactories(SceneLoader sceneLoader, IResourceManager resourceManager)
    {
        sceneLoader.RegisterComponentFactory("MeshRenderer", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;

            // Get Mesh
            if (!propsDict.TryGetValue("Mesh", out var meshNameObj) || meshNameObj is not string meshName) return null;
            Mesh? mesh = resourceManager.GetMesh(meshName);
            if (mesh is null) return null;

            // Get Material properties from a nested dictionary
            if (!propsDict.TryGetValue("Material", out var materialObj) || materialObj is not Dictionary<object, object> matProps)
            {
                // For backward compatibility, check for top-level properties.
                matProps = propsDict;
            }

            // Inside Material dictionary
            string textureName = "White"; // Default value
            if (matProps.TryGetValue("Texture", out var textureNameObj) && textureNameObj is string parsedTextureName)
            {
                textureName = parsedTextureName;
            }
            ITexture texture = resourceManager.GetTexture(textureName);
            var material = new Material(texture, textureName);

            if (matProps.TryGetValue("TextureTiling", out var tilingObj) && tilingObj is List<object> tilingList && tilingList.Count == 2)
            {
                try
                {
                    material.TextureTiling = new Vector2(
                        Convert.ToSingle(tilingList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(tilingList[1], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Deserializer] Warning: Could not parse TextureTiling values. Using default. Error: {e.Message}");
                }
            }

            if (matProps.TryGetValue("EmissiveColor", out var emissiveObj) && emissiveObj is List<object> emissiveList && emissiveList.Count == 3)
            {
                try
                {
                    material.EmissiveColor = new(
                        Convert.ToSingle(emissiveList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[1], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[2], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Deserializer] Warning: Could not parse EmissiveColor values. Using default. Error: {e.Message}");
                }
            }

            if (matProps.TryGetValue("SpecularIntensity", out var specIntObj))
            {
                material.SpecularIntensity = Convert.ToSingle(specIntObj, CultureInfo.InvariantCulture);
            }

            if (matProps.TryGetValue("Shininess", out var shininessObj))
            {
                material.Shininess = Convert.ToSingle(shininessObj, CultureInfo.InvariantCulture);
            }

            return new MeshRenderer(mesh, material, meshName);
        });

        sceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());

        sceneLoader.RegisterComponentFactory("Skybox", (properties) =>
        {
            if (properties is Dictionary<object, object> propsDict &&
                propsDict.TryGetValue("CubeMap", out var cubemapNameObj) &&
                cubemapNameObj is string cubemapName)
            {
                return resourceManager.GetSkybox(cubemapName);
            }
            return null;
        });

        sceneLoader.RegisterComponentFactory("Light", (properties) =>
        {
            var light = new Light();
            if (properties is not Dictionary<object, object> propsDict) return light;

            if (propsDict.TryGetValue("Type", out var typeObj) && Enum.TryParse<LightType>(typeObj as string, out var type))
            {
                light.Type = type;
            }

            if (propsDict.TryGetValue("Color", out var colorObj) && colorObj is List<object> colorList && colorList.Count == 3)
            {
                try
                {
                    light.Color = new Vector3(
                        Convert.ToSingle(colorList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(colorList[1], CultureInfo.InvariantCulture),
                        Convert.ToSingle(colorList[2], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Deserializer] Warning: Could not parse Light Color values. Using default. Error: {e.Message}");
                }
            }

            if (propsDict.TryGetValue("Intensity", out var intensityObj)) light.Intensity = Convert.ToSingle(intensityObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("AmbientStrength", out var ambientObj)) light.AmbientStrength = Convert.ToSingle(ambientObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("Range", out var rangeObj)) light.Range = Convert.ToSingle(rangeObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("InnerConeAngle", out var innerAngleObj)) light.InnerConeAngle = Convert.ToSingle(innerAngleObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("OuterConeAngle", out var outerAngleObj)) light.OuterConeAngle = Convert.ToSingle(outerAngleObj, CultureInfo.InvariantCulture);

            return light;
        });

        sceneLoader.RegisterComponentFactory("RigidBody", (properties) =>
        {
            var rb = new RigidBody();
            if (properties is not Dictionary<object, object> propsDict) return rb;

            if (propsDict.TryGetValue("IsStatic", out var isStaticObj)) rb.IsStatic = Convert.ToBoolean(isStaticObj);
            if (propsDict.TryGetValue("Mass", out var massObj) && !rb.IsStatic) rb.Mass = Convert.ToSingle(massObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("Friction", out var frictionObj)) rb.Friction = Convert.ToSingle(frictionObj, CultureInfo.InvariantCulture);
            if (propsDict.TryGetValue("Bounciness", out var bouncinessObj)) rb.Bounciness = Convert.ToSingle(bouncinessObj, CultureInfo.InvariantCulture);

            return rb;
        });
    }
}