using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Projects;
using Cherris.Rendering.Headless;
using CherrisEditorWeb;
using CherrisEditorWeb.Host;
using OpenTK.Windowing.Desktop;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CherrisEditorPhotino.Services;

/// <summary>Lifecycle of the editor's render session.</summary>
public enum EditorSessionState
{
    /// <summary>No session: the launcher is showing.</summary>
    Idle,

    /// <summary>A session is being prepared (graphics context, listener, render thread).</summary>
    Starting,

    /// <summary>The render thread is running.</summary>
    Running,

    /// <summary>The last attempt failed; <see cref="EditorHostService.FailureMessage"/> says why.</summary>
    Failed
}

/// <summary>One row in the launcher's recent-projects list.</summary>
/// <param name="Path">Project folder.</param>
/// <param name="Name">Project name from <c>project.yaml</c>, or the folder name.</param>
/// <param name="Exists">False when the folder or its <c>project.yaml</c> has gone away.</param>
public sealed record RecentProjectEntry(string Path, string Name, bool Exists);

/// <summary>
/// Owns the editor's render session: the GL context, the render thread, the HTTP/WebSocket listener and the
/// latest scene snapshot, plus the project it was opened from.
/// </summary>
/// <remarks>
/// <para>
/// The window's main thread belongs to the native Photino message loop and to Blazor's dispatcher, so each
/// session puts the engine on its own background thread. That is the opposite arrangement from the browser
/// host, where the engine owns the main thread.
/// </para>
/// <para>
/// One step cannot live on that thread: OpenTK refuses to initialise GLFW anywhere but the process' main
/// thread (<c>GLFWProvider.CheckForMainThread</c>), and <see cref="Cherris.Core.Engine"/>'s constructor
/// initialises the backend. The hidden window is therefore created here, on the thread that called
/// <see cref="OpenProjectAsync"/> — Blazor's dispatcher, which is the main thread — and the render thread
/// only makes the context current. GL contexts are portable between threads and the hidden window is never
/// presented.
/// </para>
/// <para>
/// Switching projects stops the session and starts a new one: the scene, the project's asset root and the
/// GL resources of the previous project do not have to be untangled that way, and opening a project is a
/// user-initiated action where a second of work is invisible.
/// </para>
/// </remarks>
public sealed class EditorHostService : IDisposable
{
    private const int MaximumLogLines = 400;
    private const int ContextWidth = 1920;
    private const int ContextHeight = 1080;
    private const int StopTimeoutSeconds = 5;
    private const int StartTimeoutSeconds = 20;

    private readonly string _webRoot;
    private readonly int _port;
    private readonly int _width;
    private readonly int _height;
    private readonly int _targetFrameRate;
    private readonly Cherris.Projects.RecentProjects _recent = new();
    private readonly Queue<EditorLogEntry> _log = new();
    private readonly object _logLock = new();

    private Session? _session;
    private volatile EditorSessionState _state = EditorSessionState.Idle;
    private volatile string? _failure;
    private volatile string? _failureDetail;
    private volatile string? _projectRoot;
    private string _sessionName = EditorSceneRequest.Demo.DisplayName;
    private string? _logFilePath;
    private bool _disposed;

    /// <param name="webRoot">Directory served over HTTP; only the frame socket matters to the UI.</param>
    /// <param name="width">Initial render width in device pixels.</param>
    /// <param name="height">Initial render height in device pixels.</param>
    /// <param name="targetFrameRate">Render loop rate.</param>
    public EditorHostService(string webRoot, int width = 1280, int height = 720, int targetFrameRate = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webRoot);

        _webRoot = webRoot;
        _width = width;
        _height = height;
        _targetFrameRate = targetFrameRate;

        // One port for the whole app: the viewport's socket reconnects to the same URL when a session is
        // swapped, so switching projects does not have to re-mount the canvas.
        _port = FindFreePort();

