using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Projects;
using Cherris.Rendering.Headless;
using Cherris.Rendering.OpenTK;
using Cherris.Scripting;
using Cherris.Utils;
using CherrisEditorSession.Host;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;
using System.Runtime.Loader;

namespace CherrisEditorSession;

/// <summary>
/// Engine host that renders off-screen and publishes each frame to an <see cref="IViewportSink"/> instead
/// of presenting it to a window.
/// </summary>
/// <remarks>
/// <para>
/// Frames are captured from <see cref="Engine.OnDrawUI"/>, which runs at the start of the engine's update:
/// at that point the previous frame is still in the renderer's composite texture, so the capture reads a
/// complete, unmodified frame while the current one has not been drawn yet.
/// </para>
/// <para>
/// The demo scene is a placeholder; swap <see cref="LoadContent"/> for the project/scene loading the
/// desktop editor performs once the web UI can open projects.
/// </para>
/// </remarks>
public sealed class EditorSession : Engine, IDisposable
{
    private readonly HeadlessBackend _backend;
    private readonly IViewportSink _sink;
    private readonly ISceneSnapshotSink? _snapshotSink;
    private readonly EditorSceneRequest _scene;
    private readonly EditorCommandDispatcher? _commands;
    private readonly EditorPlaySession _play;

    private SceneFrameCapture? _capture;
    private GameObject? _spinTarget;
    private float _spinAngle;
    private volatile bool _stopRequested;
    private volatile string? _rendererInfo;
    private long _frameNumber;
    private int _playRequest = NoPlayRequest;
    private int _scriptReloadRequested;
    private int _playState;

    private AssemblyLoadContext _scriptContext = NewScriptContext();
    private readonly List<Type> _scriptTypes = [];
    private IReadOnlyList<ComponentCatalog.Entry> _scriptComponents = [];
    private volatile string? _sceneNotice;
    private readonly ViewportCameraController _camera = new();

    private GameObject? _cameraOwner;

    /// <summary>The rotation the camera was left in last frame, so a rotation from anywhere else can be noticed.</summary>
    private Quaternion _cameraRotation;

    /// <summary>Path of a scene the UI asked for, waiting for the next frame. Consumed by <see cref="ApplySceneRequest"/>.</summary>
    private string? _pendingScenePath;

    /// <summary>Sentinel for "no transport action is waiting"; every <see cref="PlaybackRequest"/> is zero or more.</summary>
    private const int NoPlayRequest = -1;

    /// <param name="backend">Off-screen rendering backend.</param>
    /// <param name="sink">Receives captured frames and the desired viewport size.</param>
    /// <param name="snapshotSink">Optional receiver of scene snapshots for the UI.</param>
    /// <param name="scene">Project scene to open; the demo scene when omitted.</param>
    /// <param name="commands">Edits to apply each frame, from the UI thread.</param>
    public EditorSession(
        HeadlessBackend backend,
        IViewportSink sink,
        ISceneSnapshotSink? snapshotSink = null,
        EditorSceneRequest? scene = null,
        EditorCommandDispatcher? commands = null)
        : base("Cherris Web Editor", false, backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _snapshotSink = snapshotSink;
        _scene = scene ?? EditorSceneRequest.Demo;
        _commands = commands;

        Exposure = 0.5f;

        // Play mode owns the world: physics and scripts only advance while the transport says so, which is
        // what makes pause and stop possible at all. The scene is still started when it loads, as in a game.
        AutoUpdateScene = false;
        _play = new EditorPlaySession(SceneManager, Logger.Info);

        // Before the first frame, because the first frame reads the scene: a factory that arrives afterwards would
        // be missing from the components the file names.
        LoadProjectScripts();

        if (_commands is not null)
        {
            // A command that writes a C# script attaches it in the same drain, which is only possible if it can ask
            // for a compile first.
            _commands.ReloadScripts = ReloadProjectScripts;
        }

        OnDrawUI = _ =>
        {
            // Everything the UI asked for lands at the top of the frame, before the snapshot below is taken:
            // which scene is open decides what the transport and the edits apply to, so it comes first.
            ApplySceneRequest();
            ApplyPlaybackRequest();
            ApplyScriptReload();
            ExecuteCommands();
            CaptureAndPublish();
        };
    }

