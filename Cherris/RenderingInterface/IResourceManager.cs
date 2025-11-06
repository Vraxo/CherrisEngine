using Cherris.Components;

namespace Cherris.Rendering;

public interface IResourceManager : IDisposable
{
    void LoadInitialAssets();
    Mesh GetMesh(string name);
    ITexture GetTexture(string name);
    Skybox GetSkybox(string name);
}