        OpenLogFile();
        LogEnvironment();
        SubscribeToEngineLog();
    }

    /// <summary>Project the launcher should open on its first render, from the command line. Cleared after use.</summary>
    public string? PendingProject { get; set; }

    /// <summary>
    /// Native folder picker, supplied by the host once the window exists. Null when unavailable, and the
    /// launcher then falls back to typed paths.
    /// </summary>
    public Func<string, string?, Task<string[]>>? FolderPicker { get; set; }

    /// <summary>Closes the window; supplied by the host once the window exists (File &gt; Exit).</summary>
    public Action? CloseRequested { get; set; }

    /// <summary>The log the console panel shows, oldest first.</summary>
    public IReadOnlyList<EditorLogEntry> LogEntries
    {
        get
        {
            lock (_logLock)
            {
                return _log.ToArray();
            }
        }
    }

    /// <summary>Current session state.</summary>
    public EditorSessionState State => _state;

    /// <summary>Root of the open project, or null for the demo scene.</summary>
    public string? ProjectRoot => _projectRoot;

    /// <summary>Name shown for the open session: the project name, or the demo scene's.</summary>
    public string SessionName => _sessionName;

    /// <summary>True when a project (rather than the demo scene) is open.</summary>
    public bool HasProject => _projectRoot is not null;

    /// <summary>URL of the current session's host, or null while nothing is listening.</summary>
    public string? Url => _session?.Host.Url;

    /// <summary>WebSocket endpoint the viewport script connects to for frames.</summary>
    public string WebSocketUrl
    {
        get
        {
            string? url = _session?.Host.Url;

            if (url is null)
            {
                return string.Empty;
            }

            Uri parsed = new(url);

            UriBuilder builder = new(parsed)
            {
                Scheme = parsed.Scheme == "https" ? "wss" : "ws",
                Path = "/ws"
            };

            return builder.Uri.ToString();
        }
    }

    /// <summary>Most recent scene snapshot from the render thread, or null before the first frame.</summary>
    public SceneSnapshot? LatestScene => _session?.Host.LatestScene;

    /// <summary>Human-readable reason the host is not usable, or null when it is fine.</summary>
    public string? FailureMessage => _failure;

    /// <summary>The exception behind <see cref="FailureMessage"/>, verbatim, or null.</summary>
    public string? FailureDetail => _failureDetail ?? _session?.Host.FailureDetail;

    /// <summary>Renderer, vendor and GL version, or null while the renderer is starting.</summary>
    public string? RendererInfo => _session?.Host.RendererInfo;

    /// <summary>Where the diagnostics log is written, or null when no file could be created.</summary>
    public string? LogFilePath => _logFilePath;

    /// <summary>The components the inspector's Add Component menu offers, in menu order.</summary>
    public IReadOnlyList<Cherris.Components.ComponentCatalog.Entry> ComponentTypes =>
        Cherris.Components.ComponentCatalog.BuiltIn;

    /// <summary>Recent projects, most recent first, with their state resolved for display.</summary>
    public IReadOnlyList<RecentProjectEntry> RecentProjects => _recent.Get()
        .Select(path => new RecentProjectEntry(path, ProjectWorkspace.DisplayName(path), ProjectWorkspace.Exists(path)))
        .ToArray();

    /// <summary>The environment report a user can copy out of the window.</summary>
    public string Diagnostics => HostDiagnostics.Build(new HostDiagnosticsInfo(
        Runtime: $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} " +
            $"({RuntimeInformation.ProcessArchitecture})",
        BaseDirectory: AppContext.BaseDirectory,
        WebRoot: _webRoot,
        WebRootExists: Directory.Exists(_webRoot),
        EditorResourcesPresent: Directory.Exists(Path.Combine(AppContext.BaseDirectory, "EditorResources")),
        HostUrl: _session?.Host.Url,
        RendererInfo: _session?.Host.RendererInfo,
        Failure: FailureDetail,
        LogFilePath: _logFilePath));

    /// <summary>
    /// Opens a project folder: validates it, remembers it in the shared history and starts a session that
    /// renders the project's start scene. Returns false when the project could not be opened; the reason is
    /// in <see cref="FailureMessage"/>.
    /// </summary>
    public async Task<bool> OpenProjectAsync(string projectRoot, bool rememberInHistory = true)
    {
        string root;
        ProjectConfig project;

        try
        {
            root = ProjectWorkspace.Validate(projectRoot);
            project = ProjectConfig.Load(root);
        }
        catch (Exception exception)
        {
            Fail($"Could not open that project: {exception.Message}", exception.ToString());
            return false;
        }

        if (rememberInHistory)
        {
            _recent.Remember(root);
        }

        WriteLog($"[editor] opening project '{project.Name}' at {root}");

        return await StartSessionAsync(EditorSceneRequest.FromProject(project), root).ConfigureAwait(true);
    }

    /// <summary>Starts a session that renders the built-in demo scene.</summary>
    public Task<bool> OpenDemoSceneAsync()
    {
        WriteLog("[editor] opening the built-in demo scene");

        return StartSessionAsync(EditorSceneRequest.Demo, null);
    }

    /// <summary>True when there is an edit to take back.</summary>
    public bool CanUndo => _session?.Host.History.CanUndo ?? false;

    /// <summary>True when there is an edit to re-apply.</summary>
    public bool CanRedo => _session?.Host.History.CanRedo ?? false;

    /// <summary>What Undo would take back, for the menu's label, or null.</summary>
    public string? UndoLabel => _session?.Host.History.UndoLabel;

    /// <summary>What Redo would re-apply, for the menu's label, or null.</summary>
    public string? RedoLabel => _session?.Host.History.RedoLabel;

    /// <summary>
    /// Adds a component to an object in the live scene. The render thread applies it on its next frame, and
    /// the snapshot the panels read picks it up from there; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to add to.</param>
    /// <param name="componentName">One of <see cref="ComponentTypes"/>' display names.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddComponent(Guid objectId, string componentName) =>
        Enqueue(new AddComponentCommand(objectId, componentName), "add " + componentName);

    /// <summary>Writes one property of a component from the inspector's text.</summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="text">The new value, in the inspector's text format.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetProperty(Guid objectId, string componentTypeName, string propertyName, string text) =>
        Enqueue(
            new SetPropertyCommand(objectId, componentTypeName, propertyName, text),
            $"set {componentTypeName}.{propertyName}");

    /// <summary>Writes one transform row of an object.</summary>
    /// <param name="objectId">The object.</param>
    /// <param name="transformField">Position, rotation (Euler degrees) or scale.</param>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetTransform(Guid objectId, TransformField transformField, float x, float y, float z) =>
        Enqueue(
            new SetTransformCommand(objectId, transformField, new System.Numerics.Vector3(x, y, z)),
            "move the object");

    /// <summary>Renames an object.</summary>
    /// <param name="objectId">The object.</param>
    /// <param name="name">The new name.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool RenameObject(Guid objectId, string name) =>
        Enqueue(new RenameObjectCommand(objectId, name), "rename the object");

    /// <summary>Takes the newest edit back.</summary>
    /// <returns>False when there is nothing to undo, or no session.</returns>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        return Enqueue(new UndoCommand(), "undo");
    }

    /// <summary>Re-applies the newest undone edit.</summary>
    /// <returns>False when there is nothing to redo, or no session.</returns>
    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        return Enqueue(new RedoCommand(), "redo");
    }

    /// <summary>Hands an edit to the render thread, or says why it could not.</summary>
    private bool Enqueue(CherrisEditorWeb.Host.IEditorCommand command, string action)
    {
        WebEditorHost? host = _session?.Host;

        if (host is null)
        {
            WriteLog($"[editor] cannot {action}: no session is running.", LogLevel.Warning);
            return false;
        }

        host.Enqueue(command);
        return true;
    }

    /// <summary>Stops the session and drops the project reference, leaving the launcher in charge.</summary>
    public void CloseSession()
    {
        Close();
        _projectRoot = null;
        _sessionName = EditorSceneRequest.Demo.DisplayName;
        _failure = null;
        _failureDetail = null;
        _state = EditorSessionState.Idle;
    }

    /// <summary>Removes a project from the launcher's history without touching its files.</summary>
    public void ForgetProject(string projectRoot)
    {
        try
        {
            _recent.Forget(projectRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteLog($"[editor] could not update the recent projects: {exception.Message}");
        }
    }

    /// <summary>Shows the native folder picker. Returns null when it is unavailable or the user cancels.</summary>
    public async Task<string?> PickFolderAsync(string title, string? startIn)
    {
        Func<string, string?, Task<string[]>>? picker = FolderPicker;

        if (picker is null)
        {
            return null;
        }

        string[] folders = await picker(title, startIn).ConfigureAwait(true);

        return folders.Length > 0 ? folders[0] : null;
    }

    /// <summary>Writes a diagnostic line to the console, the in-memory log and the log file.</summary>
    public void WriteLog(string message) => WriteLog(message, LogLevel.Info);

    /// <inheritdoc cref="WriteLog(string)" />
    public void WriteLog(string message, LogLevel level)
    {
        EditorLogEntry entry = new(DateTimeOffset.Now, level, message);

        lock (_logLock)
        {
            _log.Enqueue(entry);

            while (_log.Count > MaximumLogLines)
            {
                _log.Dequeue();
            }
        }

        Console.WriteLine(message);

        string line = $"{entry.Timestamp:HH:mm:ss.fff} [{level}] {message}";

        string? path = _logFilePath;

        if (path is null)
        {
            return;
        }

        try
        {
            lock (_logLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the editor down; the file is a convenience.
        }
    }

    /// <summary>Last <paramref name="lineCount"/> log lines as text, oldest first.</summary>
    public string LogTail(int lineCount = 30)
    {
        lock (_logLock)
        {
            int skip = Math.Max(0, _log.Count - Math.Max(1, lineCount));

            return string.Join(Environment.NewLine, _log.Skip(skip)
                .Select(entry => $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] {entry.Message}"));
        }
    }

    /// <summary>Empties the console. The log file keeps its history.</summary>
    public void ClearLog()
    {
        lock (_logLock)
        {
            _log.Clear();
        }
    }

    /// <summary>Stops the session and releases everything it owns.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Logger.OnMessageLogged -= OnEngineLog;
        Close();
        WriteLog("[editor] stopped");
        GC.SuppressFinalize(this);
    }

    private async Task<bool> StartSessionAsync(EditorSceneRequest scene, string? projectRoot)
    {
        Close();

        _state = EditorSessionState.Starting;
        _failure = null;
        _failureDetail = null;
        _projectRoot = projectRoot;
        _sessionName = scene.DisplayName;

        // GLFW has to be initialised on the main thread, which is where this method's synchronous part runs
        // (Blazor's dispatcher). The render thread takes over the context below.
        HeadlessBackend backend = new();

        try
        {
            backend.CreateGraphicsContext("Cherris Editor", ContextWidth, ContextHeight);
            GLFWProvider.CheckForMainThread = false;
            WriteLog("[editor] hidden GL context created on the main thread");
        }
        catch (Exception exception)
        {
            backend.Dispose();
            Fail("The graphics context could not be created on this machine.", exception.ToString());
            return false;
        }

        WebEditorHost host = new(CreateOptions(), _webRoot, backend, WriteLog, scene);
        ManualResetEventSlim exited = new(false);

        // The thread body needs the session, so the closure reads it after it has been assigned.
        Session? session = null;
        Thread worker = new(() => RunHost(session!))
        {
            IsBackground = true,
            Name = "Cherris Engine"
        };

        session = new Session(host, backend, exited);
        _session = session;
        worker.Start();

        // Waiting off the dispatcher keeps the launcher responsive; the hand-off is already done, so the
        // continuation does not need the main thread.
        await Task.Run(() => host.WaitForStart(TimeSpan.FromSeconds(StartTimeoutSeconds))).ConfigureAwait(true);

        if (host.StartFailed)
        {
            Fail($"The editor host could not listen on port {_port}.", host.FailureDetail);
            return false;
        }

        _state = EditorSessionState.Running;
        WriteLog($"[editor] session running at {host.Url}");

        return true;
    }

    private void Close()
    {
        Session? session = _session;

        if (session is null)
        {
            return;
        }

        // Drop the reference first: the finishing render thread checks that it still owns the session before
        // writing state, so a late exit cannot overwrite the next session's state.
        _session = null;

        session.Host.RequestStop();

        if (!session.Exited.Wait(TimeSpan.FromSeconds(StopTimeoutSeconds)))
        {
            WriteLog("[editor] render thread did not stop within " +
                $"{StopTimeoutSeconds} seconds; abandoning it");
        }

        session.Host.Dispose();
        session.Backend.Dispose();
        session.Exited.Dispose();

        WriteLog("[editor] session closed");
    }

    private void RunHost(Session session)
    {
        try
        {
            int exitCode = session.Host.Run();

            WriteLog($"[editor] host exited with code {exitCode}");

            if (exitCode != 0 && ReferenceEquals(_session, session))
            {
                _failure = "The renderer stopped.";
                _state = EditorSessionState.Failed;
            }
        }
        catch (Exception exception)
        {
            WriteLog($"[editor] host failed: {exception}");

            if (ReferenceEquals(_session, session))
            {
                Fail("The editor host threw an exception.", exception.ToString());
            }
        }
        finally
        {
            session.Exited.Set();
        }
    }

    private void Fail(string message, string? detail)
    {
        _state = EditorSessionState.Failed;
        _failure = message;
        _failureDetail = detail;
        WriteLog($"[editor] {message}{(detail is null ? string.Empty : $" {detail}")}", LogLevel.Error);
    }

    private WebEditorOptions CreateOptions() => new()
    {
        Port = _port,
        Width = _width,
        Height = _height,
        TargetFrameRate = _targetFrameRate,

        // The window is the UI; opening a browser as well would be a second, unrelated viewport.
        OpenBrowser = false
    };

    private void OpenLogFile()
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cherris");

            Directory.CreateDirectory(directory);

            _logFilePath = Path.Combine(directory, "editor-host.log");
            File.WriteAllText(
                _logFilePath,
                $"Cherris editor host, started {DateTimeOffset.Now:O}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logFilePath = null;
        }
    }

    private void LogEnvironment()
    {
        WriteLog($"[editor] starting, process {Environment.ProcessId}, {Environment.Version} " +
            $"on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        WriteLog($"[editor] base directory: {AppContext.BaseDirectory}");
        WriteLog($"[editor] web root: {_webRoot} ({(Directory.Exists(_webRoot) ? "present" : "missing")})");
        WriteLog($"[editor] recent projects: {_recent.HistoryDirectory}");

        if (_logFilePath is not null)
        {
            WriteLog($"[editor] log file: {_logFilePath}");
        }
    }

    private void SubscribeToEngineLog()
    {
        Logger.OnMessageLogged += OnEngineLog;
    }

    private void OnEngineLog(LogMessage message)
    {
        WriteLog(message.Message, message.Level);
    }

    /// <summary>Reserves a port by binding to port 0, so two editor instances never collide.</summary>
    private static int FindFreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);

        try
        {
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>One render session: the host, its GL context and the signal its thread sets when it ends.</summary>
    private sealed class Session(
        WebEditorHost host,
        HeadlessBackend backend,
        ManualResetEventSlim exited)
    {
        public WebEditorHost Host { get; } = host;

        public HeadlessBackend Backend { get; } = backend;

        public ManualResetEventSlim Exited { get; } = exited;
    }
}

/// <summary>One line of the editor log the console panel renders.</summary>
/// <param name="Timestamp">When the line was produced.</param>
/// <param name="Level">Info, warning or error.</param>
/// <param name="Message">The text.</param>
public sealed record EditorLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

/// <summary>Viewport numbers the status bar shows, read from the JavaScript bridge.</summary>
/// <param name="Connected">True while the frame socket is open.</param>
/// <param name="Fps">Frames displayed per second, averaged over the last sample window.</param>
/// <param name="Width">Width of the most recent frame in pixels.</param>
/// <param name="Height">Height of the most recent frame in pixels.</param>
/// <param name="Frames">Total frames displayed since the viewport mounted.</param>
/// <param name="Status">Bridge state: connecting, connected, disconnected, error or idle.</param>
/// <param name="Message">Message the bridge shows over the canvas, or an empty string.</param>
public sealed record ViewportStats(
    bool Connected,
    int Fps,
    int Width,
    int Height,
    long Frames,
    string Status,
    string Message);
