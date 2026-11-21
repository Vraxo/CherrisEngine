using Cherris.RenderingInterface;

namespace Cherris.Rendering.OpenTK;

public class OpenTKBackend : IRenderingInterface
{
    public IGameWindow? GameWindow { get; private set; }
    public IRenderer? Renderer { get; private set; }
    public IResourceManager? ResourceManager { get; private set; }
    public IUIController? UIController { get; private set; }

    private ImGuiController? _imGuiController;

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        OpenTKGameWindow window = new(windowTitle, width, height, startWithMouseLocked);
        GameWindow = window;

        _imGuiController = new(width, height);
        UIController = _imGuiController;
        window.SetImGuiController(_imGuiController);

        Renderer = new OpenTKRenderer(_imGuiController);
        ResourceManager = new OpenTKResourceManager();
    }

    public void Dispose()
    {
        _imGuiController?.Dispose();
        GameWindow?.Dispose();
        Renderer?.Dispose();
        ResourceManager?.Dispose();

        GC.SuppressFinalize(this);
    }
}