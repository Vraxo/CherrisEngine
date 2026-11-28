using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Projects;
using Overcore.Scripting;
using Overcore.Utils;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OvercoreEditorPhotino.Services;

/// <summary>
/// Owns the editor's render session: the GL context, the render thread, the HTTP/WebSocket listener and the
/// latest scene snapshot, plus the project it was opened from.
/// </summary>
/// <remarks>
/// <para>
/// The window's main thread belongs to the native Photino message loop and to Blazor's dispatcher, so a session
/// is started with <see cref="EditorSessionHost.Start"/>, which creates the graphics context on the thread that calls
/// it — this one — and runs the engine on its own background thread. That is the opposite of the browser host,
/// where <see cref="EditorSessionHost.Run"/> may own the main thread. Which thread may do what is the host's
/// business: nothing here has to know about GLFW.
/// </para>
/// <para>
/// Switching projects stops the session and starts a new one: the scene, the project's asset root and the
/// GL resources of the previous project do not have to be untangled that way, and opening a project is a
/// user-initiated action where a second of work is invisible.
/// </para>
/// </remarks>
/// <remarks>
/// The edit surface is in <c>EditorHostService.Editing.cs</c>, the project and file surface in
/// <c>EditorHostService.ProjectFiles.cs</c>, and the console log in <c>EditorHostService.Logging.cs</c>.
/// </remarks>
public sealed partial class EditorHostService : IDisposable
{
    private const int MaximumLogLines = 400;
    private const int StartTimeoutSeconds = 20;

    private readonly string _webRoot;
    private readonly int _port;
    private readonly int _width;
    private readonly int _height;
    private readonly int _targetFrameRate;
    private readonly Overcore.Projects.RecentProjects _recent = new();
    private readonly Queue<EditorLogEntry> _log = new();
    private readonly object _logLock = new();

    private EditorSessionHost? _session;
    private volatile EditorSessionState _state = EditorSessionState.Idle;
    private volatile string? _failure;
    private volatile string? _failureDetail;
    private volatile string? _projectRoot;

    // What the transport was last asked to do. The render thread applies it at the top of its next frame,
    // which is one frame away, so the toolbar reads this rather than waiting for a snapshot to come back.
    private volatile EditorPlayState _playState = EditorPlayState.Editing;

    // What the pointer is dragging: the command it feeds, and a key that stops a different field in the same
    // inspector from feeding it too.
    private string? _liveKey;
    private IEditorCommand? _liveCommand;
    private bool _showGrid = true;
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

    /// <summary>
    /// Where a pick from the viewport goes — normally <c>SceneSelection.Select</c>. Called on the render thread,
    /// so the target has to be safe to call from there; when nothing is registered, picks are ignored.
    /// </summary>
    public Action<Guid?>? SelectionSink { get; set; }

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
    public string? Url => _session?.Url;

    /// <summary>WebSocket endpoint the viewport script connects to for frames.</summary>
    public string WebSocketUrl
    {
        get
        {
            string? url = _session?.Url;

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
    public SceneSnapshot? LatestScene => _session?.LatestScene;

    /// <summary>
    /// Whether the viewport draws the editor's grid. The render loop reads it from the session's host, so a new
    /// session is told where the toggle stood rather than starting over.
    /// </summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            _session?.ShowGrid = value;
        }
    }

    /// <summary>
    /// What the transport is doing: the last action asked for, which the render thread carries out a frame
    /// later. Editing whenever no session is running.
    /// </summary>
    public EditorPlayState PlayState => _session is null ? EditorPlayState.Editing : _playState;

    /// <summary>Starts the scene running, or carries on after a pause.</summary>
    public bool Play() => RequestPlayback(PlaybackRequest.Play, EditorPlayState.Playing);

    /// <summary>Freezes the scene where it is; only a running scene can be paused.</summary>
    public bool Pause() => RequestPlayback(PlaybackRequest.Pause, EditorPlayState.Paused);

    /// <summary>Puts the scene back to how it was authored and returns to editing.</summary>
    public bool Stop() => RequestPlayback(PlaybackRequest.Stop, EditorPlayState.Editing);

    /// <summary>Puts the scene back and plays it again.</summary>
    public bool Restart() => RequestPlayback(PlaybackRequest.Restart, EditorPlayState.Playing);

    /// <summary>Human-readable reason the host is not usable, or null when it is fine.</summary>
    public string? FailureMessage => _failure;

    /// <summary>The exception behind <see cref="FailureMessage"/>, verbatim, or null.</summary>
    public string? FailureDetail => _failureDetail ?? _session?.FailureDetail;

    /// <summary>Renderer, vendor and GL version, or null while the renderer is starting.</summary>
    public string? RendererInfo => _session?.RendererInfo;

    /// <summary>
    /// Something the session had to say about the scene it opened — an empty file, a camera it had to add, a
    /// file that is not a scene — or null when the scene opened exactly as it is on disk.
    /// </summary>
    public string? SceneNotice => _session?.SceneNotice;

