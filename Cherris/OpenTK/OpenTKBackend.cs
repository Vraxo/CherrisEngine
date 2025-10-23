using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Desktop;
using DirectUI;
using DirectUI.Core;
using DirectUI.Input;
using System;
using Vortice.Mathematics;
using SizeI = Vortice.Mathematics.SizeI;

namespace Cherris;

public class OpenTKBackend : IGraphicsBackend, IWindowHost
{
    public IGameWindow GameWindow { get; private set; }
    public Cherris.Rendering.IRenderer Renderer { get; private set; }
    public IResourceManager ResourceManager { get; private set; }
    private AppEngine _appEngine;
    private IAppLogic _uiLogic;

    // --- IWindowHost Implementation ---
    public AppEngine AppEngine => _appEngine;
    public InputManager Input => _appEngine.Input;
    public SizeI ClientSize => new((int)GameWindow.Width, (int)GameWindow.Height);
    public bool ShowFpsCounter { get; set; }
    public IModalWindowService ModalWindowService => null; // Not implemented for this demonstration
    public IntPtr Handle => (GameWindow as OpenTKGameWindow)?.Handle ?? IntPtr.Zero;

    public void SetUILogic(IAppLogic uiLogic)
    {
        _uiLogic = uiLogic;
    }

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        var window = new OpenTKGameWindow(windowTitle, width, height, startWithMouseLocked);
        GameWindow = window;

        // The GameWindow's context is made current in its constructor.

        _appEngine = new AppEngine(
            context => _uiLogic?.DrawUI(context),
            new Color4(0, 0, 0, 0) // Transparent background for UI layer
        );

        // Pass AppEngine to window for input forwarding
        window.SetInputManager(_appEngine.Input);

        Renderer = new OpenTKRenderer(_appEngine);
        ResourceManager = new OpenTKResourceManager();

        // AppEngine needs an IRenderer and ITextService to be initialized. We can get them from our OpenTKRenderer.
        if (Renderer is OpenTKRenderer otkRenderer)
        {
            _appEngine.Initialize(otkRenderer.UiTextService, otkRenderer.UiRenderer);
        }
    }

    // This is part of IWindowHost but not used in this integration pattern.
    bool IWindowHost.Initialize(Action<UIContext> uiDrawCallback, Color4 backgroundColor, float initialScale)
    {
        throw new NotSupportedException("This Initialize method is for standalone DirectUI applications.");
    }

    // RunLoop is handled by Cherris.Engine
    void IWindowHost.RunLoop()
    {
        throw new NotSupportedException("RunLoop is managed by the Cherris Engine.");
    }

    void IWindowHost.Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        _appEngine?.Cleanup();
        GameWindow?.Dispose();
        Renderer?.Dispose();
        ResourceManager?.Dispose();
    }
}