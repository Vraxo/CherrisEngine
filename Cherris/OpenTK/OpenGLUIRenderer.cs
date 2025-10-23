using DirectUI;
using DirectUI.Backends.SkiaSharp;
using DirectUI.Core;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using System;
using System.Numerics;

namespace Cherris;

internal class OpenGLUIRenderer : IDisposable
{
    private readonly AppEngine _appEngine;
    private readonly GRContext _grContext;
    private SKSurface _skSurface;
    private GRBackendRenderTarget _skRenderTarget;
    private readonly SilkNetRenderer _uiRenderer; // Reusing SilkNetRenderer as it's Skia-based
    private readonly SilkNetTextService _uiTextService;

    public IRenderer UiRenderer => _uiRenderer;
    public ITextService UiTextService => _uiTextService;

    public OpenGLUIRenderer(AppEngine appEngine)
    {
        _appEngine = appEngine;

        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);
        _uiTextService = new SilkNetTextService();
        _uiRenderer = new SilkNetRenderer(_uiTextService);
    }

    public void OnResize(int width, int height)
    {
        _skRenderTarget?.Dispose();
        _skSurface?.Dispose();

        GL.GetInteger(GetPName.Samples, out int samples);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Stencil, FramebufferParameterName.FramebufferAttachmentStencilSize, out int stencil);

        var framebufferInfo = new GRGlFramebufferInfo(0, (uint)PixelInternalFormat.Rgba8);
        _skRenderTarget = new GRBackendRenderTarget(width, height, samples, stencil, framebufferInfo);
        _skSurface = SKSurface.Create(_grContext, _skRenderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        _uiTextService?.Cleanup();
    }

    public void Render()
    {
        if (_appEngine != null && _skSurface != null)
        {
            // GL state for 2D UI
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _uiRenderer.SetCanvas(_skSurface.Canvas, new Vector2(_skRenderTarget.Width, _skRenderTarget.Height));
            _appEngine.UpdateAndRender(_uiRenderer, _uiTextService);
            _skSurface.Canvas.Flush();
        }
    }

    public void Dispose()
    {
        _skSurface?.Dispose();
        _skRenderTarget?.Dispose();
        _grContext?.Dispose();
        _uiRenderer?.Cleanup();
        _uiTextService?.Cleanup();
    }
}