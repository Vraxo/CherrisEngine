using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Cherris.Rendering;
using Cherris.OpenTK;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris;

// A simple ray for picking.
public struct Ray
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    // Slab method for ray-AABB intersection.
    public bool Intersects(BoundingBox box, out float distance)
    {
        distance = 0.0f;
        float tmin = 0.0f;
        float tmax = float.MaxValue;

        if (Math.Abs(Direction.X) < 1e-6)
        {
            if (Origin.X < box.Min.X || Origin.X > box.Max.X) return false;
        }
        else
        {
            float ood = 1.0f / Direction.X;
            float t1 = (box.Min.X - Origin.X) * ood;
            float t2 = (box.Max.X - Origin.X) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(Direction.Y) < 1e-6)
        {
            if (Origin.Y < box.Min.Y || Origin.Y > box.Max.Y) return false;
        }
        else
        {
            float ood = 1.0f / Direction.Y;
            float t1 = (box.Min.Y - Origin.Y) * ood;
            float t2 = (box.Max.Y - Origin.Y) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(Direction.Z) < 1e-6)
        {
            if (Origin.Z < box.Min.Z || Origin.Z > box.Max.Z) return false;
        }
        else
        {
            float ood = 1.0f / Direction.Z;
            float t1 = (box.Min.Z - Origin.Z) * ood;
            float t2 = (box.Max.Z - Origin.Z) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        distance = tmin;
        return true;
    }
}


public abstract class Engine
{
    private readonly IGameWindow _gameWindow;
    private readonly GameLoop _gameLoop;
    private float _lastDeltaTime;
    private float _snapshotTimer;
    private const float SnapshotInterval = 1.0f;
    private bool _snapshotsEnabled;

    // Engine Systems
    protected readonly IGraphicsBackend _backend;
    public readonly IResourceManager ResourceManager;
    public readonly SceneLoader SceneLoader;
    public readonly SceneManager SceneManager;
    private readonly IRenderer _renderer;

    public Action<float>? OnDrawUI;
    protected GameObject? SelectedGameObject { get; set; } // Accessible for editor subclasses

    public float Exposure { get; set; } = 1.0f;

    protected Engine(string windowTitle, bool startWithMouseLocked, GraphicsAPI api)
    {
        _backend = CreateBackend(api);
        _backend.Initialize(windowTitle, 960, 540, startWithMouseLocked);

        _gameWindow = _backend.GameWindow;
        ResourceManager = _backend.ResourceManager;
        _renderer = _backend.Renderer;

        // Initialize systems that depend on the backend
        SceneLoader = new(ResourceManager);
        SceneManager = new();

        // Game loop is created last, as it depends on the Update/Draw methods
        _gameLoop = new(_gameWindow, Update, Draw);

        // Subscribe to resize event
        _gameWindow.Resized += OnWindowResized;

        RegisterEngineComponents();
    }

    private IGraphicsBackend CreateBackend(GraphicsAPI api)
    {
        switch (api)
        {
            case GraphicsAPI.Veldrid:
                return new VeldridBackend();
            case GraphicsAPI.OpenTK:
                return new OpenTKBackend();
            default:
                throw new ArgumentOutOfRangeException(nameof(api), api, null);
        }
    }

