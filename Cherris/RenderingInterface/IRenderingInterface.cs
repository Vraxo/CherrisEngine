using Cherris.Rendering;

namespace Cherris.RenderingInterface;

public interface IRenderingInterface : IDisposable
{
    IGameWindow GameWindow { get; }
    IRenderer Renderer { get; }
    IResourceManager ResourceManager { get; }
    IUIController? UIController { get; }

    void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked);
}