    /// <summary>
    /// The project's C# script types, compiled when the session opened: the types a scene may name, and the ones an
    /// inspector can offer. Empty when the project has no <c>Scripts/</c> folder or nothing in it compiled.
    /// </summary>
    public IReadOnlyList<Type> ScriptTypes => _scriptTypes;

    /// <summary>
    /// The same types as Add Component menu entries, named the way the menu names them. Empty until a session has
    /// compiled the project's scripts.
    /// </summary>
    public IReadOnlyList<ComponentCatalog.Entry> ScriptComponents => _scriptComponents;

    /// <summary>
    /// Asks the render thread to compile the project's C# scripts again at the top of its next frame, and returns
    /// false when the session is already shutting down. Safe from any thread.
    /// </summary>
    /// <remarks>
    /// This is what makes a script created from the UI attachable without reopening the project. The compile happens
    /// on the render thread because that is the thread the scene and its factories belong to.
    /// </remarks>
    public bool RequestScriptReload()
    {
        if (_stopRequested)
        {
            return false;
        }

        Volatile.Write(ref _scriptReloadRequested, 1);

        return true;
    }

    /// <summary>
    /// Compiles the project's C# scripts into a fresh load context and returns the entries they make now.
    /// </summary>
    /// <remarks>
    /// The old context is not unloaded here, only dropped: a script already attached to an object still refers to
    /// its types, and those keep working for as long as something holds them. What changes is the catalog and the
    /// factories — from now on the scene names the new types — which is the rebuild-or-lose trade the transport
    /// makes on purpose rather than leaving a stale type registered.
    /// </remarks>
    /// <returns>The Add Component entries for the types the project declares.</returns>
    public IReadOnlyList<ComponentCatalog.Entry> ReloadProjectScripts()
    {
        _scriptTypes.Clear();
        _scriptComponents = [];
        _scriptContext = NewScriptContext();

        LoadProjectScripts();

        return _scriptComponents;
    }

    private static AssemblyLoadContext NewScriptContext() =>
        new("EditorScripts", isCollectible: true);

    /// <summary>Runs a reload the UI asked for, once, at the top of a frame.</summary>
    private void ApplyScriptReload()
    {
        if (Interlocked.Exchange(ref _scriptReloadRequested, 0) == 0)
        {
            return;
        }

        ReloadProjectScripts();
    }

    /// <summary>
    /// Compiles the project's C# scripts and registers them, so a scene that names one of those types gets the
    /// component instead of a warning about an unknown one.
    /// </summary>
    /// <remarks>
    /// A failure is reported, not thrown: a project whose scripts do not compile still opens, and the editor is the
    /// only place the error can be shown. The session keeps running with whatever the scene's built-in components
    /// are, which is what lets the mistake be fixed and the project reopened.
    /// </remarks>
    private void LoadProjectScripts()
    {
        if (_scene.ProjectRoot is not { } projectRoot)
        {
            return;
        }

        ScriptCompileResult result = ProjectScriptCompiler.Compile(projectRoot, _scriptContext);

        if (!result.Succeeded)
        {
            Logger.Error($"[web-editor] {result.Errors.Count} error(s) compiling the project's C# scripts:");

            foreach (string error in result.Errors)
            {
                Logger.Error($"  {error}");
            }

            return;
        }

        foreach (Type type in result.ScriptTypes)
        {
            // The name the scene file carries: the class name, exactly as the ImGui editor registers it.
            SceneLoader.RegisterComponentFactory(type.Name, _ => (Component)Activator.CreateInstance(type)!);
            _scriptTypes.Add(type);
        }

        if (_scriptTypes.Count == 0)
        {
            return;
        }

        // The inspector's menu and the command that follows a click both need these: the menu to offer them, the
        // command to turn the name it was quoted back into the type it creates.
        _scriptComponents = ComponentCatalog.ForScriptTypes(_scriptTypes);

        if (_commands is not null)
        {
            _commands.ScriptComponents = _scriptComponents;
        }

        Logger.Info($"[web-editor] Compiled {_scriptTypes.Count} C# script(s) from the project's Scripts folder.");
    }

