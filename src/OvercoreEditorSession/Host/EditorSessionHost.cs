using Overcore;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Rendering.Headless;
using OpenTK.Windowing.Desktop;
using System.Diagnostics;
using System.Net.WebSockets;

namespace OvercoreEditorSession.Host;

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
/// <summary>Which step of <see cref="EditorSessionHost.Start"/> a session could not get past.</summary>
public enum EditorStartFailure
{
    /// <summary>The session started.</summary>
    None,

    /// <summary>No graphics context: no GL 3.3 driver, or GLFW refused the thread it was asked on.</summary>
    Graphics,

    /// <summary>The listener could not take its port.</summary>
    Listener
}

public sealed class EditorSessionHost : IViewportSink, IViewportInputSink, ISceneSnapshotSink, IDisposable
{
    private const int MinimumViewport = 32;
    private const int MaximumViewportWidth = 2560;
    private const int MaximumViewportHeight = 1600;
    private const int ContextWidth = 1920;
    private const int ContextHeight = 1080;

    /// <summary>How long a session gets to finish when it is asked to stop.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly EditorSessionOptions _options;
    private readonly ManualResetEventSlim _shutdown = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly string _webRoot;
    private HeadlessBackend? _graphicsBackend;
    private bool _ownsBackend;
    private Thread? _renderThread;
    private readonly ManualResetEventSlim _exited = new(false);
    private readonly Action<string>? _log;
    private readonly EditorSceneRequest _scene;

    /// <summary>Edits the UI asked for, and the session's undo history.</summary>
    private readonly EditorCommandDispatcher _commands;

    private ViewerConnection? _activeConnection;
    private WebEditorServer? _server;
    private volatile EditorSession? _engine;
    private int _width;
    private int _height;

    /// <summary>1 while the grid is drawn, 0 when it is not; an int so it can be read from another thread.</summary>
    private int _showGrid = 1;

    /// <summary>1 while the look button is held, 0 when it is not; an int so it can be read from another thread.</summary>
    private int _lookDragging;

    /// <summary>1 once a look drag has been seen, so the first one can be reported without repeating itself.</summary>
    private int _lookReported;
    private int _shuttingDown;
    private volatile bool _renderFailed;
    private volatile bool _startFailed;
    private volatile string? _failureDetail;
    private SceneSnapshot? _latestScene;
    private bool _disposed;

    /// <param name="options">Render, transport and sizing options.</param>
    /// <param name="webRoot">Directory served over HTTP; only the browser front end uses it.</param>
    /// <param name="graphicsBackend">
    /// A backend whose hidden window was already created on another thread, for hosts where the process'
    /// main thread belongs to a UI message loop. Null creates it on the render thread instead.
    /// </param>
    /// <param name="log">Sink for host diagnostics, in addition to the console.</param>
    /// <param name="scene">Scene the renderer opens; the built-in demo scene when omitted.</param>
    public EditorSessionHost(
        EditorSessionOptions options,
        string webRoot,
        HeadlessBackend? graphicsBackend = null,
        Action<string>? log = null,
        EditorSceneRequest? scene = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _webRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
        _graphicsBackend = graphicsBackend;
        _log = log;
        _scene = scene ?? EditorSceneRequest.Demo;

        // The project folder travels with the edits: adding a script writes a file into it, and the demo scene
        // has none to write to.
        _commands = new EditorCommandDispatcher(_scene.ProjectRoot);
        _width = options.Width;
        _height = options.Height;
    }

    /// <inheritdoc />
    public (int Width, int Height) ViewportSize => (Volatile.Read(ref _width), Volatile.Read(ref _height));

    /// <summary>
    /// Whether the viewport draws the editor's grid; on unless the UI turns it off. Safe from any thread — the
    /// render loop reads it once per frame and tells the renderer.
    /// </summary>
    public bool ShowGrid
    {
        get => Volatile.Read(ref _showGrid) != 0;
        set => Volatile.Write(ref _showGrid, value ? 1 : 0);
    }

    /// <inheritdoc />
    public bool LookDragging => Volatile.Read(ref _lookDragging) != 0;

