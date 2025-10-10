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
    private readonly GameWindow _gameWindow;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly CommandList _commandList;
    private readonly Stopwatch _stopwatch;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private readonly Renderer _renderer;

    protected Engine(string windowTitle)
    {
        _gameWindow = new GameWindow(windowTitle, 960, 540);

        GraphicsDeviceOptions options = new GraphicsDeviceOptions
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_gameWindow.SdlWindow, options);
        _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        _stopwatch = new Stopwatch();

        // Initialize systems
        ResourceManager = new ResourceManager(_graphicsDevice);
        SceneLoader = new SceneLoader(ResourceManager, _graphicsDevice);
        SceneManager = new SceneManager();
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

        while (_gameWindow.Exists)
        {
            _gameWindow.ProcessEvents();

            if (!_gameWindow.Exists) break;

            float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            Update(deltaTime);
            Draw();
        }

        DisposeResources();
    }

    private void Start()
    {
        SceneManager.Start();
    }

    protected GraphicsDevice GetGraphicsDevice() => _graphicsDevice;

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        SceneManager.Update(deltaTime);
    }

    private void Draw()
    {
        var mainCamera = SceneManager.MainCamera;
        if (mainCamera == null) return;

        Matrix4x4 view = mainCamera.GetViewMatrix();
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(_gameWindow.Width / _gameWindow.Height);

        _commandList.Begin();
        _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.ClearDepthStencil(1f);

        _renderer.RenderScene(_commandList, view, projection, SceneManager.GameObjects);

        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _renderer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }
}