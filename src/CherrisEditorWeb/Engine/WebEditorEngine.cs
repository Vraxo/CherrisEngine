using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Projects;
using Cherris.Rendering.Headless;
using Cherris.Rendering.OpenTK;
using Cherris.Utils;
using CherrisEditorWeb.Host;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;

namespace CherrisEditorWeb;

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
public sealed class WebEditorEngine : Engine, IDisposable
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
    private int _playState;
    private volatile string? _sceneNotice;

    /// <summary>Sentinel for "no transport action is waiting"; every <see cref="PlaybackRequest"/> is zero or more.</summary>
    private const int NoPlayRequest = -1;

    /// <param name="backend">Off-screen rendering backend.</param>
    /// <param name="sink">Receives captured frames and the desired viewport size.</param>
    /// <param name="snapshotSink">Optional receiver of scene snapshots for the UI.</param>
    /// <param name="scene">Project scene to open; the demo scene when omitted.</param>
    /// <param name="commands">Edits to apply each frame, from the UI thread.</param>
    public WebEditorEngine(
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

        OnDrawUI = _ =>
        {
            // The transport and the edits land at the top of the frame: nothing else touches the scene while
            // this thread runs, and the snapshot published below already includes both.
            ApplyPlaybackRequest();
            ExecuteCommands();
            CaptureAndPublish();
        };
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

        try
        {
            // Asset paths in the scene are relative to the project, and a packaged project keeps them in
            // Assets.pak, so both have to be pointed at the project folder before the scene is read.
            ProjectFiles.ProjectRoot = _scene.ProjectRoot;
            ProjectFiles.Initialize(_scene.ProjectRoot);

            List<GameObject> objects = SceneLoader.LoadScene(_scene.ScenePath);

            // An empty scene and a file that is not a scene both load as no objects, and they are not the same
            // thing: the first is a project with nothing in it yet, the second is a broken start scene.
            SceneFileKind kind = objects.Count == 0 ? Inspect(_scene.ScenePath) : SceneFileKind.Scene;

            if (kind != SceneFileKind.Scene)
            {
                bool missing = kind == SceneFileKind.Missing;

                Logger.Warning(
                    $"[web-editor] '{_scene.ScenePath}' " +
                    (missing ? "could not be found" : "is not a scene") + "; showing the demo scene.");

                return (DemoScene.Create(ResourceManager), missing
                    ? $"{fileName} could not be found, so the demo scene is open instead."
                    : $"{fileName} is not a scene, so the demo scene is open instead.");
            }

            Scene scene = new(_scene.ScenePath, objects);
            bool addedCamera = EnsureCamera(scene);

            if (objects.Count == 0)
            {
                Logger.Info(
                    $"[web-editor] '{_scene.ScenePath}' has no objects yet; opened it empty" +
                    (addedCamera ? " with a camera the editor added." : "."));

                return (scene, $"{fileName} has no objects yet." +
                    (addedCamera ? " The editor added a camera so the viewport can draw." : string.Empty));
            }

            Logger.Info($"[web-editor] Opened '{_scene.DisplayName}' ({objects.Count} objects).");

            return (scene, addedCamera
                ? "The scene has no camera, so the editor added one at the default view."
                : null);
        }
        catch (Exception exception)
        {
            Logger.Error(
                $"[web-editor] Could not open '{_scene.ScenePath}': {exception.Message}; showing the demo scene.");

            return (DemoScene.Create(ResourceManager),
                $"{fileName} could not be opened, so the demo scene is showing.");
        }
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

        GameObject cameraObject = new("Camera");
        cameraObject.Transform.Position = new Vector3(0f, 2.2f, 6.5f);
        cameraObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.2f);
        cameraObject.AddComponent(new Camera());
        scene.AddGameObject(cameraObject);
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