    /// <summary>URL the HTTP/WebSocket listener serves, or null before <see cref="Run"/> starts it.</summary>
    public string? Url => _server?.Url;

    /// <summary>True when the listener could not be started; <see cref="Url"/> stays null.</summary>
    public bool StartFailed => _startFailed;

    /// <summary>
    /// What went wrong, verbatim, when the renderer or the render loop failed: the exception type, its
    /// message and its stack, not a guess at the cause.
    /// </summary>
    public string? FailureDetail => Volatile.Read(ref _failureDetail);

    /// <summary>Renderer and GL version strings captured once the renderer is live, or null before that.</summary>
    public string? RendererInfo => _engine?.RendererInfo;

    /// <summary>
    /// The open project's compiled C# scripts, as Add Component menu entries; empty until a session has compiled
    /// them, and empty forever in a session with no project.
    /// </summary>
    public IReadOnlyList<ComponentCatalog.Entry> ScriptComponents => _engine?.ScriptComponents ?? [];

    /// <summary>Most recent scene snapshot published by the render thread, or null before the first one.</summary>
    public SceneSnapshot? LatestScene => Volatile.Read(ref _latestScene);

    /// <summary>What the transport is doing in the running session; editing when nothing is running.</summary>
    public EditorPlayState PlayState => _engine?.PlayState ?? EditorPlayState.Editing;

    /// <summary>
    /// Something the session had to say about the scene it opened — an empty file, a camera it had to add, a
    /// file that is not a scene — or null when the scene opened exactly as it is on disk.
    /// </summary>
    public string? SceneNotice => _engine?.SceneNotice;

    /// <summary>
    /// Raised on the render thread when a click in the viewport picked an object: its id, or null when the click
    /// landed on empty space. Handlers must hand the id to the UI rather than touch the scene.
    /// </summary>
    public event Action<Guid?>? ObjectPicked;

    /// <summary>
    /// Asks the render thread to open another scene file in place of the one that is open. Safe from any thread;
    /// the render loop carries it out at the top of its next frame.
    /// </summary>
    /// <returns>False when there is no render loop to ask, or it is already shutting down.</returns>
    public bool RequestSceneOpen(string scenePath) => _engine?.RequestSceneOpen(scenePath) ?? false;

    /// <summary>
    /// Queues a transport action for the render thread. Safe from any thread; the render loop carries it out
    /// at the top of its next frame.
    /// </summary>
    /// <param name="request">The action.</param>
    /// <returns>False when no engine is running to take it.</returns>
    public bool RequestPlayback(PlaybackRequest request) => _engine?.RequestPlayback(request) ?? false;

    /// <summary>
    /// Asks the render thread to compile the project's C# scripts again at the top of its next frame. Safe from any
    /// thread; the UI calls it after writing a <c>.cs</c> file, so the type it declares is attachable without
    /// reopening the project.
    /// </summary>
    /// <returns>False when there is no render loop to ask, or it is already shutting down.</returns>
    public bool RequestScriptReload() => _engine?.RequestScriptReload() ?? false;

    /// <summary>
    /// Queues a change for the render thread. Safe from any thread, including Blazor's dispatcher; the
    /// command runs on the next frame, before that frame is captured.
    /// </summary>
    /// <param name="command">The command.</param>
    public void Enqueue(IEditorCommand command)
    {
        _commands.Enqueue(command);
    }

    /// <summary>The session's undo and redo stacks, for the menus to enable themselves.</summary>
    public EditorHistory History => _commands.History;

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
    /// Writes a diagnostic line to the host's log sink, or straight to the console when the host has no
    /// sink (the browser front end). The sink owns console output in that case, so lines are not doubled.
    /// </summary>
    public void WriteLog(string message)
    {
        if (_log is null)
        {
            Console.WriteLine(message);
            return;
        }

        _log(message);
    }

    /// <summary>
    /// Which step of <see cref="Start"/> a failed session stopped at.
    /// </summary>
    public EditorStartFailure StartFailure { get; private set; } = EditorStartFailure.None;

    /// <summary>
    /// Raised on the render thread once the loop has finished, with the code <see cref="Run"/> would have
    /// returned. A host that started the session with <see cref="Start"/> uses this to notice a crash.
    /// </summary>
    public event Action<int>? Exited;

