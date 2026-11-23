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

    private SceneFrameCapture? _capture;
    private GameObject? _spinTarget;
    private float _spinAngle;
    private volatile bool _stopRequested;
    private volatile string? _rendererInfo;
    private long _frameNumber;

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
        OnDrawUI = _ =>
        {
            // Edits land at the top of the frame: nothing else touches the scene while this thread runs, and
            // the snapshot published below already includes them.
            ExecuteCommands();
            CaptureAndPublish();
        };
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

    /// <inheritdoc />
    protected override void LoadContent()
    {
        _rendererInfo = DescribeRenderer();

        ResourceManager.LoadInitialAssets();

        Scene scene = OpenRequestedScene();
        SceneManager.OpenScene(scene);

        // Only the built-in demo spins; a project's objects must sit exactly where they were authored.
        _spinTarget = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Name == "Cube")
            is { } cube && _scene.ProjectRoot is null ? cube : null;
    }

    /// <summary>
    /// Loads the project's start scene, or the demo scene when no project is open. A missing, empty or
    /// unreadable scene falls back to the demo scene instead of leaving the viewport blank: the user gets
    /// something to look at and the reason is in the log.
    /// </summary>
    private Scene OpenRequestedScene()
    {
        if (_scene.ProjectRoot is null || string.IsNullOrEmpty(_scene.ScenePath))
        {
            return DemoScene.Create(ResourceManager);
        }

        try
        {
            // Asset paths in the scene are relative to the project, and a packaged project keeps them in
            // Assets.pak, so both have to be pointed at the project folder before the scene is read.
            ProjectFiles.ProjectRoot = _scene.ProjectRoot;
            ProjectFiles.Initialize(_scene.ProjectRoot);

            List<GameObject> objects = SceneLoader.LoadScene(_scene.ScenePath);

            if (objects.Count == 0)
            {
                Logger.Warning(
                    $"[web-editor] '{_scene.ScenePath}' is empty or could not be read; showing the demo scene.");

                return DemoScene.Create(ResourceManager);
            }

            Scene scene = new(_scene.ScenePath, objects);

            EnsureCamera(scene);

            Logger.Info($"[web-editor] Opened '{_scene.DisplayName}' ({objects.Count} objects).");

            return scene;
        }
        catch (Exception exception)
        {
            Logger.Error(
                $"[web-editor] Could not open '{_scene.ScenePath}': {exception.Message}; showing the demo scene.");

            return DemoScene.Create(ResourceManager);
        }
    }

    /// <summary>
    /// Adds a camera to a scene that has none. A new project's start scene is empty, and the renderer needs
    /// a camera to draw anything at all.
    /// </summary>
    private static void EnsureCamera(Scene scene)
    {
        if (scene.MainCamera is not null)
        {
            return;
        }

        GameObject cameraObject = new("Camera");
        cameraObject.Transform.Position = new Vector3(0f, 2.2f, 6.5f);
        cameraObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.2f);
        cameraObject.AddComponent(new Camera());
        scene.AddGameObject(cameraObject);
        scene.FindMainComponents();

        Logger.Warning("[web-editor] The scene has no camera; added one at the default view.");
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

        if (_spinTarget is null)
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
