using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace Cherris;

public abstract class Engine
{
    private readonly GameWindow _gameWindow;
    private readonly GraphicsManager _graphicsManager;
    private readonly Stopwatch _stopwatch;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private Renderer _renderer;

    protected Engine(string windowTitle)
    {
        _gameWindow = new GameWindow(windowTitle, 960, 540);
        _graphicsManager = new GraphicsManager(_gameWindow.SdlWindow, TextureSampleCount.Count4);
        _stopwatch = new Stopwatch();

        // Initialize systems
        ResourceManager = new ResourceManager(_graphicsManager.GraphicsDevice);
        SceneLoader = new SceneLoader(ResourceManager, _graphicsManager.GraphicsDevice);
        SceneManager = new SceneManager();
        _renderer = new Renderer(_graphicsManager.GraphicsDevice, _graphicsManager.MsaaFramebuffer, _graphicsManager.SwapchainFramebuffer);

        // Subscribe to resize event
        _gameWindow.SdlWindow.Resized += OnWindowResized;

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

            return new MeshRenderer(mesh, _graphicsManager.GraphicsDevice, _renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler, texture, textureTiling);
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());

        SceneLoader.RegisterComponentFactory("Skybox", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;

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

    protected GraphicsDevice GetGraphicsDevice() => _graphicsManager.GraphicsDevice;

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
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(_gameWindow.Width / (float)_gameWindow.Height);

        CommandList cl = _graphicsManager.CommandList;
        cl.Begin();

        // Render to MSAA framebuffer.
        cl.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, _gameWindow.Width, _gameWindow.Height, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f);

        var skybox = SceneManager.Skybox;
        if (skybox != null)
        {
            _renderer.RenderSkybox(cl, skybox, view, projection);
        }

        _renderer.RenderScene(cl, view, projection, SceneManager.GameObjects);

        // Switch to swapchain and resolve MSAA with custom shader for sRGB conversion
        cl.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, _gameWindow.Width, _gameWindow.Height, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black); // Optional, since resolve covers the screen
        _renderer.ResolveMSAA(cl, _graphicsManager.MsaaColorView);

        cl.End();
        _graphicsManager.GraphicsDevice.SubmitCommands(cl);
        _graphicsManager.GraphicsDevice.SwapBuffers();
    }

    private void OnWindowResized()
    {
        _graphicsManager.Resize((int)_gameWindow.Width, (int)_gameWindow.Height);

        // Recreate renderer with new MSAA framebuffer
        _renderer?.Dispose();
        _renderer = new Renderer(_graphicsManager.GraphicsDevice, _graphicsManager.MsaaFramebuffer, _graphicsManager.SwapchainFramebuffer);

        // Recreate every MeshRenderer's resources so their ResourceSets reference the new layouts/sampler
        if (SceneManager?.GameObjects != null)
        {
            foreach (var go in SceneManager.GameObjects)
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.RecreateResources(_renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler);
                }
            }
        }
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _renderer.Dispose();
        ResourceManager.Dispose();
        _graphicsManager.Dispose();
    }
}