    /// <summary>
    /// The components the inspector's Add Component menu offers, in menu order: the built-in ones, then the open
    /// project's compiled C# scripts. A script is only here once the session has compiled it, which is why this is
    /// read from the session rather than from the catalog.
    /// </summary>
    public IReadOnlyList<Overcore.Components.ComponentCatalog.Entry> ComponentTypes
    {
        get
        {
            IReadOnlyList<Overcore.Components.ComponentCatalog.Entry> scripts =
                _session?.ScriptComponents ?? [];

            return scripts.Count == 0
                ? Overcore.Components.ComponentCatalog.BuiltIn
                : [.. Overcore.Components.ComponentCatalog.BuiltIn, .. scripts];
        }
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

        // Every session starts in editing: play mode belongs to the scene, and this is a new one.
        _playState = EditorPlayState.Editing;
        _state = EditorSessionState.Starting;
        _failure = null;
        _failureDetail = null;
        _projectRoot = projectRoot;
        _sessionName = scene.DisplayName;

        EditorSessionHost host = new(CreateOptions(), _webRoot, log: WriteLog, scene: scene);

        // A viewport click is picked on the render thread; this is the hand-off to the UI's selection.
        host.ObjectPicked += OnObjectPicked;

        // The viewport's options belong to the UI and outlive a session, so a new host starts where the last one
        // left off instead of drawing a grid the user turned off.
        host.ShowGrid = _showGrid;
        host.Exited += exitCode => OnSessionExited(host, exitCode);

        _session = host;

        // The context is made here, on Blazor's dispatcher, because OpenTK only initialises GLFW on the process'
        // main thread; the host puts the render loop on its own thread and says which step failed if one does.
        if (!host.Start())
        {
            Fail(
                host.StartFailure == EditorStartFailure.Graphics
                    ? "The graphics context could not be created on this machine."
                    : $"The editor host could not listen on port {_port}.",
                host.FailureDetail);

            return false;
        }

        // Waiting off the dispatcher keeps the launcher responsive; the listener is already up.
        await Task.Run(() => host.WaitForStart(TimeSpan.FromSeconds(StartTimeoutSeconds))).ConfigureAwait(true);

        _state = EditorSessionState.Running;
        WriteLog($"[editor] session running at {host.Url}");

        return true;
    }

    private void Close()
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            return;
        }

        // Drop the reference first: the finishing render thread checks that it still owns the session before
        // writing state, so a late exit cannot overwrite the next session's state.
        _session = null;
        _playState = EditorPlayState.Editing;

        // Stops the loop, gives it a bounded chance to finish, releases the graphics context and destroys the
        // hidden window — in that order, which is what the host exists to get right.
        host.Dispose();

        WriteLog("[editor] session closed");
    }

    /// <summary>
    /// What the render thread says when a session ends. A non-zero code means the loop failed, and the window
    /// shows it the same way a failed start is shown.
    /// </summary>
    /// <param name="host">The session that ended.</param>
    /// <param name="exitCode">Its exit code.</param>
    private void OnSessionExited(EditorSessionHost host, int exitCode)
    {
        WriteLog($"[editor] host exited with code {exitCode}");

        if (exitCode != 0 && ReferenceEquals(_session, host))
        {
            _failure = "The renderer stopped.";
            _state = EditorSessionState.Failed;
        }
    }

    /// <summary>
    /// Applies a pick that came from the render thread to the UI's selection. The host already keeps a throwing
    /// handler from ending the render loop, so this stays a plain hand-off.
    /// </summary>
    private void OnObjectPicked(Guid? id) => SelectionSink?.Invoke(id);

    /// <summary>
    /// Hands a transport action to the render thread and remembers the state it leads to. The engine applies
    /// it on the next frame and logs what it did, so the console always tells the same story as the toolbar.
    /// </summary>
    private bool RequestPlayback(PlaybackRequest request, EditorPlayState stateAfter)
    {
        EditorSessionHost? host = _session;

        if (host is null || !host.RequestPlayback(request))
        {
            WriteLog("[play] There is no running session to play.", LogLevel.Warning);

            return false;
        }

        _playState = stateAfter;

        return true;
    }

    private void Fail(string message, string? detail)
    {
        _state = EditorSessionState.Failed;
        _failure = message;
        _failureDetail = detail;
        WriteLog($"[editor] {message}{(detail is null ? string.Empty : $" {detail}")}", LogLevel.Error);
    }

    private EditorSessionOptions CreateOptions() => new()
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
                "Overcore");

            Directory.CreateDirectory(directory);

            _logFilePath = Path.Combine(directory, "editor-host.log");
            File.WriteAllText(
                _logFilePath,
                $"Overcore editor host, started {DateTimeOffset.Now:O}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logFilePath = null;
        }
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
}
