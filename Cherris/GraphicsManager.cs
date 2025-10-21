using System;
using Veldrid;
using Veldrid.Sdl2;

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

    // Temporary "ping-pong" buffer for blurring
    public Framebuffer BloomTempFramebuffer { get; private set; }
    public Veldrid.Texture BloomTempColorTarget { get; private set; }
    public TextureView BloomTempColorView { get; private set; }


    public Framebuffer SwapchainFramebuffer => GraphicsDevice.SwapchainFramebuffer;

    private readonly TextureSampleCount _msaaSampleCount;

    public GraphicsManager(GraphicsDevice graphicsDevice, Sdl2Window window, TextureSampleCount msaaSampleCount)
    {
        _msaaSampleCount = msaaSampleCount;
        GraphicsDevice = graphicsDevice;
        CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();

        CreateResources((int)window.Width, (int)window.Height);
    }

    public void Resize(int width, int height)
    {
        // The swapchain is resized automatically by the window resize event handler in VeldridStartup.
        // We only need to recreate our intermediate buffers.
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
        uint bloomWidth = (uint)Math.Max(1, width / 2);
        uint bloomHeight = (uint)Math.Max(1, height / 2);
        BloomColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            bloomWidth, bloomHeight, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        BloomColorView = GraphicsDevice.ResourceFactory.CreateTextureView(BloomColorTarget);

        BloomFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, BloomColorTarget));

        // Bloom Temp Target (half res)
        BloomTempColorTarget = GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            bloomWidth, bloomHeight, 1, 1, colorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        BloomTempColorView = GraphicsDevice.ResourceFactory.CreateTextureView(BloomTempColorTarget);

        BloomTempFramebuffer = GraphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, BloomTempColorTarget));
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

        BloomTempColorView?.Dispose();
        BloomTempColorTarget?.Dispose();
        BloomTempFramebuffer?.Dispose();
    }

    public void Dispose()
    {
        DisposeResources();
        CommandList.Dispose();
        // The GraphicsDevice is managed by the backend, so we don't dispose it here.
    }
}