using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Projects;
using Overcore.Scripting;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OvercoreEditorPhotino.Services;

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
public sealed class EditorHostService : IDisposable
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

    /// <summary>Where the diagnostics log is written, or null when no file could be created.</summary>
    public string? LogFilePath => _logFilePath;

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
        HostUrl: _session?.Url,
        RendererInfo: _session?.RendererInfo,
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

    /// <summary>
    /// Asks the open session to load another scene file in place of the one it has. The session itself stays — the
    /// viewport keeps its connection and the panels keep polling — so this is a scene change, not a restart.
    /// </summary>
    /// <param name="scenePath">Absolute path of a scene file, normally inside the open project.</param>
    /// <returns>False when there is no session to ask; the reason is in <see cref="FailureMessage"/>.</returns>
    public bool OpenScene(string scenePath)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            // Not a failure of the session: there simply is not one. The menus that offer this need a session to be
            // enabled in the first place, so this is a race, not a state to report.
            WriteLog("[editor] there is no session to open a scene in", LogLevel.Warning);
            return false;
        }

        // The selection names an object in the scene that is going away, so it cannot survive the switch.
        SelectionSink?.Invoke(null);

        // Play mode belongs to the scene: the render thread stops it, and the toolbar has to say so too.
        _playState = EditorPlayState.Editing;

        if (!host.RequestSceneOpen(scenePath))
        {
            WriteLog("[editor] the session is shutting down, so the scene was not opened", LogLevel.Warning);
            return false;
        }

        WriteLog($"[editor] opening '{scenePath}'");

        return true;
    }

    /// <summary>
    /// Writes a new scene into the open project and opens it. The file is written first, and everything that can go
    /// wrong — no project, a name that is not a file name, a scene that already exists, a file that cannot be
    /// opened — comes back as text for the dialog to show, so nothing is created silently or left half-done.
    /// </summary>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="error">Why no scene was made, when this returns false.</param>
    public bool TryCreateScene(string name, out string? error) => CreateScene(name, null, out error) is not null;

    /// <summary>
    /// Writes a new scene into a folder of the open project and opens it.
    /// </summary>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="directory">Folder to write into, or null for the project root.</param>
    /// <param name="error">Why no scene was made, when this returns null.</param>
    /// <returns>The path of the file, or null when nothing was written.</returns>
    public string? CreateScene(string name, string? directory, out string? error)
    {
        if (!ResolveDirectory("a scene", directory, out string folder, out error))
        {
            return null;
        }

        string path;

        try
        {
            path = SceneTemplate.Write(folder, name);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return null;
        }

        WriteLog($"[editor] created '{path}'");

        if (!OpenScene(path))
        {
            // The file is on disk and says so; only opening it failed, so the dialog reports that rather than
            // pretending the scene was never made.
            error = $"'{Path.GetFileName(path)}' was created, but the session could not open it.";
            return null;
        }

        return path;
    }

    /// <summary>
    /// Writes a new script into the project's <c>Scripts/</c> folder, in either language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder is not a choice: a script kept anywhere else is one the engine will never run, so a browser that
    /// let one be written into <c>Levels/</c> would be making a dead file.
    /// </para>
    /// <para>
    /// A C# file is compiled straight away. Nothing is attached here — the browser only made a file — but the Add
    /// Component menu should offer the type it declares as soon as the dialog closes, and it only knows types the
    /// session has compiled.
    /// </para>
    /// </remarks>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="kind">Lua or C#, which decides the extension and the source.</param>
    /// <param name="error">Why no script was made, when this returns null.</param>
    /// <returns>The path of the file, or null when nothing was written.</returns>
    public string? CreateScript(string name, ScriptKind kind, out string? error)
    {
        error = null;

        if (_projectRoot is not { } projectRoot)
        {
            error = "Open a project before making a script: a script is a file inside one.";
            return null;
        }

        if (!ScriptTemplate.TryCreate(projectRoot, name, kind, out string relativePath, out error))
        {
            return null;
        }

        if (kind == ScriptKind.CSharp)
        {
            _session?.RequestScriptReload();
        }

        string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        WriteLog($"[editor] created '{path}'");

        return path;
    }

    /// <summary>
    /// Creates a folder inside the open project.
    /// </summary>
    /// <param name="name">What the user typed; a name, not a path.</param>
    /// <param name="directory">Folder to create it in, or null for the project root.</param>
    /// <param name="error">Why no folder was made, when this returns null.</param>
    /// <returns>The path of the folder, or null when nothing was created.</returns>
    public string? CreateFolder(string name, string? directory, out string? error)
    {
        if (!ProjectFileName.TryNormalize(name, out string folderName, out string nameError))
        {
            error = nameError;
            return null;
        }

        if (!ResolveDirectory("a folder", directory, out string parent, out error))
        {
            return null;
        }

        string path = Path.Combine(parent, folderName);

        if (Directory.Exists(path) || File.Exists(path))
        {
            error = $"There is already something called {folderName} here.";
            return null;
        }

        try
        {
            // A folder holds no contents of its own, so there is nothing to write, only somewhere to put things.
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"Could not create {folderName}: {exception.Message}";
            return null;
        }

        WriteLog($"[editor] created '{path}'");

        return path;
    }

    /// <summary>
    /// The folder an item is to be created in: the project root when none was named, and never anything outside it.
    /// </summary>
    /// <param name="what">What is being created, for the messages.</param>
    /// <param name="directory">The folder asked for, or null.</param>
    /// <param name="folder">The absolute folder to write into, when this returns true.</param>
    /// <param name="error">Why the folder cannot be used, when this returns false.</param>
    private bool ResolveDirectory(string what, string? directory, out string folder, out string? error)
    {
        folder = string.Empty;
        error = null;

        if (_projectRoot is not { } projectRoot)
        {
            error = $"Open a project before making {what}: everything the editor writes lives inside one.";
            return false;
        }

        string root = Path.GetFullPath(projectRoot);

        if (string.IsNullOrWhiteSpace(directory))
        {
            folder = root;
            return true;
        }

        string candidate;

        try
        {
            candidate = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            error = $"'{directory}' is not a folder the editor can write to.";
            return false;
        }

        // The browser only ever shows one project's folder, but the path arrives from a click: it is checked here
        // rather than trusted, so a created file cannot land outside the project it belongs to.
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Files can only be created inside the open project.";
            return false;
        }

        folder = candidate;

        return true;
    }

    /// <summary>True when there is an edit to take back.</summary>
    public bool CanUndo => _session?.History.CanUndo ?? false;

    /// <summary>True when there is an edit to re-apply.</summary>
    public bool CanRedo => _session?.History.CanRedo ?? false;

    /// <summary>What Undo would take back, for the menu's label, or null.</summary>
    public string? UndoLabel => _session?.History.UndoLabel;

    /// <summary>What Redo would re-apply, for the menu's label, or null.</summary>
    public string? RedoLabel => _session?.History.RedoLabel;

    /// <summary>
    /// Adds a component to an object in the live scene. The render thread applies it on its next frame, and
    /// the snapshot the panels read picks it up from there; the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to add to.</param>
    /// <param name="componentName">One of <see cref="ComponentTypes"/>' display names.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddComponent(Guid objectId, string componentName) =>
        Enqueue(new AddComponentCommand(objectId, componentName), "add " + componentName);

    /// <summary>
    /// Adds an empty object at the scene root, as the hierarchy's create menu sends: Unity's "Create Empty".
    /// The render thread applies it on its next frame, and the snapshot the panels read picks it up from there;
    /// the outcome is logged to the console.
    /// </summary>
    /// <param name="name">The name the object is born with.</param>
    /// <param name="objectId">
    /// Set to the id the object is created with, so the caller can select it before the snapshot catches up.
    /// </param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool CreateGameObject(string name, out Guid objectId)
    {
        objectId = Guid.NewGuid();
        return Enqueue(new CreateGameObjectCommand(objectId, name), "create " + name);
    }

    /// <summary>
    /// Takes an object and everything under it out of the scene, as the hierarchy's context menu sends:
    /// Unity's "Delete". The subtree comes back whole on Undo. The render thread applies it on its next
    /// frame, and the outcome is logged to the console.
    /// </summary>
    /// <param name="objectId">The object to take out.</param>
    /// <param name="name">Its name, so the step can be named.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool DeleteGameObject(Guid objectId, string name) =>
        Enqueue(new DeleteGameObjectCommand(objectId, name), "delete " + name);

    /// <summary>
    /// Switches a component on or off, as the inspector's header checkbox sends. Off stops the component — a
    /// renderer stops drawing, a light stops lighting, a script stops ticking — without taking it off the object,
    /// and the state is saved with the scene. One click is one undo step.
    /// </summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="enabled">Where the checkbox goes.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetComponentEnabled(Guid objectId, string componentTypeName, bool enabled, Action<long>? completed = null)
    {
        // A drag that is still running ends here, as it does before any other discrete edit.
        EndLiveEdit();

        EditorSessionHost? host = _session;
        if (host is null)
        {
            WriteLog($"[editor] cannot {(enabled ? "enable" : "disable")} {componentTypeName}: no session is running.", LogLevel.Warning);
            return false;
        }

        // This runs before the frame's snapshot is published. The UI can ignore older snapshots until it
        // sees one newer than this frame, including when the command was refused or threw.
        IEditorCommand command = WrapForCompletion(
            new SetComponentEnabledCommand(objectId, componentTypeName, enabled), host, completed);

        host.Enqueue(command);
        return true;
    }

    /// <summary>
    /// Writes one property of a component while the author drags it. Consecutive calls for the same property
    /// feed a single command, so the scene follows the pointer every frame while the whole drag stays one undo
    /// step. The panel calls <see cref="EndLiveEdit"/> when a drag begins, a keystroke or a menu edit arrives, so
    /// neither two drags nor a drag and the value typed after it are ever the same step.
    /// </summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="text">The value the pointer is on, in the inspector's text format.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetPropertyLive(Guid objectId, string componentTypeName, string propertyName, string text, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog("[editor] cannot set a property: no session is running.", LogLevel.Warning);
            return false;
        }

        string key = $"property:{objectId:N}:{componentTypeName}.{propertyName}";

        if (_liveCommand is SetPropertyCommand live && String.Equals(_liveKey, key, StringComparison.Ordinal))
        {
            live.Text = text;
            host.Enqueue(WrapForCompletion(live, host, completed));
            return true;
        }

        SetPropertyCommand command = new(objectId, componentTypeName, propertyName, text);

        _liveKey = key;
        _liveCommand = command;
        host.Enqueue(WrapForCompletion(command, host, completed));

        return true;
    }

    /// <summary>
    /// Writes one transform row while the author drags it, feeding a single command the way
    /// <see cref="SetPropertyLive"/> does for a property.
    /// </summary>
    /// <param name="objectId">The object.</param>
    /// <param name="transformField">Position, rotation (Euler degrees) or scale.</param>
    /// <param name="x">The row's first component, which is the one the pointer is usually on.</param>
    /// <param name="y">The row's second component.</param>
    /// <param name="z">The row's third component.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetTransformLive(Guid objectId, TransformField transformField, float x, float y, float z, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog("[editor] cannot move the object: no session is running.", LogLevel.Warning);
            return false;
        }

        System.Numerics.Vector3 value = new(x, y, z);
        string key = $"transform:{objectId:N}:{transformField}";

        if (_liveCommand is SetTransformCommand live && String.Equals(_liveKey, key, StringComparison.Ordinal))
        {
            live.Value = value;
            host.Enqueue(WrapForCompletion(live, host, completed));
            return true;
        }

        SetTransformCommand command = new(objectId, transformField, value);

        _liveKey = key;
        _liveCommand = command;
        host.Enqueue(WrapForCompletion(command, host, completed));

        return true;
    }

    /// <summary>
    /// Ends the drag: the value the pointer is on is already queued, so this only closes the session, and the
    /// next edit of that property records a new step instead of joining the drag that just finished.
    /// </summary>
    public void EndLiveEdit()
    {
        _liveKey = null;
        _liveCommand = null;
    }

    /// <summary>Writes one property of a component from the inspector's text.</summary>
    /// <param name="objectId">The object that owns the component.</param>
    /// <param name="componentTypeName">The component's type name, as the snapshot reports it.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="text">The new value, in the inspector's text format.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetProperty(Guid objectId, string componentTypeName, string propertyName, string text, Action<long>? completed = null)
    {
        // A field that is no longer being dragged starts its own step.
        EndLiveEdit();

        return Enqueue(
            new SetPropertyCommand(objectId, componentTypeName, propertyName, text),
            $"set {componentTypeName}.{propertyName}",
            completed);
    }

    /// <summary>
    /// Adds a lua script to an object, creating the file in the project's <c>Scripts/</c> folder when it is not
    /// there yet. The render thread does the writing, so the UI thread never touches the project folder.
    /// </summary>
    /// <param name="objectId">The object to add the script to.</param>
    /// <param name="scriptName">The name the author typed; a <c>.lua</c> extension is forgiven.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddScript(Guid objectId, string scriptName) =>
        Enqueue(new AddScriptCommand(objectId, scriptName), "add script " + scriptName);

    /// <summary>
    /// Adds a C# script to an object: the file is written, the project is compiled and the component is attached, in
    /// that order and on the render thread, because a scene may only name a type the session has compiled.
    /// </summary>
    /// <param name="objectId">The object to add the script to.</param>
    /// <param name="scriptName">The name the author typed; a <c>.cs</c> extension is forgiven.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool AddCSharpScript(Guid objectId, string scriptName) =>
        Enqueue(new AddCSharpScriptCommand(objectId, scriptName), "add C# script " + scriptName);

    /// <summary>True when the open project already holds a script file with this name, in this language.</summary>
    /// <param name="scriptName">The name the author typed.</param>
    /// <param name="kind">Which language's file is looked for.</param>
    public bool ScriptExists(string? scriptName, ScriptKind kind) =>
        ScriptTemplate.Exists(_projectRoot, scriptName, kind);

    /// <summary>
    /// Compiles the project's <c>Scripts/</c> folder again, inside the running session, so a script edited in another
    /// window is what Play runs and a type it just declared is in the Add Component menu.
    /// </summary>
    /// <remarks>
    /// The compile is queued rather than done here: the scene, its factories and the load context belong to the
    /// render thread. What came of it is reported the way every compile is — the count on success, one line per
    /// error — because the console is the only place a script error can be shown.
    /// </remarks>
    /// <returns>False when no session is running to compile them.</returns>
    public bool ReloadScripts()
    {
        if (_session?.RequestScriptReload() != true)
        {
            return false;
        }

        WriteLog("[editor] recompiling the project's C# scripts");

        return true;
    }

    /// <summary>Writes one transform row of an object.</summary>
    /// <param name="objectId">The object.</param>
    /// <param name="transformField">Position, rotation (Euler degrees) or scale.</param>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    /// <param name="completed">Called on the render thread with the last snapshot frame before execution completed.</param>
    /// <returns>False when no session is running to apply it.</returns>
    public bool SetTransform(Guid objectId, TransformField transformField, float x, float y, float z, Action<long>? completed = null)
    {
        EndLiveEdit();

        return Enqueue(
            new SetTransformCommand(objectId, transformField, new System.Numerics.Vector3(x, y, z)),
            "move the object",
            completed);
    }

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
        EndLiveEdit();

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
        EndLiveEdit();

        if (!CanRedo)
        {
            return false;
        }

        return Enqueue(new RedoCommand(), "redo");
    }

    /// <summary>Hands an edit to the render thread, or says why it could not.</summary>
    private bool Enqueue(OvercoreEditorSession.Host.IEditorCommand command, string action, Action<long>? completed = null)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            WriteLog($"[editor] cannot {action}: no session is running.", LogLevel.Warning);
            return false;
        }

        host.Enqueue(WrapForCompletion(command, host, completed));
        return true;
    }

    /// <summary>
    /// Wraps a command so the caller learns the last snapshot frame before it ran, letting the UI ignore older
    /// snapshots until a newer one confirms the edit. Without a callback the command goes as it is.
    /// </summary>
    private static OvercoreEditorSession.Host.IEditorCommand WrapForCompletion(
        OvercoreEditorSession.Host.IEditorCommand command, EditorSessionHost host, Action<long>? completed)
    {
        return completed is null
            ? command
            : new CompletionCommand(command, () => completed(host.LatestScene?.Frame ?? -1));
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
