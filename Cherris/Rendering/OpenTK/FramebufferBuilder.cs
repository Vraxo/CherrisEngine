using OpenTK.Graphics.OpenGL4;

namespace Cherris.Rendering.OpenTK;

internal sealed class FramebufferBuilder
{
    private readonly int _width;
    private readonly int _height;
    private bool _halfSize;
    private bool _multisampled;
    private int _samples = 4;
    private PixelInternalFormat _colorFormat = PixelInternalFormat.Rgba8;
    private readonly PixelFormat _pixelFormat = PixelFormat.Rgba;
    private PixelType _pixelType = PixelType.UnsignedByte;
    private bool _depthStencil;
    private TextureWrapMode _wrapMode = TextureWrapMode.Repeat;

    public FramebufferBuilder(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid framebuffer size: {width}x{height}");
        }

        _width = width;
        _height = height;
    }

    public FramebufferBuilder WithHalfSize()
    {
        _halfSize = true;
        return this;
    }

    public FramebufferBuilder WithMultisampling(int samples)
    {
        _multisampled = true;
        _samples = samples;
        return this;
    }

    public FramebufferBuilder WithColorFormat(PixelInternalFormat format, PixelType pixelType)
    {
        _colorFormat = format;
        _pixelType = pixelType;
        return this;
    }

    public FramebufferBuilder WithWrapMode(TextureWrapMode mode)
    {
        _wrapMode = mode;
        return this;
    }

    public FramebufferBuilder WithDepthStencil()
    {
        _depthStencil = true;
        return this;
    }

    public Framebuffer Build(string label)
    {
        int w = _halfSize ? Math.Max(1, _width / 2) : _width;
        int h = _halfSize ? Math.Max(1, _height / 2) : _height;

        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        int colorTexture;
        if (_multisampled)
        {
            colorTexture = CreateMultisampleColorTexture(w, h);
        }
        else
        {
            colorTexture = CreateRegularColorTexture(w, h);
        }

        int? depthStencil = _depthStencil ? CreateDepthStencil(w, h) : null;

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            throw new InvalidOperationException($"[FramebufferBuilder] {label} Framebuffer incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return new Framebuffer(fbo, colorTexture, depthStencil);
    }

    private int CreateMultisampleColorTexture(int w, int h)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DMultisample, tex);
        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, _samples, _colorFormat, w, h, true);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2DMultisample, tex, 0);
        return tex;
    }

    private int CreateRegularColorTexture(int w, int h)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, _colorFormat, w, h, 0, _pixelFormat, _pixelType, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)_wrapMode);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)_wrapMode);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        return tex;
    }

    private int CreateDepthStencil(int w, int h)
    {
        int rbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, rbo);

        if (_multisampled)
        {
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, _samples, RenderbufferStorage.Depth24Stencil8, w, h);
        }
        else
        {
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, w, h);
        }

        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, rbo);
        return rbo;
    }
}