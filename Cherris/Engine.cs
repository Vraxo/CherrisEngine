using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Veldrid;

namespace Cherris;

public enum EngineMode
{
    Game,
    Editor
}

public abstract class Engine
{
    // A simple ray for picking.
    private struct Ray
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

    private readonly GameWindow _gameWindow;
    private readonly GraphicsManager _graphicsManager;
    private readonly GameLoop _gameLoop;
    private readonly EngineMode _mode;
    private GameObject _selectedGameObject;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private readonly Renderer _renderer;

    protected Engine(string windowTitle, EngineMode mode = EngineMode.Game)
    {
        _mode = mode;
        _gameWindow = new(windowTitle, 960, 540, startWithMouseLocked: _mode == EngineMode.Game);
        _graphicsManager = new(_gameWindow.SdlWindow, TextureSampleCount.Count4);

        // Initialize systems
        ResourceManager = new(_graphicsManager.GraphicsDevice);
        SceneLoader = new(ResourceManager, _graphicsManager.GraphicsDevice);
        SceneManager = new();
        _renderer = new(_graphicsManager);

        // Game loop is created last, as it depends on the Update/Draw methods
        _gameLoop = new(_gameWindow, Update, Draw);

        // Subscribe to resize event
        _gameWindow.SdlWindow.Resized += OnWindowResized;

        RegisterEngineComponents();
    }

    private void RegisterEngineComponents()
    {
        SceneLoader.RegisterComponentFactory("MeshRenderer", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict)
            {
                return null;
            }

            Mesh? mesh = null;

            if (propsDict.TryGetValue("Mesh", out var meshNameObj) && meshNameObj is string meshName)
            {
                mesh = ResourceManager.GetMesh(meshName);
            }

            if (mesh is null)
            {
                return null;
            }

            Texture texture;

            if (propsDict.TryGetValue("Texture", out var textureNameObj) && textureNameObj is string textureName)
            {
                texture = ResourceManager.GetTexture(textureName);
            }
            else
            {
                // Use a default white texture if none is specified
                texture = ResourceManager.GetTexture("White");
            }

            if (texture is null)
            {
                return null;
            }

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

            return new MeshRenderer(
                mesh,
                _graphicsManager.GraphicsDevice,
                _renderer.TextureLayout,
                _renderer.MaterialLayout,
                _renderer.Sampler,
                texture,
                textureTiling);
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());

        SceneLoader.RegisterComponentFactory("Skybox", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict)
            {
                return null;
            }

            if (propsDict.TryGetValue("CubeMap", out var cubemapNameObj) && cubemapNameObj is string cubemapName)
            {
                return ResourceManager.GetSkybox(cubemapName);
            }
            return null;
        });
    }

    public void Run()
    {
        LoadContent();
        Start();
        _gameLoop.Run();
        DisposeResources();
    }

    private void Start()
    {
        SceneManager.Start(_mode);
    }

    protected GraphicsDevice GetGraphicsDevice() => _graphicsManager.GraphicsDevice;

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        if (_mode == EngineMode.Editor)
        {
            UpdateEditor();
        }
        else // Only run scripts in Game mode
        {
            SceneManager.Update(deltaTime);
        }
    }

    private void UpdateEditor()
    {
        if (!Input.WasMouseButtonPressed(MouseButton.Left))
        {
            return;
        }

        Ray ray = CreateRayFromMouse();

        GameObject closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (var go in SceneManager.GameObjects)
        {
            // Don't allow selecting the skybox container or the player/camera itself.
            if (go.GetComponent<Skybox>() != null || go.GetComponent<Camera>() != null) continue;

            var aabb = go.GetWorldSpaceAABB();
            if (ray.Intersects(aabb, out float distance))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestObject = go;
                }
            }
        }

        _selectedGameObject = closestObject;
        if (_selectedGameObject is not null)
        {
            Console.WriteLine($"Selected '{_selectedGameObject.Name}'");
        }
    }

    private Ray CreateRayFromMouse()
    {
        Camera camera = SceneManager.MainCamera;
        if (camera is null) return new Ray();

        // 1. Mouse coordinates to Normalized Device Coordinates (NDC)
        float x = (2.0f * Input.MousePosition.X) / _gameWindow.Width - 1.0f;
        float y = 1.0f - (2.0f * Input.MousePosition.Y) / _gameWindow.Height;
        var ndc = new Vector4(x, y, 1.0f, 1.0f);

        // 2. NDC to Camera (View) space
        Matrix4x4.Invert(camera.GetProjectionMatrix(_gameWindow.Width / _gameWindow.Height), out var invProjection);
        var viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        // 3. Camera (View) space to World space
        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        var worldRay = Vector4.Transform(viewRay, invView);

        var rayDir = Vector3.Normalize(new Vector3(worldRay.X, worldRay.Y, worldRay.Z));
        var rayOrigin = camera.GameObject.Transform.Position;

        return new Ray(rayOrigin, rayDir);
    }

    private void Draw()
    {
        _renderer.RenderFrame(
            SceneManager.MainCamera,
            SceneManager.Skybox,
            SceneManager.GameObjects,
            _selectedGameObject,
            _gameWindow.Width,
            _gameWindow.Height);
    }

    private void OnWindowResized()
    {
        _graphicsManager.Resize((int)_gameWindow.Width, (int)_gameWindow.Height);
        _renderer.OnWindowResized();
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _renderer.Dispose();
        ResourceManager.Dispose();
        _graphicsManager.Dispose();
    }
}