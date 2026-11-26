using OpenTK.Graphics.OpenGL4;

namespace Overcore.Rendering.OpenTK;

/// <summary>
/// A depth-only render target whose storage is a <c>Texture2DArray</c>, one layer per shadow cascade.
/// </summary>
/// <remarks>
/// <para>
/// Four layers of one texture rather than four textures, and the reason is a language rule rather than taste: GLSL 330
/// lets a sampler array be indexed only by a constant expression, so a receiver that picked its cascade at run time
/// could not write <c>uShadowMaps[cascade]</c>. A <c>sampler2DArray</c> is sampled with an arbitrary layer, which is
/// exactly the operation the receiver needs.
/// </para>
/// <para>
/// There is no colour attachment and <c>glDrawBuffer(GL_NONE)</c> says so. A framebuffer the driver thinks is
/// incomplete draws nothing and raises nothing, which is the failure this type exists to make loud instead.
/// </para>
/// <para>
/// The layers are not sampled through the fixed-function comparison — no <c>sampler2DArrayShadow</c>, no
/// <c>textureProj</c>. The receiver does the comparison itself after an ordinary fetch, because a slope-scaled bias and
/// a PCF tent both have to happen around it, and because a hardware comparison would put the bias on the wrong side of
/// the filter.
/// </para>
/// </remarks>
internal sealed class ShadowDepthTarget : IDisposable
{
    private readonly int _framebuffer;
    private readonly int _texture;
    private bool _disposed;

    /// <param name="resolution">Texels on a side.</param>
    /// <param name="layers">How many cascades the texture holds. At least one.</param>
    public ShadowDepthTarget(int resolution, int layers)
    {
        Resolution = Math.Max(1, resolution);
        Layers = Math.Max(1, layers);

        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, _texture);

        GL.TexImage3D(
            TextureTarget.Texture2DArray,
            0,
            PixelInternalFormat.DepthComponent24,
            Resolution,
            Resolution,
            Layers,
            0,
            PixelFormat.DepthComponent,
            PixelType.UnsignedInt,
            IntPtr.Zero);

        // Clamp in all three dimensions. The default is Repeat, and a repeated shadow map wraps the far side of a
        // cascade onto its near side — a shadow that is present and in the wrong place, which is worse than a missing
        // one to diagnose.
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMaxLevel, 0);

        _framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        // The attachment is per layer, so the layer is what the status check has to be made against. Checking once
        // with no attachment is the way to ship a framebuffer that is incomplete on every draw.
        for (int layer = 0; layer < Layers; layer++)
        {
            GL.FramebufferTextureLayer(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                _texture,
                0,
                layer);

            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                Dispose();

                throw new InvalidOperationException(
                    $"[ShadowDepthTarget] Layer {layer} of {Layers} at {Resolution}x{Resolution} is incomplete: {status}");
            }
        }

        // Depth only. Without this the driver looks for a colour attachment, finds none, and calls the framebuffer
        // incomplete — on the next status check it is asked for rather than on this one.
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    /// <summary>Texels on a side.</summary>
    public int Resolution { get; }

    /// <summary>How many cascades the texture holds.</summary>
    public int Layers { get; }

    /// <summary>The texture handle, for a receiver to sample.</summary>
    public int Texture => _texture;

    /// <summary>
    /// Makes one cascade the render target: binds the framebuffer, attaches that layer and sizes the viewport to the
    /// map. Leaves the framebuffer bound; the caller restores it, because a depth pass that guessed where to return to
    /// is a depth pass that draws into the editor's viewport on some other driver.
    /// </summary>
    public void BindLayer(int layer)
    {
        if (layer < 0 || layer >= Layers)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, $"This target has {Layers} layers.");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.FramebufferTextureLayer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            _texture,
            0,
            layer);

        GL.Viewport(0, 0, Resolution, Resolution);
    }

    /// <summary>
    /// Clears the depth buffer of whatever layer is bound, to the far plane, so the clear value is only ever seen where
    /// nothing was drawn — and there it means "nothing occludes".
    /// </summary>
    public void Clear()
    {
        GL.ClearDepth(1.0);
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        GL.DeleteFramebuffer(_framebuffer);
        GL.DeleteTexture(_texture);
        _disposed = true;
    }
}
