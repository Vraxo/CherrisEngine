using Cherris;
using Cherris.Core;
using Cherris.Rendering.Headless;
using System.Diagnostics;
using System.Net.WebSockets;

namespace CherrisEditorWeb.Host;

/// <summary>
/// Composes the web editor: the HTTP/WebSocket server, the render thread and the frame/input plumbing
/// between them.
/// </summary>
/// <remarks>
/// <para>
/// The engine and its OpenGL context live on a dedicated render thread, because the GL context is
/// thread-affine and the server has its own async loops. Frames cross from the render thread to the
/// client through <see cref="ViewerConnection.PublishFrame"/>, which never blocks the render thread, and
/// input crosses the other way through <see cref="HeadlessInput"/>, which is a thread-safe queue drained
/// at frame start.
/// </para>
/// <para>
/// One editor window is supported at a time: a newly connected client replaces the previous one.
/// </para>
/// </remarks>
public sealed class WebEditorHost : IViewportSink, IViewportInputSink, ISceneSnapshotSink, IDisposable
{
    private const int MinimumViewport = 32;
    private const int MaximumViewportWidth = 2560;
    private const int MaximumViewportHeight = 1600;

    private readonly WebEditorOptions _options;
    private readonly ManualResetEventSlim _shutdown = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly string _webRoot;

    private ViewerConnection? _activeConnection;
    private WebEditorServer? _server;
    private volatile WebEditorEngine? _engine;
    private int _width;
    private int _height;
    private int _shuttingDown;
    private volatile bool _renderFailed;
    private volatile bool _startFailed;
    private SceneSnapshot? _latestScene;
    private bool _disposed;

