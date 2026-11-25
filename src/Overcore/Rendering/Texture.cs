using Overcore.Rendering;
using Veldrid;

namespace Overcore;

public class Texture : ITexture
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

    public object GetBackendHandle() => this;
}