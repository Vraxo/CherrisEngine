using Cherris.RenderingInterface;

namespace Cherris.Rendering;

public interface RenderingInterface : IDisposable
{
    IGameWindow GameWindow { get; }
    IRenderer Renderer { get; }
    IResourceManager ResourceManager { get; }
    IUIController? UIController { get; }

    void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked);
}