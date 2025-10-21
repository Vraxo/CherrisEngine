using System;
using System.Collections.Generic;
using Cherris.Rendering;

namespace Cherris;

public class OpenTKResourceManager : IResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new();

    public void LoadInitialAssets()
    {
        Console.WriteLine("[OpenTKResourceManager] Initial assets loaded.");
        var cubeMesh = Mesh.CreateCube();
        _meshes.Add("Cube", cubeMesh);

        var planeMesh = Mesh.CreatePlane(20f);
        _meshes.Add("Plane", planeMesh);
    }

    public Mesh GetMesh(string name)
    {
        return _meshes.TryGetValue(name, out var mesh) ? mesh : null;
    }

    public ITexture GetTexture(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetTexture '{name}'");
        return null; // Stubbed for now
    }

    public Skybox GetSkybox(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetSkybox '{name}'");
        return null; // Stubbed for now
    }

    public void Dispose()
    {
    }
}