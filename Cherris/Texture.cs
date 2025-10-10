using System;
using Veldrid;

namespace Cherris;

public class Texture : IDisposable
{
    public Veldrid.Texture VeldridTexture { get; }
    public TextureView VeldridTextureView { get; }

    public Texture(Veldrid.Texture texture, TextureView textureView)
    {
        VeldridTexture = texture;
        VeldridTextureView = textureView;
    }

    public void Dispose()
    {
        VeldridTextureView.Dispose();
        VeldridTexture.Dispose();
    }
}