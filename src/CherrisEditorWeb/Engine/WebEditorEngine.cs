using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering.Headless;
using Cherris.Rendering.OpenTK;
using CherrisEditorWeb.Host;
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

    private SceneFrameCapture? _capture;
    private GameObject? _spinTarget;
    private float _spinAngle;
    private volatile bool _stopRequested;

    public WebEditorEngine(HeadlessBackend backend, IViewportSink sink)
        : base("Cherris Web Editor", false, backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

        Exposure = 0.5f;
        OnDrawUI = _ => CaptureAndPublish();
    }

    /// <summary>
    /// Asks the render loop to clean up GL resources and stop. Safe to call from any thread; the loop
    /// notices on its next update.
    /// </summary>
    public void RequestStop()
    {
        _stopRequested = true;
    }

    /// <inheritdoc />
    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();

        Scene scene = DemoScene.Create(ResourceManager);
        SceneManager.OpenScene(scene);

        _spinTarget = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Name == "Cube");
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

    private void CaptureAndPublish()
    {
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
}
