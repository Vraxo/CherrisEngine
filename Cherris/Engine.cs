using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Veldrid;

namespace Cherris;

public abstract class Engine
{
    private readonly GameWindow _gameWindow;
    private readonly GraphicsManager _graphicsManager;
    private readonly GameLoop _gameLoop;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private readonly Renderer _renderer;

    protected Engine(string windowTitle)
    {
        _gameWindow = new(windowTitle, 960, 540);
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
        SceneManager.Start();
    }

    protected GraphicsDevice GetGraphicsDevice() => _graphicsManager.GraphicsDevice;

    protected abstract void LoadContent();

    protected virtual void Update(float deltaTime)
    {
        SceneManager.Update(deltaTime);
    }

    private void Draw()
    {
        _renderer.RenderFrame(
            SceneManager.MainCamera,
            SceneManager.Skybox,
            SceneManager.GameObjects,
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