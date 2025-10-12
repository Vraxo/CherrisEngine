using System;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris;

public class GraphicsManager : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public CommandList CommandList { get; }

    // MSAA resources
    public Framebuffer MsaaFramebuffer { get; private set; }
    private Veldrid.Texture _msaaColorTarget;
    private Veldrid.Texture _msaaDepthTarget;
    public TextureView MsaaColorView { get; private set; }

    // Final render target resources
    public Framebuffer FinalFramebuffer { get; private set; }
    public Veldrid.Texture FinalColorTarget { get; private set; }
    public TextureView FinalColorView { get; private set; }

    // Bloom resources (half resolution)
    public Framebuffer BloomFramebuffer { get; private set; }
    public Veldrid.Texture BloomColorTarget { get; private set; }
    public TextureView BloomColorView { get; private set; }


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

        CreateResources((int)window.Width, (int)window.Height);
    }

    public void Resize(int width, int height)
    {
        GraphicsDevice.ResizeMainWindow((uint)width, (uint)height);
        DisposeResources();
        CreateResources(width, height);
    }

    private void CreateResources(int width, int height)
    {
        // Use a 16-bit float format for our intermediate render targets to support HDR values.
        const PixelFormat colorFormat = PixelFormat.R16_G16_B16_A16_Float;

        // MSAA Targets
        _msaaColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled,
            sampleCount: _msaaSampleCount));

        _msaaDepthTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil,
            sampleCount: _msaaSampleCount));

        MsaaFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
            _msaaDepthTarget, _msaaColorTarget));

        MsaaColorView = GraphicsDevice.ResourceFactory.CreateTextureView(_msaaColorTarget);

        // Final Target
        FinalColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        FinalColorView = GraphicsDevice.ResourceFactory.CreateTextureView(FinalColorTarget);

        FinalFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, FinalColorTarget));

        // Bloom Target (half res)
        uint bloomWidth = (uint)width / 2;
        uint bloomHeight = (uint)height / 2;
        BloomColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            bloomWidth, bloomHeight, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        BloomColorView = GraphicsDevice.ResourceFactory.CreateTextureView(BloomColorTarget);

        BloomFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, BloomColorTarget));
    }

    private void DisposeResources()
    {
        MsaaColorView?.Dispose();
        _msaaColorTarget?.Dispose();
        _msaaDepthTarget?.Dispose();
        MsaaFramebuffer?.Dispose();

        FinalColorView?.Dispose();
        FinalColorTarget?.Dispose();
        FinalFramebuffer?.Dispose();

        BloomColorView?.Dispose();
        BloomColorTarget?.Dispose();
        BloomFramebuffer?.Dispose();
    }

    public void Dispose()
    {
        DisposeResources();
        CommandList.Dispose();
        GraphicsDevice.Dispose();
    }
}