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
    private GameObject _selectedGameObject;
    private Script _editorController;
    private readonly Snapshotter _snapshotter;
    private float _lastDeltaTime;
    private float _snapshotTimer;
    private const float SnapshotInterval = 1.0f;
    private bool _snapshotsEnabled;
    private bool _f12PressedLastFrame;


    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private readonly Renderer _renderer;

    protected EngineMode Mode { get; }

    protected Engine(string windowTitle, EngineMode mode = EngineMode.Game)
    {
        Mode = mode;
        _gameWindow = new(windowTitle, 960, 540, startWithMouseLocked: Mode == EngineMode.Game);
        _graphicsManager = new(_gameWindow.SdlWindow, TextureSampleCount.Count4);

        // Initialize systems
        ResourceManager = new(_graphicsManager.GraphicsDevice);
        SceneLoader = new(ResourceManager, _graphicsManager.GraphicsDevice);
        SceneManager = new();
        _renderer = new(_graphicsManager);
        _snapshotter = new(_graphicsManager.GraphicsDevice);
        _renderer.SetSnapshotter(_snapshotter);

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
        OnStart();
        _gameLoop.Run();
        DisposeResources();
    }

    private void Start()
    {
        SceneManager.Start(Mode);
    }

    protected virtual void OnStart() { }

    protected void RegisterEditorController(Script controller)
    {
        _editorController = controller;
        _editorController.Start();
    }

    protected GraphicsDevice GetGraphicsDevice() => _graphicsManager.GraphicsDevice;

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        _lastDeltaTime = deltaTime;

        bool isF12Down = Input.IsKeyDown(Key.F12);
        if (isF12Down && !_f12PressedLastFrame)
        {
            _snapshotsEnabled = !_snapshotsEnabled;
            Console.WriteLine($"[Engine] Snapshots {(_snapshotsEnabled ? "enabled" : "disabled")}. Press F12 to toggle.");
        }
        _f12PressedLastFrame = isF12Down;

        if (Mode == EngineMode.Editor)
        {
            _editorController?.Update(deltaTime);
            UpdateEditor(deltaTime);
        }
        else // Only run scripts in Game mode
        {
            SceneManager.Update(deltaTime);
        }
    }

    private void UpdateEditor(float deltaTime)
    {
        // --- Object Picking ---
        if (Input.WasMouseButtonPressed(MouseButton.Left))
        {
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

        // --- Object Movement ---
        if (_selectedGameObject is not null)
        {
            const float moveSpeed = 2.0f;
            var moveDirection = Vector3.Zero;
            bool shiftHeld = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);

            // Left/Right on X-axis
            if (Input.IsKeyDown(Key.Left)) moveDirection.X -= 1;
            if (Input.IsKeyDown(Key.Right)) moveDirection.X += 1;

            if (shiftHeld)
            {
                // Up/Down on Y-axis (vertical)
                if (Input.IsKeyDown(Key.Up)) moveDirection.Y += 1;
                if (Input.IsKeyDown(Key.Down)) moveDirection.Y -= 1;
            }
            else
            {
                // Up/Down on Z-axis (depth)
                if (Input.IsKeyDown(Key.Up)) moveDirection.Z -= 1;
                if (Input.IsKeyDown(Key.Down)) moveDirection.Z += 1;
            }

            if (moveDirection != Vector3.Zero)
            {
                _selectedGameObject.Transform.Position += Vector3.Normalize(moveDirection) * moveSpeed * deltaTime;
            }
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

        // --- Prepare Outline Data ---
        // Get all unique profile names currently in use by the system.
        var activeProfileNames = new List<string>(OutlineSystem.ObjectsByProfile.Keys);
        var activeProfiles = new List<OutlineProfile>();
        foreach (var name in activeProfileNames)
        {
            var profile = ResourceManager.GetOutlineProfile(name);
            if (profile != null)
            {
                activeProfiles.Add(profile);
            }
        }

        // Also add a default profile for the selected object, if it doesn't have one.
        if (_selectedGameObject != null)
        {
            var selectionProfile = ResourceManager.GetOutlineProfile("Selection");
            if (selectionProfile == null) // Create a default if not defined in scene
            {
                selectionProfile = new OutlineProfile { Name = "Selection", Color = RgbaFloat.Yellow, Thickness = 2.0f };
            }
            if (activeProfiles.All(p => p.Name != "Selection"))
            {
                activeProfiles.Add(selectionProfile);
            }
        }

        _renderer.RenderFrame(
            SceneManager.MainCamera,
            SceneManager.Skybox,
            SceneManager.GameObjects,
            _selectedGameObject,
            activeProfiles,
            _gameWindow.Width,
            _gameWindow.Height);

        _renderer.ProcessSnapshot();
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
        _snapshotter.Dispose();
        _graphicsManager.Dispose();
    }
}