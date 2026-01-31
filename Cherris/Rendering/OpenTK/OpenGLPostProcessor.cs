using Cherris.Core.Logging;
using OpenTK.Graphics.OpenGL4;
using System.Diagnostics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLPostProcessor : IDisposable
{
    private readonly OpenGLPostProcessResources _resources;
    private int _width = 1;
    private int _height = 1;

    public OpenGLPostProcessor()
    {
        _resources = new OpenGLPostProcessResources();
        CreateFramebuffers();
        CheckGLError("Constructor end");
    }

    public int FinalSceneTexture => _resources.Composite?.ColorTexture ?? 0;

    public void OnResize(int width, int height)
    {
        int newW = Math.Max(1, width);
        int newH = Math.Max(1, height);

        if (newW == _width && newH == _height && _resources.Msaa != null)
        {
            return;
        }

        Logger.Info($"[OpenGLPostProcessor] Resizing to {newW}x{newH}");
        _width = newW;
        _height = newH;
        DisposeFramebuffers();
        CreateFramebuffers();
        CheckGLError("Resize end");
    }

    public void BeginFrame()
    {
        if (_resources.Msaa == null)
        {
            Logger.Error("[OpenGLPostProcessor] MSAA FBO is null");
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resources.Msaa.Handle);
        CheckGLError("Bind MSAA FBO");
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        CheckGLError("Clear MSAA");
    }

    public void ResolveMsaa()
    {
        if (_resources.Msaa == null || _resources.Resolved == null)
        {
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resources.Msaa.Handle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resources.Resolved.Handle);
        CheckGLError("Bind Resolve FBOs");
        GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        CheckGLError("Blit MSAA->Resolved");
    }

    public void RenderBloom()
    {
        if (_resources.Bloom[0] == null || _resources.BrightPassShader == null)
        {
            return;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        CheckGLError("Disable depth/cull");

        RenderBrightPass();
        RenderBlur();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
    }

    private void RenderBrightPass()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resources.Bloom[0].Handle);
        CheckGLError("Bind Bloom[0]");
        GL.Viewport(0, 0, Math.Max(1, _width / 2), Math.Max(1, _height / 2));

        _resources.BrightPassShader.Use();
        CheckGLError("Use BrightPass shader");
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resources.Resolved.ColorTexture);
        CheckGLError("Bind Resolved texture");
        _resources.RenderQuad();
        CheckGLError("Render brightpass quad");
    }

    private void RenderBlur()
    {
        bool horizontal = true;
        bool firstIteration = true;
        const int amount = 10;

        int imageLoc = GL.GetUniformLocation(_resources.BlurShader.Handle, "image");
        int horizontalLoc = GL.GetUniformLocation(_resources.BlurShader.Handle, "horizontal");
        CheckGLError("Get blur uniform locations");

        _resources.BlurShader.Use();
        GL.Uniform1(imageLoc, 0);
        CheckGLError("Set blur uniform 'image'");

        for (int i = 0; i < amount; i++)
        {
            int targetIndex = horizontal ? 1 : 0;
            int sourceIndex = firstIteration ? 0 : (horizontal ? 0 : 1);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resources.Bloom[targetIndex].Handle);
            CheckGLError($"Bind Bloom[{targetIndex}]");
            GL.Uniform1(horizontalLoc, horizontal ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _resources.Bloom[sourceIndex].ColorTexture);
            CheckGLError($"Bind Bloom[{sourceIndex}] texture");
            _resources.RenderQuad();

            horizontal = !horizontal;
            if (firstIteration)
            {
                firstIteration = false;
            }
        }
        CheckGLError("Blur loop end");
    }

    public void Composite(float exposure)
    {
        if (_resources.Composite == null || _resources.FinalCompositeShader == null)
        {
            Logger.Error("[OpenGLPostProcessor] Cannot composite: null resources");
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resources.Composite.Handle);
        CheckGLError("Bind Composite");
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        CheckGLError("Clear Composite");

        int imageLoc = GL.GetUniformLocation(_resources.FinalCompositeShader.Handle, "image");
        int exposureLoc = GL.GetUniformLocation(_resources.FinalCompositeShader.Handle, "exposure");
        int isBloomLoc = GL.GetUniformLocation(_resources.FinalCompositeShader.Handle, "isBloomPass");
        CheckGLError("Get composite uniform locations");

        _resources.FinalCompositeShader.Use();

        // Main scene pass (IsBloomPass = 0)
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resources.Resolved.ColorTexture);
        GL.Uniform1(imageLoc, 0);
        GL.Uniform1(exposureLoc, exposure);
        GL.Uniform1(isBloomLoc, 0);
        CheckGLError("Set composite uniforms (main pass)");
        _resources.RenderQuad();
        CheckGLError("Render composite main quad");

        // Bloom additive blend (IsBloomPass = 1)
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        GL.BindTexture(TextureTarget.Texture2D, _resources.Bloom[0].ColorTexture);
        GL.Uniform1(isBloomLoc, 1);
        CheckGLError("Set composite uniforms (bloom pass)");
        _resources.RenderQuad();
        CheckGLError("Render composite bloom quad");
        GL.Disable(EnableCap.Blend);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        CheckGLError("Unbind Composite");
    }

    private void CreateFramebuffers()
    {
        _resources.Msaa = new FramebufferBuilder(_width, _height)
            .WithMultisampling(4)
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .WithDepthStencil()
            .Build("MSAA");

        _resources.Resolved = new FramebufferBuilder(_width, _height)
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .Build("Resolved");

        _resources.Bloom[0] = new FramebufferBuilder(_width, _height)
            .WithHalfSize()
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .WithWrapMode(TextureWrapMode.ClampToEdge)
            .Build("Bloom0");

        _resources.Bloom[1] = new FramebufferBuilder(_width, _height)
            .WithHalfSize()
            .WithColorFormat(PixelInternalFormat.Rgba16f, PixelType.Float)
            .WithWrapMode(TextureWrapMode.ClampToEdge)
            .Build("Bloom1");

        _resources.Composite = new FramebufferBuilder(_width, _height)
            .WithColorFormat(PixelInternalFormat.Srgb8Alpha8, PixelType.UnsignedByte)
            .Build("Composite");

        Logger.Info($"[OpenGLPostProcessor] Framebuffers created. Composite handle: {_resources.Composite.Handle}, Texture: {_resources.Composite.ColorTexture}");
    }

    private void DisposeFramebuffers()
    {
        _resources.Msaa?.Dispose();
        _resources.Resolved?.Dispose();
        _resources.Bloom[0]?.Dispose();
        _resources.Bloom[1]?.Dispose();
        _resources.Composite?.Dispose();

        _resources.Msaa = null;
        _resources.Resolved = null;
        _resources.Bloom[0] = null;
        _resources.Bloom[1] = null;
        _resources.Composite = null;
    }

    public void Dispose()
    {
        DisposeFramebuffers();
        _resources.Dispose();
    }

    [Conditional("DEBUG")]
    private void CheckGLError(string context)
    {
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError)
        {
            Logger.Error($"[GL ERROR] {context}: {error}");
        }
    }

    private class OpenGLPostProcessResources : IDisposable
    {
        public Framebuffer? Msaa { get; set; }
        public Framebuffer? Resolved { get; set; }
        public Framebuffer[] Bloom { get; set; } = new Framebuffer[2];
        public Framebuffer? Composite { get; set; }

        public ShaderProgram BrightPassShader { get; }
        public ShaderProgram BlurShader { get; }
        public ShaderProgram FinalCompositeShader { get; }

        private readonly int _quadVao;
        private readonly int _quadVbo;

        public OpenGLPostProcessResources()
        {
            BrightPassShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_brightpass.frag");
            BlurShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_blur.frag");
            FinalCompositeShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_composite.frag");

            if (BrightPassShader == null || BlurShader == null || FinalCompositeShader == null)
            {
                throw new InvalidOperationException("Failed to load post-processing shaders");
            }

            (_quadVao, _quadVbo) = CreateFullscreenQuad();
        }

        public void RenderQuad()
        {
            if (_quadVao == 0)
            {
                return;
            }

            GL.BindVertexArray(_quadVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
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
            BrightPassShader?.Dispose();
            BlurShader?.Dispose();
            FinalCompositeShader?.Dispose();
            GL.DeleteVertexArray(_quadVao);
            GL.DeleteBuffer(_quadVbo);
        }
    }
}