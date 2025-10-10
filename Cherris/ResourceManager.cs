using System.Collections.Generic;
using Veldrid;

namespace Cherris;

public class ResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
    private readonly GraphicsDevice _graphicsDevice;

    public ResourceManager(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
    }

    public void LoadInitialAssets()
    {
        // In a real engine, this would load from files (e.g. .obj, .fbx)
        // For now, we pre-load our procedural cube.
        var cubeMesh = Mesh.CreateCube();
        _meshes.Add("Cube", cubeMesh);

        var planeMesh = Mesh.CreatePlane(20f);
        _meshes.Add("Plane", planeMesh);
    }

    public Mesh GetMesh(string name)
    {
        return _meshes.TryGetValue(name, out var mesh) ? mesh : null;
    }

    public void Dispose()
    {
        // Meshes don't have GPU resources, so nothing to dispose here yet.
        // If we had textures, etc., we would dispose them here.
    }
}