    /// <summary>
    /// Runs until the process is interrupted. Returns the process exit code.
    /// </summary>
    /// <remarks>
    /// For a host with nothing else to do with its main thread — the browser front end. A host with a window
    /// message loop on that thread uses <see cref="Start"/> instead.
    /// </remarks>
    public int Run()
    {
        if (!StartServer())
        {
            return 1;
        }

        WriteLog("[web-editor] Press Ctrl+C to stop.");

        if (_options.OpenBrowser && _server is not null)
        {
            OpenBrowser(_server.Url);
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        StartShutdownWatcher();

        RenderLoop();

        return _renderFailed ? 1 : 0;
    }

    /// <summary>
    /// Starts a session without keeping the calling thread, for a host whose main thread belongs to a UI message
    /// loop, and returns once the listener is up or the session has failed.
    /// </summary>
    /// <remarks>
    /// The graphics context is created on the calling thread — OpenTK only initialises GLFW on the process' main
    /// thread — and the render loop runs on a background thread, which makes that context current itself. This is
    /// the only place that knows the order those two have to happen in, so both front ends get it right.
    /// </remarks>
    /// <returns>True when a session is running. When it is false, <see cref="StartFailure"/> and
    /// <see cref="FailureDetail"/> say which step could not be done.</returns>
    public bool Start()
    {
        if (!CreateGraphicsBackend())
        {
            StartFailure = EditorStartFailure.Graphics;
            Shutdown();
            return false;
        }

        if (!StartServer())
        {
            StartFailure = EditorStartFailure.Listener;
            Shutdown();
            return false;
        }

        StartShutdownWatcher();

        Thread render = new(RenderLoop)
        {
            IsBackground = true,
            Name = "Overcore Engine"
        };

        _renderThread = render;
        render.Start();

        return true;
    }

    /// <summary>
    /// Waits until the render loop started by <see cref="Start"/> or <see cref="Run"/> has finished.
    /// </summary>
    /// <param name="timeout">How long to wait before giving up on it.</param>
    public bool WaitForExit(TimeSpan timeout) => _exited.Wait(timeout);

    /// <summary>Runs the engine, then tears the session down, whichever thread it was started on.</summary>
    private void RenderLoop()
    {
        try
        {
            RunEngine();
        }
        finally
        {
            Shutdown();
            _exited.Set();

            try
            {
                Exited?.Invoke(_renderFailed ? 1 : 0);
            }
            catch (Exception exception)
            {
                WriteLog($"[web-editor] Exit handler failed: {exception.Message}");
            }
        }
    }

    /// <summary>Opens the listener, reporting a busy port rather than throwing.</summary>
    private bool StartServer()
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
            WriteLog($"[web-editor] Could not listen on {prefix}: {exception.Message}");
            WriteLog("[web-editor] If the port is busy, pass --port <n>.");
            Volatile.Write(ref _failureDetail, exception.ToString());
            _startFailed = true;
            _started.Set();

            return false;
        }

        _started.Set();
        WriteLog($"[web-editor] Serving the editor at {_server.Url}");

