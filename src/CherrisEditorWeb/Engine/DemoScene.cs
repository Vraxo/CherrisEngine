using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using System.Numerics;

namespace CherrisEditorWeb;

/// <summary>
/// A tiny built-in scene (camera, directional light, floor, spinning cube) so the host has something to
/// render before it can load user projects.
/// </summary>
public static class DemoScene
{
    /// <summary>Builds the demo scene using the engine's built-in meshes and textures.</summary>
    public static Scene Create(IResourceManager resourceManager)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        List<GameObject> objects = [];

        GameObject cameraObject = new("Camera");
        cameraObject.Transform.Position = new Vector3(0f, 2.2f, 6.5f);
        cameraObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.2f);
        Camera camera = cameraObject.AddComponent(new Camera());
        objects.Add(cameraObject);

        GameObject lightObject = new("Sun");
        lightObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.9f, -0.8f, 0f);
        lightObject.AddComponent(new Light
        {
            Type = LightType.Directional,
            Intensity = 1.1f,
            AmbientStrength = 0.35f
        });
        objects.Add(lightObject);

        // Each renderer gets its own material so per-renderer backend data cannot be shared by accident.
        Material floorMaterial = new(resourceManager.GetTexture("White"), "White") { SpecularIntensity = 0.1f };
        Material cubeMaterial = new(resourceManager.GetTexture("White"), "White") { SpecularIntensity = 0.6f };

        GameObject floorObject = new("Floor");
        floorObject.AddComponent(new MeshRenderer
        {
            Mesh = resourceManager.GetMesh("Plane") ?? Mesh.CreatePlane(20f),
            MeshName = "Plane",
            Material = floorMaterial
        });
        objects.Add(floorObject);

        GameObject cubeObject = new("Cube");
        cubeObject.Transform.Position = new Vector3(0f, 0.75f, 0f);
        cubeObject.Transform.Scale = new Vector3(1.5f);
        cubeObject.AddComponent(new MeshRenderer
        {
            Mesh = resourceManager.GetMesh("Cube") ?? Mesh.CreateCube(),
            MeshName = "Cube",
            Material = cubeMaterial
        });
        objects.Add(cubeObject);

        return new Scene(string.Empty, objects) { MainCamera = camera };
    }
}