    private void RegisterEngineComponents()
    {
        SceneLoader.RegisterComponentFactory("MeshRenderer", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;

            Mesh mesh = null;
            if (propsDict.TryGetValue("Mesh", out var meshNameObj) && meshNameObj is string meshName)
                mesh = ResourceManager.GetMesh(meshName);
            if (mesh is null) return null;

            ITexture texture;
            if (propsDict.TryGetValue("Texture", out var textureNameObj) && textureNameObj is string textureName)
                texture = ResourceManager.GetTexture(textureName);
            else
                texture = ResourceManager.GetTexture("White");
            if (texture is null) return null;

            Vector2 textureTiling = Vector2.One;
            if (propsDict.TryGetValue("TextureTiling", out var tilingObj) && tilingObj is List<object> tilingList && tilingList.Count == 2)
            {
                try
                {
                    textureTiling = new Vector2(
                        Convert.ToSingle(tilingList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(tilingList[1], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Engine] Warning: Could not parse TextureTiling values. Using default. Error: {e.Message}");
                }
            }

            Vector3 emissiveColor = Vector3.Zero;
            if (propsDict.TryGetValue("EmissiveColor", out var emissiveObj) && emissiveObj is List<object> emissiveList && emissiveList.Count == 3)
            {
                try
                {
                    emissiveColor = new Vector3(
                        Convert.ToSingle(emissiveList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[1], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[2], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Engine] Warning: Could not parse EmissiveColor values. Using default. Error: {e.Message}");
                }
            }
            return new MeshRenderer(mesh, texture, textureTiling, emissiveColor);
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());
        SceneLoader.RegisterComponentFactory("Skybox", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;
            if (propsDict.TryGetValue("CubeMap", out var cubemapNameObj) && cubemapNameObj is string cubemapName)
                return ResourceManager.GetSkybox(cubemapName);
            return null;
        });
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

    protected virtual void OnStart() { }

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        _lastDeltaTime = deltaTime;

        _backend.UIController?.Update(deltaTime);
        OnDrawUI?.Invoke(deltaTime);

        if (Input.WasKeyPressed(Key.F12))
        {
            _snapshotsEnabled = !_snapshotsEnabled;
            Console.WriteLine($"[Engine] Snapshots {(_snapshotsEnabled ? "enabled" : "disabled")}. Press F12 to toggle.");
        }
    }

    public Ray CreateRayFromMouse()
    {
        Camera camera = SceneManager.MainCamera;
        if (camera is null) return new Ray();

        float x = (2.0f * Input.MousePosition.X) / _gameWindow.Width - 1.0f;
        float y = 1.0f - (2.0f * Input.MousePosition.Y) / _gameWindow.Height;
        var ndc = new Vector4(x, y, 1.0f, 1.0f);

        Matrix4x4.Invert(camera.GetProjectionMatrix(_gameWindow.Width / _gameWindow.Height), out var invProjection);
        var viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        var worldRay = Vector4.Transform(viewRay, invView);

        var rayDir = Vector3.Normalize(new Vector3(worldRay.X, worldRay.Y, worldRay.Z));
        var rayOrigin = camera.GameObject.Transform.Position;
        return new Ray(rayOrigin, rayDir);
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
                _renderer.RequestSnapshot(path);
            }
        }
        _renderer.RenderFrame(
            SceneManager.MainCamera, SceneManager.Skybox, SceneManager.GameObjects,
            SelectedGameObject, _gameWindow.Width, _gameWindow.Height, Exposure);

        _renderer.ProcessSnapshot();
        _gameWindow.SwapBuffers();
    }

    private void OnWindowResized()
    {
        _renderer.OnWindowResized();
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _backend.Dispose();
    }
}

// Concrete Veldrid backend implementation, nested here to avoid creating new files.
public class VeldridBackend : IGraphicsBackend
{
    public IGameWindow GameWindow { get; private set; }
    public IRenderer Renderer { get; private set; }
    public IResourceManager ResourceManager { get; private set; }
    public IUIController? UIController => null; // ImGui not implemented for Veldrid yet
    private GraphicsDevice _graphicsDevice;
    private GraphicsManager _graphicsManager;

    public VeldridBackend() { }

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        var window = new GameWindow(windowTitle, width, height, startWithMouseLocked);

        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window.SdlWindow, options);
        window.SetGraphicsDevice(_graphicsDevice);

        _graphicsManager = new GraphicsManager(_graphicsDevice, window.SdlWindow, TextureSampleCount.Count4);

        GameWindow = window;
        ResourceManager = new ResourceManager(_graphicsDevice);
        Renderer = new Renderer(_graphicsManager, _graphicsDevice);
    }

    public void Dispose()
    {
        Renderer?.Dispose();
        ResourceManager?.Dispose();
        _graphicsDevice?.Dispose();
        GameWindow?.Dispose();
    }
}