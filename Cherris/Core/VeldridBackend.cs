using Cherris.Rendering;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Cherris;

// Concrete Veldrid backend implementation, nested here to avoid creating new files.
public class VeldridBackend : RenderingInterface
{
    public IGameWindow GameWindow { get; private set; }
    public IRenderer Renderer { get; private set; }
    public IResourceManager ResourceManager { get; private set; }
    public IUIController? UIController => null; // ImGui not implemented for Veldrid yet
    private GraphicsDevice _graphicsDevice;
    private VeldridGraphicsManager _graphicsManager;

    public VeldridBackend() { }

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        var window = new GameWindow(windowTitle, width, height, startWithMouseLocked);

        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window.SdlWindow, options);
        window.SetGraphicsDevice(_graphicsDevice);

        _graphicsManager = new VeldridGraphicsManager(_graphicsDevice, window.SdlWindow, TextureSampleCount.Count4);

        GameWindow = window;
        ResourceManager = new ResourceManager(_graphicsDevice);
        Renderer = new Renderer(_graphicsManager, _graphicsDevice);
    }

    public void Dispose()
    {
        Renderer?.Dispose();
        ResourceManager?.Dispose();
        _graphicsDevice?.Dispose();
        GameWindow?.Dispose();
    }
}