using Cherris.Core.Logging;
using OpenTK.Graphics.OpenGL4;
using System.Diagnostics;

namespace Cherris.Rendering.OpenTK;

internal partial class OpenGLPostProcessor : IDisposable
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

        // Restore state if needed, though usually RenderFrame handles this for the next pass
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

        // Disable culling and depth test for the fullscreen quad pass
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);

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

        // Restore depth/cull defaults for safety, although RenderFrame usually resets them
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
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
}