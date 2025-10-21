using Cherris.Rendering;

namespace Cherris;

public class Skybox : Component
{
    public ITexture CubeMapTexture { get; }

    public Skybox(ITexture cubeMapTexture)
    {
        CubeMapTexture = cubeMapTexture;
    }

    public void Dispose()
    {
        CubeMapTexture.Dispose();
    }
}