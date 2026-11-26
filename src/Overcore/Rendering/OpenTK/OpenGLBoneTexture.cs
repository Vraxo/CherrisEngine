using OpenTK.Graphics.OpenGL4;

namespace Overcore.Rendering.OpenTK;

/// <summary>
/// The bone palette as a GPU texture: one <c>RGBA32F</c> row, three texels per bone.
/// </summary>
/// <remarks>
/// <para>
/// A texture rather than a uniform array because a uniform block's size is driver-limited, and a palette that
/// silently truncates at 64 bones is a bug that shows up on other people's hardware as a character whose arms
/// stop moving. <see cref="Overcore.Animation.BonePalette"/> does the packing; this class owns the handle and the
/// upload, and nothing else, so the arithmetic stays testable without a context.
/// </para>
/// <para>
/// <see cref="Upload"/> is what a renderer calls only when the pose changed: a static prop made of skinned parts, or
/// a paused character, costs no upload at all.
/// </para>
/// </remarks>
internal sealed class OpenGLBoneTexture : IDisposable
{
    private readonly int _handle;
    private int _width;
    private bool _disposed;

    public OpenGLBoneTexture(int textureUnit)
    {
        TextureUnit = textureUnit;

        _handle = GL.GenTexture();
        GL.ActiveTexture(TextureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);

        // Nearest and no mipmaps: the shader fetches exact texels by index, so filtering would blend two bones
        // together and clamp-to-edge would substitute a neighbour for a bone the palette does not have.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    /// <summary>Which unit the texture is bound to, so the draw binds it where the shader expects.</summary>
    public TextureUnit TextureUnit { get; }

    public int Handle => _handle;

    /// <summary>
    /// Rebuilds the texture for a bone count if the count changed, then writes the palette. A resize is not a bug to
    /// hide: a skeleton's bone count is fixed at import, so a change means a different skeleton was bound.
    /// </summary>
    /// <param name="floats">The packed palette, from <see cref="Overcore.Animation.BonePalette.Pack"/>.</param>
    /// <param name="boneCount">How many bones the palette holds.</param>
    public void Upload(float[] floats, int boneCount)
    {
        ArgumentNullException.ThrowIfNull(floats);

        int width = Overcore.Animation.BonePalette.TextureWidth(boneCount);

        GL.ActiveTexture(TextureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);

        if (width != _width)
        {
            // Allocation and upload in one call, so the texture never exists uninitialized: a shader that sampled it
            // between a bare allocation and a sub-image write would read whatever the driver left in the storage.
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba32f,
                width,
                1,
                0,
                PixelFormat.Rgba,
                PixelType.Float,
                floats);

            _width = width;

            return;
        }

        // The storage is already the right size, so a sub-image write replaces in place.
        GL.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0,
            0,
            width,
            1,
            PixelFormat.Rgba,
            PixelType.Float,
            floats);
    }

    /// <summary>Binds the texture to its unit, ready for the draw that samples it.</summary>
    public void Bind()
    {
        GL.ActiveTexture(TextureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        GL.DeleteTexture(_handle);
        _disposed = true;
    }
}
