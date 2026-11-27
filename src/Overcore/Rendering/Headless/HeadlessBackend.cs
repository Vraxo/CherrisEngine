using Overcore.Rendering.OpenTK;
using Overcore.RenderingInterface;
using OpenTK.Windowing.Desktop;
using System.Numerics;
using Vector2i = OpenTK.Mathematics.Vector2i;

namespace Overcore.Rendering.Headless;

/// <summary>
/// Rendering backend for hosts whose UI is not a native window: it owns an off-screen OpenGL context,
/// a <see cref="HeadlessGameWindow"/> standing in for the window, and an <see cref="OpenTKRenderer"/>
/// whose final scene texture the host can publish with <see cref="SceneFrameCapture"/>.
/// </summary>
/// <remarks>
/// <para>
/// The context comes from a hidden <see cref="NativeWindow"/> because a GL context needs a native
/// drawable on Windows and macOS. Nothing is ever presented: the window is never shown, never swapped,
/// and only exists to make the context current on the thread that calls
/// <see cref="IRenderingInterface.Initialize"/>.
/// </para>
/// <para>
/// All members that touch OpenGL must be used from that same thread. On macOS, GLFW also has to be
/// initialized from the process' main thread, so create the backend before starting the webview's
/// message loop rather than from a Blazor component.
/// </para>
/// </remarks>
public sealed class HeadlessBackend : IRenderingInterface
{
    private readonly bool _ownGraphicsContext;

    private NativeWindow? _contextWindow;
    private HeadlessGameWindow? _headlessWindow;
    private bool _disposed;

    /// <param name="ownGraphicsContext">
    /// When true (the default) a hidden native window is created to provide the GL context. Pass false
    /// when the host already made a context current on this thread.
    /// </param>
    public HeadlessBackend(bool ownGraphicsContext = true)
    {
        _ownGraphicsContext = ownGraphicsContext;
    }

    /// <inheritdoc />
    public IGameWindow? GameWindow { get; private set; }

    /// <inheritdoc />
    public IRenderer? Renderer { get; private set; }

    /// <inheritdoc />
    public IResourceManager? ResourceManager { get; private set; }

    /// <inheritdoc />
    /// <remarks>Headless hosts draw their own UI, so the engine has no in-engine UI controller.</remarks>
    public IUIController? UIController => null;

    /// <summary>
    /// Frame rate the render loop is throttled to. Applied to the window stand-in when the backend is
    /// initialized; set it before <see cref="Initialize"/>.
    /// </summary>
    public double TargetFrameRate { get; set; } = 60;

    /// <summary>The window stand-in, for resizing and frame pacing.</summary>
    public HeadlessGameWindow? HeadlessWindow => _headlessWindow;

    /// <summary>The concrete renderer, for viewport sizing and scene texture access.</summary>
    public OpenTKRenderer? OpenGLRenderer => Renderer as OpenTKRenderer;

    /// <summary>
    /// Creates the hidden window that provides the GL context, without making the context current.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Initialize"/> for hosts whose UI thread is not the render thread (a
    /// native message loop owns the main thread in that case). OpenTK refuses to initialize GLFW off the
    /// process' main thread, so create the context there first and let <see cref="Initialize"/> make it
    /// current on the render thread. GL contexts are portable between threads; the window handle is not
    /// used for anything after this call, because nothing is ever presented.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This backend does not own a graphics context, so there is nothing to create.
    /// </exception>
    public void CreateGraphicsContext(string windowTitle, int width = 1920, int height = 1080)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_ownGraphicsContext)
        {
            throw new InvalidOperationException(
                "This backend was created without its own graphics context, so there is nothing to create.");
        }

        if (_contextWindow is not null)
        {
            return;
        }

        NativeWindowSettings settings = new()
        {
            ClientSize = new Vector2i(Math.Max(1, width), Math.Max(1, height)),
            Title = windowTitle,
            StartVisible = false
        };

        _contextWindow = new NativeWindow(settings);

        // NativeWindow made the context current on this thread, and a GL context can only be current on one
        // thread at a time: leaving it current here makes the render thread's Initialize fail with
        // "WGL: Failed to make context current: The requested resource is in use". Release it; Initialize
        // makes it current on whichever thread runs the engine.
        ReleaseGraphicsContext();
    }

    /// <summary>
    /// Unbinds the GL context from the calling thread, if it is current there. Call before handing the
    /// backend to another thread and before disposing it from a different thread than the render loop's.
    /// </summary>
    public void ReleaseGraphicsContext()
    {
        _contextWindow?.Context?.MakeNoneCurrent();
    }

    /// <inheritdoc />
    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_ownGraphicsContext)
        {
            CreateGraphicsContext(windowTitle, width, height);
        }

        _contextWindow?.MakeCurrent();

        _headlessWindow = new HeadlessGameWindow(width, height) { TargetFrameRate = TargetFrameRate };
        GameWindow = _headlessWindow;
        ResourceManager = new OpenTKResourceManager();
        Renderer = new OpenTKRenderer(resourceManager: ResourceManager);

        if (startWithMouseLocked)
        {
            _headlessWindow.IsMouseLocked = true;
        }
    }

    /// <summary>
    /// Resizes the off-screen viewport. Call once per frame before the engine renders, with the
    /// on-screen viewport size in device pixels; the renderer takes its off-screen (editor) path only
    /// for frames where a viewport size was set.
    /// </summary>
    public void SetViewportSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        _headlessWindow?.Resize(width, height);
        OpenGLRenderer?.SetViewportSize(new Vector2(width, height));
    }

    /// <summary>Stops the engine's game loop.</summary>
    public void Close()
    {
        _headlessWindow?.Close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Renderer?.Dispose();
        ResourceManager?.Dispose();
        _headlessWindow?.Dispose();
        _contextWindow?.Dispose();

        Renderer = null;
        ResourceManager = null;
        GameWindow = null;
        _headlessWindow = null;
        _contextWindow = null;

        GC.SuppressFinalize(this);
    }
}
