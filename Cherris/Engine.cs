using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace Cherris;

public abstract class Engine
{
    private readonly Sdl2Window _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly CommandList _commandList;
    private readonly Stopwatch _stopwatch;
    private bool _escapePressedLastFrame = false;
    private Vector2 _windowCenter;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    private readonly Renderer _renderer;

    protected readonly List<GameObject> Scene = new List<GameObject>();
    protected Camera MainCamera { get; private set; }

    protected Engine(string windowTitle)
    {
        WindowCreateInfo windowCI = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = 960,
            WindowHeight = 540,
            WindowTitle = windowTitle
        };
        _window = VeldridStartup.CreateWindow(ref windowCI);

        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, options);
        _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        _stopwatch = new Stopwatch();

        // Initialize systems
        ResourceManager = new ResourceManager(_graphicsDevice);
        SceneLoader = new SceneLoader(ResourceManager, _graphicsDevice);
        _renderer = new Renderer(_graphicsDevice);

        RegisterEngineComponents();
    }

    private void RegisterEngineComponents()
    {
        SceneLoader.RegisterComponentFactory("MeshRenderer", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;

            Mesh mesh = null;
            if (propsDict.TryGetValue("Mesh", out var meshNameObj) && meshNameObj is string meshName)
            {
                mesh = ResourceManager.GetMesh(meshName);
            }
            if (mesh == null) return null;

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
            if (texture == null) return null;

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

            return new MeshRenderer(mesh, _graphicsDevice, _renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler, texture, textureTiling);
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());
    }

    public void Run()
    {
        LoadContent();
        Start();
        _stopwatch.Start();

        _windowCenter = new Vector2(_window.Width / 2f, _window.Height / 2f);

        // Replaced Sdl2Native.SDL_SetRelativeMouseMode with manual cursor management.
        Input.IsMouseLocked = true;
        Sdl2Native.SDL_ShowCursor(0); // Hide cursor.
        // Center mouse initially and pump events to clear the warp event from the queue.
        Sdl2Native.SDL_WarpMouseInWindow(_window.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
        _window.PumpEvents();

        while (_window.Exists)
        {
            InputSnapshot snapshot = _window.PumpEvents();
            Input.UpdateSnapshot(snapshot, _windowCenter);

            // Toggle mouse lock state on Escape key press
            bool isEscapeDown = Input.IsKeyDown(Key.Escape);
            if (isEscapeDown && !_escapePressedLastFrame)
            {
                Input.IsMouseLocked = !Input.IsMouseLocked;
                Sdl2Native.SDL_ShowCursor(Input.IsMouseLocked ? 0 : 1);
                if (Input.IsMouseLocked)
                {
                    // When re-locking, center mouse immediately to prepare for next frame's input.
                    Sdl2Native.SDL_WarpMouseInWindow(_window.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
                }
            }
            _escapePressedLastFrame = isEscapeDown;

            if (!_window.Exists) break;

            float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            Update(deltaTime);
            Draw();

            // If the mouse is locked, re-center it for the next frame's delta calculation.
            if (Input.IsMouseLocked)
            {
                Sdl2Native.SDL_WarpMouseInWindow(_window.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
            }
        }

        DisposeResources();
    }

    private void Start()
    {
        foreach (var gameObject in Scene)
        {
            var camera = gameObject.GetComponent<Camera>();
            if (camera != null)
            {
                MainCamera = camera;
            }

            foreach (var script in gameObject.GetComponents<Script>())
            {
                script.Start();
            }
        }

        if (MainCamera == null)
        {
            Console.WriteLine("Warning: No camera found in scene. Creating a default one.");
            var go = new GameObject("Default Camera");
            go.Transform.Position = new Vector3(0, 1, 3);
            MainCamera = go.AddComponent(new Camera());
            Scene.Add(go);
        }
    }

    protected GraphicsDevice GetGraphicsDevice() => _graphicsDevice;

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        foreach (var gameObject in Scene)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                script.Update(deltaTime);
            }
        }
    }

    private void Draw()
    {
        if (MainCamera == null) return;

        Matrix4x4 view = MainCamera.GetViewMatrix();
        Matrix4x4 projection = MainCamera.GetProjectionMatrix((float)_window.Width / _window.Height);

        _commandList.Begin();
        _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.ClearDepthStencil(1f);

        _renderer.RenderScene(_commandList, view, projection, Scene);

        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }

    private void DisposeResources()
    {
        foreach (var gameObject in Scene)
        {
            gameObject.GetComponent<MeshRenderer>()?.Dispose();
        }
        _renderer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }
}