    public WebEditorHost(WebEditorOptions options, string webRoot)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _webRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
        _width = options.Width;
        _height = options.Height;
    }

    /// <inheritdoc />
    public (int Width, int Height) ViewportSize => (Volatile.Read(ref _width), Volatile.Read(ref _height));

    /// <summary>URL the HTTP/WebSocket listener serves, or null before <see cref="Run"/> starts it.</summary>
    public string? Url => _server?.Url;

    /// <summary>True when the listener could not be started; <see cref="Url"/> stays null.</summary>
    public bool StartFailed => _startFailed;

    /// <summary>Most recent scene snapshot published by the render thread, or null before the first one.</summary>
    public SceneSnapshot? LatestScene => Volatile.Read(ref _latestScene);

    /// <summary>
    /// Blocks until the listener is up or failed. Lets a host that runs <see cref="Run"/> on a background
    /// thread discover the URL before it shows a window.
    /// </summary>
    public bool WaitForStart(TimeSpan timeout) => _started.Wait(timeout);

    /// <summary>
    /// Asks the host to stop from any thread. The engine loop stops on its next update and
    /// <see cref="Run"/> returns; unlike <see cref="Dispose"/>, this does not tear anything down while the
    /// render thread is still using it.
    /// </summary>
    public void RequestStop()
    {
        _shutdown.Set();
    }

    /// <summary>
    /// Runs until the process is interrupted. Returns the process exit code.
    /// </summary>
    public int Run()
    {
        string prefix = _options.ListenOnAllInterfaces
            ? $"http://+:{_options.Port}/"
            : $"http://localhost:{_options.Port}/";

        _server = new WebEditorServer(prefix, _webRoot, CreateConnection);

        try
        {
            _server.Start();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[web-editor] Could not listen on {prefix}: {exception.Message}");
            Console.Error.WriteLine("If the port is busy, pass --port <n>.");
            _startFailed = true;
            _started.Set();
            return 1;
        }

        _started.Set();

        Console.WriteLine($"[web-editor] Serving the editor at {_server.Url}");
        Console.WriteLine("[web-editor] Press Ctrl+C to stop.");

        if (_options.OpenBrowser)
        {
            OpenBrowser(_server.Url);
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        // Any thread can request shutdown (Ctrl+C, render failure); the engine loop itself runs on this
        // thread because a GL context, and GLFW on macOS in particular, wants the process' main thread.
        Thread shutdownWatcher = new(() =>
        {
            _shutdown.Wait();
            _engine?.RequestStop();
        })
        {
            IsBackground = true,
            Name = "Cherris Shutdown"
        };
        shutdownWatcher.Start();

        RunEngine();

        Shutdown();
        return _renderFailed ? 1 : 0;
    }

    /// <inheritdoc />
    public void PublishScene(SceneSnapshot snapshot)
    {
        Volatile.Write(ref _latestScene, snapshot);
    }

    /// <inheritdoc />
    public void PublishFrame(ReadOnlySpan<byte> rgba, int width, int height, long sequence)
    {
        ViewerConnection? connection = Volatile.Read(ref _activeConnection);

        if (connection is null)
        {
            return;
        }

        connection.PublishFrame(rgba, width, height, (int)(sequence & 0x7FFFFFFF));
    }

    /// <inheritdoc />
    public void Resize(int width, int height)
    {
        Volatile.Write(ref _width, Math.Clamp(width, MinimumViewport, MaximumViewportWidth));
        Volatile.Write(ref _height, Math.Clamp(height, MinimumViewport, MaximumViewportHeight));
    }

    /// <inheritdoc />
    public void KeyChanged(string code, bool isDown)
    {
        if (BrowserKeyMap.TryMapKey(code, out Key key))
        {
            HeadlessInput.SetKey(key, isDown);
        }
    }

    /// <inheritdoc />
    public void MouseButtonChanged(int button, bool isDown)
    {
        if (BrowserKeyMap.TryMapMouseButton(button, out MouseButton mapped))
        {
            HeadlessInput.SetMouseButton(mapped, isDown);
        }
    }

    /// <inheritdoc />
    public void MouseMoved(float deltaX, float deltaY)
    {
        HeadlessInput.MoveMouse(deltaX, deltaY);
    }

    /// <inheritdoc />
    public void MouseMovedTo(float x, float y)
    {
        HeadlessInput.SetMousePosition(x, y);
    }

    /// <inheritdoc />
    public void Wheel(float x, float y)
    {
        HeadlessInput.AddMouseWheel(x, y);
    }

    /// <inheritdoc />
    public void MouseLockChanged(bool locked)
    {
        HeadlessInput.SetMouseLocked(locked);
    }

    /// <inheritdoc />
    public void FocusLost()
    {
        HeadlessInput.Reset();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Shutdown();
        _shutdown.Dispose();
        _started.Dispose();
        GC.SuppressFinalize(this);
    }

    private ViewerConnection CreateConnection(WebSocket socket)
    {
        (int width, int height) = ViewportSize;
        string greeting = ViewportProtocol.CreateGreeting(width, height, _options.TargetFrameRate);
        ViewerConnection connection = new(socket, this, greeting);

        ViewerConnection? previous = Interlocked.Exchange(ref _activeConnection, connection);

        if (previous is not null)
        {
            // One editor window at a time: drop the old client instead of sharing the render loop.
            _ = previous.DisposeAsync().AsTask();
        }

        return connection;
    }

    private void RunEngine()
    {
        HeadlessBackend backend;
        WebEditorEngine engine;

        try
        {
            backend = new HeadlessBackend { TargetFrameRate = _options.TargetFrameRate };
            engine = new WebEditorEngine(backend, this, this);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[web-editor] Could not create the renderer: {exception.Message}");
            Console.Error.WriteLine(
                "OpenGL 3.3 and the shader files under EditorResources/ are required. On a headless " +
                "machine, run under a virtual display (Xvfb) or use the desktop editor instead.");
            _renderFailed = true;
            _shutdown.Set();
            return;
        }

        _engine = engine;

        // Ctrl+C can arrive between starting the watcher and reaching this point.
        if (_shutdown.IsSet)
        {
            engine.RequestStop();
        }

        try
        {
            engine.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[web-editor] Render loop failed: {exception}");
            _renderFailed = true;
        }
        finally
        {
            Interlocked.Exchange(ref _activeConnection, null);
            _shutdown.Set();
        }
    }

    private void Shutdown()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0)
        {
            return;
        }

        Console.WriteLine("[web-editor] Stopping...");

        _engine?.RequestStop();

        if (_server is not null)
        {
            try
            {
                _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[web-editor] Server shutdown failed: {exception.Message}");
            }
        }

        Console.WriteLine("[web-editor] Stopped.");
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _shutdown.Set();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[web-editor] Could not open a browser automatically ({exception.Message}). Open {url} manually.");
        }
    }
}
