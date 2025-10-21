using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Desktop;

namespace Cherris;

public class OpenTKBackend : IGraphicsBackend
{
    public IGameWindow GameWindow { get; private set; }
    public IRenderer Renderer { get; private set; }
    public IResourceManager ResourceManager { get; private set; }

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        var window = new OpenTKGameWindow(windowTitle, width, height, startWithMouseLocked);
        GameWindow = window;

        // The GameWindow's context is made current in its constructor,
        // and function pointers are loaded automatically.
        // The explicit GL.LoadBindings call is no longer needed.

        Renderer = new OpenTKRenderer();
        ResourceManager = new OpenTKResourceManager();
    }

    public void Dispose()
    {
        GameWindow?.Dispose();
        Renderer?.Dispose();
        ResourceManager?.Dispose();
    }
}