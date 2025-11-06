using Cherris.OpenTK;
using Cherris.Rendering;
using System.Globalization;
using System.Numerics;

namespace Cherris;

public abstract class Engine
{
    public Action<float>? OnDrawUI;
    public float Exposure { get; set; } = 1.0f;

    public readonly IResourceManager ResourceManager;
    public readonly SceneLoader SceneLoader;
    public readonly SceneManager SceneManager;
    public IRenderer Renderer { get; }

    protected GameObject? SelectedGameObject { get; set; }
    protected readonly RenderingInterface _backend;

    private readonly IGameWindow _gameWindow;
    private readonly GameLoop _gameLoop;

    private float _lastDeltaTime;
    private float _snapshotTimer;
    private const float SnapshotInterval = 1.0f;
    private bool _snapshotsEnabled;

    public Ray CreateRayFromMouse()
    {
        Camera camera = SceneManager.MainCamera;

        if (camera is null)
        {
            return new();
        }

        float x = (2.0f * Input.MousePosition.X) / _gameWindow.Width - 1.0f;
        float y = 1.0f - (2.0f * Input.MousePosition.Y) / _gameWindow.Height;
        Vector4 ndc = new(x, y, 1.0f, 1.0f);

        Matrix4x4.Invert(camera.GetProjectionMatrix(_gameWindow.Width / _gameWindow.Height), out var invProjection);

        Vector4 viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        Vector4 worldRay = Vector4.Transform(viewRay, invView);

        Vector3 rayDir = Vector3.Normalize(new(worldRay.X, worldRay.Y, worldRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new(rayOrigin, rayDir);
    }

    protected Engine(string windowTitle, bool startWithMouseLocked, GraphicsAPI api)
    {
        _backend = CreateBackend(api);
        _backend.Initialize(windowTitle, 1920, 1080, startWithMouseLocked);

        _gameWindow = _backend.GameWindow;
        ResourceManager = _backend.ResourceManager;
        Renderer = _backend.Renderer;

        // Initialize systems that depend on the backend
        SceneLoader = new(ResourceManager);
        SceneManager = new();

        // Game loop is created last, as it depends on the Update/Draw methods
        _gameLoop = new(_gameWindow, Update, Draw);

        // Subscribe to resize event
        _gameWindow.Resized += OnWindowResized;

        ComponentDeserializer.RegisterFactories(SceneLoader, ResourceManager);
    }

    protected virtual void Update(float deltaTime)
    {
        _lastDeltaTime = deltaTime;

        _backend.UIController?.Update(deltaTime);
        OnDrawUI?.Invoke(deltaTime);

        if (Input.WasKeyPressed(Key.F12))
        {
            _snapshotsEnabled = !_snapshotsEnabled;

            Console.WriteLine(
                $"[Engine] Snapshots {(_snapshotsEnabled ? "enabled" : "disabled")}. " +
                $"Press F12 to toggle.");
        }
    }

    protected virtual void OnStart() { }

    protected abstract void LoadContent();

    private static RenderingInterface CreateBackend(GraphicsAPI api)
    {
        return api switch
        {
            GraphicsAPI.Veldrid => new VeldridBackend(),
            GraphicsAPI.OpenTK => new OpenTKBackend(),
            _ => throw new ArgumentOutOfRangeException(nameof(api), api, null),
        };
    }

    public void Run()
    {
        LoadContent();
        Start();
        OnStart();
        _gameLoop.Run();
        DisposeResources();
    }

    private void Start()
    {
        SceneManager.Start();
    }

    private void Draw()
    {
        if (_snapshotsEnabled)
        {
            _snapshotTimer += _lastDeltaTime;

            if (_snapshotTimer >= SnapshotInterval)
            {
                _snapshotTimer -= SnapshotInterval;
                string path = $"Snapshots/snap_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bmp";

                Renderer.RequestSnapshot(path);
            }
        }

        Renderer.RenderFrame(
            SceneManager.MainCamera, SceneManager.Skybox, SceneManager.GameObjects, SceneManager.Lights,
            SelectedGameObject, _gameWindow.Width, _gameWindow.Height, Exposure);

        Renderer.ProcessSnapshot();
        _gameWindow.SwapBuffers();
    }

    private void OnWindowResized()
    {
        Renderer.OnWindowResized();
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _backend.Dispose();
    }
}