    /// <summary>
    /// Queues a transport action. Safe to call from any thread; the render loop carries it out at the top of
    /// its next frame, which is the only place the scene may change.
    /// </summary>
    /// <returns>False when the host is already shutting down and the action will be ignored.</returns>
    public bool RequestPlayback(PlaybackRequest request)
    {
        if (_stopRequested)
        {
            return false;
        }

        Volatile.Write(ref _playRequest, (int)request);

        return true;
    }

    /// <summary>What the transport is doing; written by the render thread, read by the UI.</summary>
    public EditorPlayState PlayState => (EditorPlayState)Volatile.Read(ref _playState);

    /// <summary>
    /// Asks the render thread to open a scene file in place of the one that is open. Safe from any thread; the
    /// switch happens at the top of the next frame, which is the only place the scene may change.
    /// </summary>
    /// <param name="scenePath">Absolute path of the file to open.</param>
    /// <returns>False when the session is already shutting down and the request will be ignored.</returns>
    public bool RequestSceneOpen(string scenePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenePath);

        if (_stopRequested)
        {
            return false;
        }

        Interlocked.Exchange(ref _pendingScenePath, scenePath);

        return true;
    }

    /// <summary>Opens the scene the UI asked for, if one is waiting. Runs on the render thread, before the frame.</summary>
    private void ApplySceneRequest()
    {
        string? path = Interlocked.Exchange(ref _pendingScenePath, null);

        if (path is not null)
        {
            SwitchScene(path);
        }
    }

    /// <summary>Carries out the transport action the UI queued, if any.</summary>
    private void ApplyPlaybackRequest()
    {
        int request = Interlocked.Exchange(ref _playRequest, NoPlayRequest);

        if (request == NoPlayRequest)
        {
            return;
        }

        PlaybackRequest action = (PlaybackRequest)request;

        if (!_play.Apply(action))
        {
            return;
        }

        Volatile.Write(ref _playState, (int)_play.State);

        // Stop and restart put the authored rotation back, so the demo's spin starts over from it too.
        if (action is PlaybackRequest.Stop or PlaybackRequest.Restart)
        {
            _spinAngle = 0f;
        }
    }

    /// <summary>
    /// Asks the render loop to clean up GL resources and stop. Safe to call from any thread; the loop
    /// notices on its next update.
    /// </summary>
    public void RequestStop()
    {
        _stopRequested = true;
    }

    /// <summary>
    /// Raised on the render thread when a click in the viewport picked an object, or null when it landed on empty
    /// space. The host hands it to the UI's selection; nothing here knows what a selection is.
    /// </summary>
    public event Action<Guid?>? ObjectPicked;

    /// <summary>Runs the UI's pending edits against the active scene.</summary>
    private void ExecuteCommands()
    {
        if (_commands is null || _commands.Pending == 0 || SceneManager.ActiveScene is not { } scene)
        {
            return;
        }

        _commands.Drain(scene, Logger.Info);
    }

    /// <summary>
    /// Renderer, vendor and GL version of the context in use, captured once it is current. Null until the
    /// first frame; it is the quickest proof that the driver gave us the GL 3.3+ context the shaders need.
    /// </summary>
    public string? RendererInfo => _rendererInfo;

    /// <summary>
    /// Something the user should know about the scene that was opened — an empty file, a camera the editor had
    /// to add, a file that is not a scene at all — or null when it opened exactly as it is on disk.
    /// </summary>
    /// <remarks>
    /// The hierarchy shows the <em>session</em> and the content browser previews the <em>file</em>, so the two can
    /// honestly differ. This is how the difference is said out loud instead of being left for the user to
    /// work out.
    /// </remarks>
    public string? SceneNotice => _sceneNotice;

    /// <inheritdoc />
    protected override void LoadContent()
    {
        _rendererInfo = DescribeRenderer();

        ResourceManager.LoadInitialAssets();

        (Scene scene, string? notice) = OpenRequestedScene();
        _sceneNotice = notice;
        SceneManager.OpenScene(scene);

        // Only the built-in demo spins, and only while play is running; a project's objects must sit exactly
        // where they were authored.
        _spinTarget = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Name == "Cube")
            is { } cube && _scene.ProjectRoot is null ? cube : null;
    }

    /// <summary>
    /// Loads the project's start scene, or the demo scene when no project is open. The file is opened as it is,
    /// empty included; only a file that is not a scene at all falls back to the demo scene, and either way what
    /// happened comes back as a notice for the UI.
    /// </summary>
    private (Scene Scene, string? Notice) OpenRequestedScene()
    {
        if (_scene.ProjectRoot is null || string.IsNullOrEmpty(_scene.ScenePath))
        {
            return (DemoScene.Create(ResourceManager), null);
        }

        // The notices name the file rather than the project: it is the file's contents being explained.
        string fileName = Path.GetFileName(_scene.ScenePath);

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = _scene.DisplayName;
        }

        SceneRead? read;

        try
        {
            read = ReadSceneFile(_scene.ScenePath, out string failure);

            if (read is null)
            {
                Logger.Warning($"[web-editor] '{_scene.ScenePath}' {failure}; showing the demo scene.");

                return (DemoScene.Create(ResourceManager),
                    $"{fileName} {failure}, so the demo scene is open instead.");
            }
        }
        catch (Exception exception)
        {
            Logger.Error(
                $"[web-editor] Could not open '{_scene.ScenePath}': {exception.Message}; showing the demo scene.");

            return (DemoScene.Create(ResourceManager),
                $"{fileName} could not be opened, so the demo scene is showing.");
        }

        SceneRead scene = read.Value;

        if (scene.WasEmpty)
        {
            Logger.Info(
                $"[web-editor] '{_scene.ScenePath}' has no objects yet; opened it empty" +
                (scene.AddedCamera ? " with a camera the editor added." : "."));
        }
        else
        {
            Logger.Info($"[web-editor] Opened '{_scene.DisplayName}' ({scene.Scene.GameObjects.Count} objects).");
        }

        return (scene.Scene, NoticeFor(fileName, scene));
    }

    /// <summary>What reading a scene file produced.</summary>
    /// <param name="Scene">The scene, with a camera added when the file had none.</param>
    /// <param name="WasEmpty">The file parsed as a scene and described no objects at all.</param>
    /// <param name="AddedCamera">The editor had to add a camera so the viewport can draw.</param>
    private readonly record struct SceneRead(Scene Scene, bool WasEmpty, bool AddedCamera);

    /// <summary>
    /// Reads a scene file as it is — empty included, no fallback — and adds a camera when it has none. The file's
    /// own contents decide everything else.
    /// </summary>
    /// <param name="path">Absolute path of the scene file.</param>
    /// <param name="failure">Why the file could not be used, when this returns null.</param>
    /// <returns>The scene, or null when the file is not there or is not a scene.</returns>
    private SceneRead? ReadSceneFile(string path, out string failure)
    {
        failure = string.Empty;

        if (_scene.ProjectRoot is { } projectRoot)
        {
            // Asset paths in the scene are relative to the project, and a packaged project keeps them in
            // Assets.pak, so both have to be pointed at the project folder before the scene is read.
            ProjectFiles.ProjectRoot = projectRoot;
            ProjectFiles.Initialize(projectRoot);
        }

        List<GameObject> objects = SceneLoader.LoadScene(path);

        // An empty scene and a file that is not a scene both load as no objects, and they are not the same
        // thing: the first is a project with nothing in it yet, the second is a broken start scene.
        SceneFileKind kind = objects.Count == 0 ? Inspect(path) : SceneFileKind.Scene;

        if (kind != SceneFileKind.Scene)
        {
            failure = kind == SceneFileKind.Missing ? "could not be found" : "is not a scene";
            return null;
        }

        Scene scene = new(path, objects);
        bool addedCamera = EnsureCamera(scene);

        return new SceneRead(scene, objects.Count == 0, addedCamera);
    }

    /// <summary>
    /// What a scene needs to say for itself, or null when it opened exactly as it is on disk. The tree shows what
    /// the session holds, so a camera the editor added is said out loud rather than left to be discovered.
    /// </summary>
    private static string? NoticeFor(string fileName, SceneRead read)
    {
        if (read.WasEmpty)
        {
            return $"{fileName} has no objects yet." +
                (read.AddedCamera ? " The editor added a camera so the viewport can draw." : string.Empty);
        }

        return read.AddedCamera
            ? "The scene has no camera, so the editor added one at the default view."
            : null;
    }

    /// <summary>
    /// Replaces the open scene with the one in a file, keeping the session — the viewport, the port, the panels —
    /// as it is. The old scene is closed, play goes back to editing, and the undo stack is dropped, because its
    /// commands name objects of the scene that has just gone.
    /// </summary>
    /// <remarks>
    /// A file that cannot be used leaves the open scene alone and says why in the hierarchy. Unlike a session's
    /// first scene, there is nothing to fall back to: the demo scene would be a second surprise on top of a
    /// file that failed.
    /// </remarks>
    private void SwitchScene(string path)
    {
        string fileName = Path.GetFileName(path);

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = path;
        }

        // The file is read first: a switch that cannot happen must not also cost the user their play state.
        SceneRead? read;

        try
        {
            read = ReadSceneFile(path, out string failure);

            if (read is null)
            {
                Logger.Warning($"[web-editor] '{path}' {failure}; keeping the open scene.");
                _sceneNotice = $"{fileName} {failure}, so the open scene is still showing.";
                return;
            }
        }
        catch (Exception exception)
        {
            Logger.Error($"[web-editor] Could not open '{path}': {exception.Message}; keeping the open scene.");
            _sceneNotice = $"{fileName} could not be opened: {exception.Message}";
            return;
        }

        // Stop while the old scene is still the active one: the play session puts its snapshot back into the scene
        // it captured it from, and that scene is about to go.
        if (_play.Apply(PlaybackRequest.Stop))
        {
            Volatile.Write(ref _playState, (int)_play.State);
        }

        SceneRead loaded = read.Value;
        Scene? previous = SceneManager.ActiveScene;

        SceneManager.OpenScene(loaded.Scene);

        try
        {
            // The scene is started exactly as a session starts the scene it opens, so its scripts and physics are
            // in the same state afterwards.
            SceneManager.Start();
        }
        catch (Exception exception)
        {
            // A script that throws on start is the script's failure, not the file's: the scene stays open and the
            // console says what happened.
            Logger.Error($"[web-editor] A script threw while '{fileName}' started: {exception.Message}");
        }

        if (previous is not null)
        {
            SceneManager.CloseScene(previous);
        }

        _commands?.History.Clear();
        _spinTarget = null;
        _sceneNotice = NoticeFor(fileName, loaded);

        Logger.Info($"[web-editor] Opened '{fileName}' ({loaded.Scene.GameObjects.Count} objects).");
    }

    /// <summary>
    /// What a scene file that loaded as no objects actually is: a scene with nothing in it yet
    /// (<c>GameObjects: []</c>, which is what a new project's start scene is), something else entirely, or a
    /// file that is not there at all. The parser that answers it is the one the content browser previews scenes
    /// with, so a tile and the hierarchy cannot disagree about whether a file is a scene.
    /// </summary>
    private static SceneFileKind Inspect(string path)
    {
        string? text;

        try
        {
            // The project may keep its scenes in Assets.pak, so they are read the way the loader reads them.
            using Stream? stream = ProjectFiles.Open(path);

            if (stream is null)
            {
                return SceneFileKind.Missing;
            }

            using StreamReader reader = new(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SceneFileKind.Missing;
        }

        return SceneThumbnail.Parse(text) is null ? SceneFileKind.NotAScene : SceneFileKind.Scene;
    }

    /// <summary>What a start scene turned out to be when loading it produced no objects.</summary>
    private enum SceneFileKind
    {
        /// <summary>A scene, even if it has no objects in it yet.</summary>
        Scene,

        /// <summary>A file that parses, but not as a scene.</summary>
        NotAScene,

        /// <summary>No such file, or it could not be opened.</summary>
        Missing
    }

    /// <summary>
    /// Adds a camera to a scene that has none. A new project's start scene is empty, and the renderer needs
    /// a camera to draw anything at all.
    /// </summary>
    /// <returns>True when a camera had to be added, so the caller can say so.</returns>
    private static bool EnsureCamera(Scene scene)
    {
        if (scene.MainCamera is not null)
        {
            return false;
        }

        // The same camera a new scene starts with, so a repaired scene and a new one look alike.
        scene.AddGameObject(SceneTemplate.CreateCamera());
        scene.FindMainComponents();

        Logger.Warning("[web-editor] The scene has no camera; added one at the default view.");

        return true;
    }

    /// <inheritdoc />
    protected override void Update(float deltaTime)
    {
        if (_stopRequested)
        {
            // Dispose GL objects while the context is still current, then end the game loop.
            _capture?.Dispose();
            _capture = null;
            _backend.Close();
            return;
        }

        HandleViewportClick();
        UpdateEditorCamera(deltaTime);

        base.Update(deltaTime);

        // Physics, audio and scripts advance only while play is running; a paused or edited scene stands still.
        _play.Tick(deltaTime);

        if (_spinTarget is null || !_play.IsRunning)
        {
            return;
        }

        _spinAngle += deltaTime * 0.4f;
        _spinTarget.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _spinAngle);
    }

    /// <summary>
    /// A left click in the viewport selects whatever is under it, and a click on empty space clears the
    /// selection — the behaviour the ImGui editor's viewport has. The press is read from the engine's input
    /// state, which the host fills from the browser before every frame.
    /// </summary>
    private void HandleViewportClick()
    {
        // A captured pointer keeps reporting the position it was captured at, so a click while it is captured has
        // nowhere real to pick at.
        if (Input.IsMouseLocked || !Input.WasMouseButtonPressed(MouseButton.Left))
        {
            return;
        }

        (int width, int height) = _sink.ViewportSize;

        GameObject? picked = ViewportPicker.Pick(
            SceneManager.ActiveScene,
            Input.MousePosition,
            new Vector2(width, height));

        ObjectPicked?.Invoke(picked?.Id);
    }

    /// <summary>
    /// Flies the editor camera: right-drag to look, WASD to move, Q and E down and up, the wheel along the view.
    /// </summary>
    /// <remarks>
    /// Nothing moves while the transport is playing: then the scene's own scripts own the camera, and a second
    /// hand on it would fight them. Paused counts as editing, because nothing is advancing.
    /// </remarks>
    private void UpdateEditorCamera(float deltaTime)
    {
        if (_play.State == EditorPlayState.Playing ||
            SceneManager.MainCamera is not { } camera ||
            camera.GameObject is not { } owner)
        {
            return;
        }

        Transform transform = owner.Transform;

        // The controller follows the camera rather than the other way round: a different camera — another scene, or a
        // script that swapped one in — and a rotation something else put back (stopping play does exactly that) are
        // both adopted before this frame's input is applied, so the next drag carries on from what is on screen.
        if (!ReferenceEquals(_cameraOwner, owner) || transform.Rotation != _cameraRotation)
        {
            _cameraOwner = owner;
            _camera.SyncFrom(camera);
        }

        bool hovered = PointerOverViewport();

        // The drag is taken from the flag the browser sets rather than from the engine's button state, and the button
        // is kept as a fallback: a client that never sends the flag still looks, and a drag whose button state was
        // cleared under it — a capture that moves the focus does that — still turns the camera.
        bool looking = _sink.LookDragging || Input.IsMouseButtonDown(MouseButton.Right);

        _camera.Update(camera, new ViewportCameraInput(
            looking ? Input.MouseDelta : Vector2.Zero,
            hovered ? MoveAxes() : Vector2.Zero,
            hovered ? VerticalAxis() : 0f,
            hovered ? Input.MouseWheelDelta.Y : 0f,
            Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight)), deltaTime);

        _cameraRotation = transform.Rotation;
    }

    /// <summary>True when the last pointer position the browser reported is inside the viewport.</summary>
    private bool PointerOverViewport()
    {
        (int width, int height) = _sink.ViewportSize;
        Vector2 pointer = Input.MousePosition;

        return width > 0 && height > 0 &&
            pointer.X >= 0f && pointer.Y >= 0f && pointer.X <= width && pointer.Y <= height;
    }

    /// <summary>The movement keys as local axes: D right, A left, S back, W forward.</summary>
    private static Vector2 MoveAxes()
    {
        float horizontal = (Input.IsKeyDown(Key.D) ? 1f : 0f) - (Input.IsKeyDown(Key.A) ? 1f : 0f);
        float depth = (Input.IsKeyDown(Key.S) ? 1f : 0f) - (Input.IsKeyDown(Key.W) ? 1f : 0f);

        return new Vector2(horizontal, depth);
    }

    /// <summary>World up or down: E adds, Q subtracts.</summary>
    private static float VerticalAxis() =>
        (Input.IsKeyDown(Key.E) ? 1f : 0f) - (Input.IsKeyDown(Key.Q) ? 1f : 0f);

    /// <inheritdoc />
    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }

    private static string DescribeRenderer()
    {
        try
        {
            string renderer = GL.GetString(StringName.Renderer) ?? "unknown renderer";
            string vendor = GL.GetString(StringName.Vendor) ?? "unknown vendor";
            string version = GL.GetString(StringName.Version) ?? "unknown version";
            return $"{renderer} ({vendor}) · GL {version}";
        }
        catch (Exception)
        {
            return "renderer details unavailable";
        }
    }

    private void CaptureAndPublish()
    {
        _frameNumber++;
        PublishSceneSnapshot();

        OpenTKRenderer? renderer = _backend.OpenGLRenderer;

        if (renderer is null)
        {
            return;
        }

        (int width, int height) = _sink.ViewportSize;

        // The viewport's own options are the UI's to change and the renderer's to draw: the toggle is read here,
        // before this frame is drawn, so the picture and the tick in the menu agree.
        renderer.ShowGrid = _sink.ShowGrid;

        // The renderer takes its off-screen path only when a viewport size was set for the frame, and the
        // capture buffer must match the composite texture it reads from.
        _backend.SetViewportSize(width, height);

        _capture ??= new SceneFrameCapture(width, height);

        if (!_capture.TryCapture(renderer.GetSceneTextureHandle(), width, height, out SceneFrame frame))
        {
            return;
        }

        _sink.PublishFrame(frame.Pixels, frame.Width, frame.Height, frame.Sequence);
    }

    /// <summary>
    /// Copies the hierarchy into an immutable snapshot every few frames. Snapshots are cheap relative to a
    /// frame, but a UI cannot use more than a handful per second, so this deliberately throttles.
    /// </summary>
    private void PublishSceneSnapshot()
    {
        if (_snapshotSink is null || _frameNumber % 15 != 0)
        {
            return;
        }

        _snapshotSink.PublishScene(
            SceneSnapshotBuilder.Build(SceneManager.ActiveScene, _frameNumber, DateTimeOffset.UtcNow));
    }
}
