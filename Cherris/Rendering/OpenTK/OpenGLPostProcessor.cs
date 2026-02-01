using Cherris.Core.Logging;
using OpenTK.Graphics.OpenGL4;

namespace Cherris.Rendering.OpenTK;

internal sealed class OpenGLPostProcessor : IDisposable
{
    private Framebuffer? _msaaFbo;
    private Framebuffer? _resolvedFbo;
    private readonly Framebuffer?[] _bloomFbos = new Framebuffer[2];
    private Framebuffer? _compositeFbo;

    private readonly ShaderProgram _brightPassShader;
    private readonly ShaderProgram _blurShader;
    private readonly ShaderProgram _compositeShader;

    private readonly int _quadVao;
    private readonly int _quadVbo;

    private int _width = 1;
    private int _height = 1;

    public int FinalSceneTexture => _compositeFbo?.ColorTexture ?? 0;

    public OpenGLPostProcessor()
    {
        _brightPassShader = LoadShaderOrThrow("Shaders/post_quad.vert", "Shaders/post_brightpass.frag");
        _blurShader = LoadShaderOrThrow("Shaders/post_quad.vert", "Shaders/post_blur.frag");
        _compositeShader = LoadShaderOrThrow("Shaders/post_quad.vert", "Shaders/post_composite.frag");

        (_quadVao, _quadVbo) = CreateFullscreenQuad();
        CreateFramebuffers();
    }

    private static ShaderProgram LoadShaderOrThrow(string vert, string frag)
    {
        return ShaderProgram.FromFiles(vert, frag)
               ?? throw new InvalidOperationException($"Failed to load shader: {vert}, {frag}");
    }

    public void OnResize(int width, int height)
    {
        int newW = Math.Max(1, width);
        int newH = Math.Max(1, height);

        if (newW == _width && newH == _height && _msaaFbo != null)
        {
            return;
        }

        Logger.Info($"[OpenGLPostProcessor] Resizing to {newW}x{newH}");
        _width = newW;
        _height = newH;

        DisposeFramebuffers();
        CreateFramebuffers();
    }

    public void BeginFrame()
    {
        if (_msaaFbo == null)
        {
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo.Handle);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void ResolveMsaa()
    {
        if (_msaaFbo == null || _resolvedFbo == null)
        {
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo.Handle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolvedFbo.Handle);
        GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
    }

    public void RenderBloom()
    {
        if (_bloomFbos[0] == null)
        {
            return;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        PerformBrightPass();
        PerformBlurPass();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
    }

    public void Composite(float exposure)
    {
        if (_compositeFbo == null)
        {
            return;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFbo.Handle);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        _compositeShader.Use();

        // Pass 1: Draw Scene
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resolvedFbo!.ColorTexture);
        GL.Uniform1(_compositeShader.GetUniformLocation("image"), 0);
        GL.Uniform1(_compositeShader.GetUniformLocation("exposure"), exposure);
        GL.Uniform1(_compositeShader.GetUniformLocation("isBloomPass"), 0);
        DrawQuad();

        // Pass 2: Add Bloom
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        GL.BindTexture(TextureTarget.Texture2D, _bloomFbos[0]!.ColorTexture);
        GL.Uniform1(_compositeShader.GetUniformLocation("isBloomPass"), 1);
        DrawQuad();

        GL.Disable(EnableCap.Blend);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
    }

    private void PerformBrightPass()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbos[0]!.Handle);
        GL.Viewport(0, 0, Math.Max(1, _width / 2), Math.Max(1, _height / 2));

        _brightPassShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resolvedFbo!.ColorTexture);
        DrawQuad();
    }

    private void PerformBlurPass()
    {
        bool horizontal = true;
        const int blurIterations = 10;
        int horizontalLoc = _blurShader.GetUniformLocation("horizontal");

        _blurShader.Use();
        GL.Uniform1(_blurShader.GetUniformLocation("image"), 0);

        for (int i = 0; i < blurIterations; i++)
        {
            int sourceIdx = horizontal ? 0 : 1;
            int targetIdx = horizontal ? 1 : 0;

            // First iteration reads from Bloom0 (result of bright pass)
            if (i == 0)
            {
                sourceIdx = 0;
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbos[targetIdx]!.Handle);
            GL.Uniform1(horizontalLoc, horizontal ? 1 : 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _bloomFbos[sourceIdx]!.ColorTexture);

            DrawQuad();
            horizontal = !horizontal;
        }
    }

    private void DrawQuad()
    {
        GL.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    private void CreateFramebuffers()
    {
        _msaaFbo = new FramebufferBuilder(_width, _height)
            .WithMultisampling(4)
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .WithDepthStencil()
            .Build("MSAA");

        _resolvedFbo = new FramebufferBuilder(_width, _height)
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .Build("Resolved");

        // Bloom buffers are half size
        for (int i = 0; i < 2; i++)
        {
            _bloomFbos[i] = new FramebufferBuilder(_width, _height)
                .WithHalfSize()
                .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
                .WithWrapMode(TextureWrapMode.ClampToEdge)
                .Build($"Bloom{i}");
        }

        _compositeFbo = new FramebufferBuilder(_width, _height)
            .WithColorFormat(PixelInternalFormat.Srgb8Alpha8, PixelType.UnsignedByte)
            .Build("Composite");
    }

    private void DisposeFramebuffers()
    {
        _msaaFbo?.Dispose();
        _resolvedFbo?.Dispose();
        _bloomFbos[0]?.Dispose();
        _bloomFbos[1]?.Dispose();
        _compositeFbo?.Dispose();

        _msaaFbo = null;
        _resolvedFbo = null;
        _bloomFbos[0] = null;
        _bloomFbos[1] = null;
        _compositeFbo = null;
    }

    private static (int vao, int vbo) CreateFullscreenQuad()
    {
        float[] quadVertices = {
            -1.0f,  1.0f,  0.0f, 1.0f,
            -1.0f, -1.0f,  0.0f, 0.0f,
             1.0f, -1.0f,  1.0f, 0.0f,
            -1.0f,  1.0f,  0.0f, 1.0f,
             1.0f, -1.0f,  1.0f, 0.0f,
             1.0f,  1.0f,  1.0f, 1.0f
        };

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * quadVertices.Length, quadVertices, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

        GL.BindVertexArray(0);
        return (vao, vbo);
    }

    public void Dispose()
    {
        DisposeFramebuffers();
        _brightPassShader.Dispose();
        _blurShader.Dispose();
        _compositeShader.Dispose();
        GL.DeleteVertexArray(_quadVao);
        GL.DeleteBuffer(_quadVbo);
    }
}