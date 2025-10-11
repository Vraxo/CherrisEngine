using System;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris;

public class GraphicsManager : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public CommandList CommandList { get; }

    public Framebuffer MsaaFramebuffer { get; private set; }
    private Veldrid.Texture _msaaColorTarget;
    private Veldrid.Texture _msaaDepthTarget;
    public TextureView MsaaColorView { get; private set; }

    public Framebuffer SwapchainFramebuffer => GraphicsDevice.SwapchainFramebuffer;

    private readonly TextureSampleCount _msaaSampleCount;

    public GraphicsManager(Sdl2Window window, TextureSampleCount msaaSampleCount = TextureSampleCount.Count4)
    {
        _msaaSampleCount = msaaSampleCount;

        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        GraphicsDevice = VeldridStartup.CreateGraphicsDevice(window, options);
        CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();

        CreateMsaaTargets((int)window.Width, (int)window.Height);
    }

    public void Resize(int width, int height)
    {
        GraphicsDevice.ResizeMainWindow((uint)width, (uint)height);
        DisposeMsaaTargets();
        CreateMsaaTargets(width, height);
    }

    private void CreateMsaaTargets(int width, int height)
    {
        PixelFormat swapchainFormat = SwapchainFramebuffer.ColorTargets[0].Target.Format;
        PixelFormat colorFormat = GetNonSrgbFormat(swapchainFormat);

        _msaaColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled,
            sampleCount: _msaaSampleCount));

        _msaaDepthTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R16_UNorm,
            TextureUsage.DepthStencil,
            sampleCount: _msaaSampleCount));

        MsaaFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
            _msaaDepthTarget, _msaaColorTarget));

        MsaaColorView = GraphicsDevice.ResourceFactory.CreateTextureView(_msaaColorTarget);
    }

    private void DisposeMsaaTargets()
    {
        MsaaColorView?.Dispose();
        _msaaColorTarget?.Dispose();
        _msaaDepthTarget?.Dispose();
        MsaaFramebuffer?.Dispose();
    }

    private static PixelFormat GetNonSrgbFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.B8_G8_R8_A8_UNorm_SRgb => PixelFormat.B8_G8_R8_A8_UNorm,
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb => PixelFormat.R8_G8_B8_A8_UNorm,
            _ => format
        };
    }

    public void Dispose()
    {
        DisposeMsaaTargets();
        CommandList.Dispose();
        GraphicsDevice.Dispose();
    }
}