        return true;
    }

    /// <summary>Shuts the loop down when any thread asks for it, whichever thread started the session.</summary>
    private void StartShutdownWatcher()
    {
        // Any thread can request shutdown (Ctrl+C, a crash, the host's Dispose); the watcher hands it to the
        // engine, whose loop stops on its next update.
        Thread watcher = new(() =>
        {
            _shutdown.Wait();
            _engine?.RequestStop();
        })
        {
            IsBackground = true,
            Name = "Overcore Shutdown"
        };

        watcher.Start();
    }

    /// <summary>
    /// Makes sure there is a backend with a context, creating one on the calling thread when the host has none.
    /// </summary>
    /// <remarks>
    /// OpenTK only initialises GLFW on the process' main thread and <see cref="Overcore.Core.Engine"/>'s
    /// constructor initialises the backend, so a host whose main thread belongs to a UI has to create the context
    /// before the render thread starts. <c>CreateGraphicsContext</c> releases the context afterwards: a context can
    /// only be current on one thread at a time, and leaving it bound here makes the render thread's own
    /// <c>MakeCurrent</c> fail with ERROR_BUSY.
    /// </remarks>
    private bool CreateGraphicsBackend()
    {
        if (_graphicsBackend is not null)
        {
            return true;
        }

        HeadlessBackend backend = new();

        try
        {
            backend.CreateGraphicsContext("Overcore Editor", ContextWidth, ContextHeight);
            GLFWProvider.CheckForMainThread = false;
        }
        catch (Exception exception)
        {
            backend.Dispose();
            Volatile.Write(ref _failureDetail, exception.ToString());
            WriteLog($"[web-editor] Could not create the graphics context: {exception.Message}");

            return false;
        }

        _graphicsBackend = backend;
        _ownsBackend = true;

        return true;
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
        // The look flag deliberately survives this. A pointer capture can take the focus with it, and a drag that
        // ended there would stop the camera the moment the capture succeeded; with no movement arriving, a flag
        // left set moves nothing anyway.
        HeadlessInput.Reset();
    }

    /// <inheritdoc />
    public void PageLog(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Logger.Info($"[web-editor] page: {text}");
        }
    }

    /// <inheritdoc />
    public void LookChanged(bool isDown)
    {
        Interlocked.Exchange(ref _lookDragging, isDown ? 1 : 0);

        // Said once per session: a viewport that receives no drags at all and one whose drags are being dropped
        // elsewhere look the same from the outside, and one line in the console tells them apart.
        if (isDown && Interlocked.Exchange(ref _lookReported, 1) == 0)
        {
            Logger.Info("[web-editor] A look drag reached the render thread.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // A session that is still running gets a bounded chance to finish: the render loop releases the context on
        // its way out, and the hidden window must not be destroyed while something is still drawing into it.
        if (_renderThread is not null)
        {
            RequestStop();
            _exited.Wait(StopTimeout);
        }

        Shutdown();

        if (_ownsBackend)
        {
            _graphicsBackend?.Dispose();
            _graphicsBackend = null;
        }

        _shutdown.Dispose();
        _started.Dispose();
        _exited.Dispose();
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

    /// <summary>Forwards a pick from the render thread; a throwing handler must not end the render loop.</summary>
    private void OnObjectPicked(Guid? id)
    {
        try
        {
            ObjectPicked?.Invoke(id);
        }
        catch (Exception exception)
        {
            WriteLog($"[web-editor] Selection handler failed: {exception.Message}");
        }
    }

    private void RunEngine()
    {
        HeadlessBackend backend;
        EditorSession engine;

        try
        {
            if (!CreateGraphicsBackend())
            {
                _renderFailed = true;
                _shutdown.Set();
                return;
            }

            backend = _graphicsBackend!;
            backend.TargetFrameRate = _options.TargetFrameRate;

            engine = new EditorSession(backend, this, this, _scene, _commands);
        }
        catch (Exception exception)
        {
            // Report the exception itself. The cause can be a missing GL 3.3 driver, a GLFW thread
            // restriction or a missing shader, and a generic hint would hide which one it was.
            Volatile.Write(ref _failureDetail, exception.ToString());
            WriteLog($"[web-editor] Could not create the renderer: {exception}");
            _renderFailed = true;
            _shutdown.Set();
            return;
        }

        // A click in the viewport is picked on the render thread, which owns the scene; the UI decides what a
        // selection means.
        engine.ObjectPicked += OnObjectPicked;
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
            Volatile.Write(ref _failureDetail, exception.ToString());
            WriteLog($"[web-editor] Render loop failed: {exception}");
            _renderFailed = true;
        }
        finally
        {
            // The context is current on this thread, which is about to end. Unbind it so the host can
            // destroy the hidden window from the thread that created it.
            backend.ReleaseGraphicsContext();

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

        WriteLog("[web-editor] Stopping...");

        _engine?.RequestStop();

        if (_server is not null)
        {
            try
            {
                _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                WriteLog($"[web-editor] Server shutdown failed: {exception.Message}");
            }
        }

        WriteLog("[web-editor] Stopped.");
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
