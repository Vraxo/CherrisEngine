using OpenTK.Graphics.OpenGL4;

namespace Cherris.Rendering.OpenTK;

internal sealed class Framebuffer : IDisposable
{
    public int Handle { get; }
    public int ColorTexture { get; }
    public int? DepthStencilRenderbuffer { get; }

    public Framebuffer(int handle, int colorTexture, int? depthStencilRenderbuffer = null)
    {
        Handle = handle;
        ColorTexture = colorTexture;
        DepthStencilRenderbuffer = depthStencilRenderbuffer;
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(Handle);
        GL.DeleteTexture(ColorTexture);

        if (DepthStencilRenderbuffer.HasValue)
        {
            GL.DeleteRenderbuffer(DepthStencilRenderbuffer.Value);
        }

        GC.SuppressFinalize(this);
    }
}