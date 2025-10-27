using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;

namespace Cherris;

public class OpenTKTexture : ITexture
{
    public int Handle { get; }
    private readonly TextureTarget _target;

    public OpenTKTexture(int handle, TextureTarget target = TextureTarget.Texture2D)
    {
        Handle = handle;
        _target = target;
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(_target, Handle);
    }

    public object GetBackendHandle() => Handle;

    public void Dispose()
    {
        GL.DeleteTexture(Handle);
    }
}
