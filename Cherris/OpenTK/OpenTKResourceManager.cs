using System;
using Cherris.Rendering;

namespace Cherris;

public class OpenTKResourceManager : IResourceManager
{
    public void LoadInitialAssets()
    {
        Console.WriteLine("[OpenTKResourceManager] Stub: LoadInitialAssets");
    }

    public Mesh GetMesh(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetMesh '{name}'");
        return null;
    }

    public ITexture GetTexture(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetTexture '{name}'");
        return null;
    }

    public Skybox GetSkybox(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetSkybox '{name}'");
        return null;
    }

    public void Dispose()
    {
    }
}