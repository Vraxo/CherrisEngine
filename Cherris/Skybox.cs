using Veldrid;

namespace Cherris;

public class Skybox : Component
{
    public Texture CubeMapTexture { get; }

    public Skybox(Texture cubeMapTexture)
    {
        CubeMapTexture = cubeMapTexture;
    }

    public void Dispose()
    {
        CubeMapTexture.Dispose();
    }
}