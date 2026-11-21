using Cherris.Rendering;

namespace Cherris.Components;

public class Skybox : Component
{
    public ITexture CubeMapTexture { get; }
    public string CubeMapName { get; }

    public Skybox(ITexture cubeMapTexture, string cubeMapName)
    {
        CubeMapTexture = cubeMapTexture;
        CubeMapName = cubeMapName;
    }

    public void Dispose()
    {
        CubeMapTexture.Dispose();
    }
}