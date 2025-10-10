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

    // MSAA resources
    private Veldrid.Texture _msaaColorTexture;
    private Veldrid.Texture _msaaDepthTexture;
    private Framebuffer _msaaFramebuffer;
    private TextureSampleCount _sampleCount;

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

        // Determine sample count for MSAA
        _sampleCount = GetSupportedSampleCount();

        // Initialize systems
        ResourceManager = new ResourceManager(_graphicsDevice);
        SceneLoader = new SceneLoader(ResourceManager, _graphicsDevice);
        SceneManager = new SceneManager();
        _renderer = new Renderer(_graphicsDevice, _sampleCount);

        CreateMsaaResources();
        _gameWindow.SdlWindow.Resized += HandleWindowResize;

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

    private TextureSampleCount GetSupportedSampleCount()
    {
        // We check for the highest supported count, but cap it at 4x for a good balance
        // of quality and performance. More can be exposed as a setting later.
        var pixelFormat = _graphicsDevice.SwapchainFramebuffer.ColorTargets[0].Target.Format;
        TextureSampleCount maxSamples = _graphicsDevice.GetSampleCountLimit(pixelFormat, false);

        if (maxSamples >= TextureSampleCount.Count4) return TextureSampleCount.Count4;
        if (maxSamples >= TextureSampleCount.Count2) return TextureSampleCount.Count2;
        return TextureSampleCount.Count1;
    }

    private void CreateMsaaResources()
    {
        // Dispose old resources if they exist. This is important for window resizing.
        _msaaColorTexture?.Dispose();
        _msaaDepthTexture?.Dispose();
        _msaaFramebuffer?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        uint width = (uint)_gameWindow.Width;
        uint height = (uint)_gameWindow.Height;

        var pixelFormat = _graphicsDevice.SwapchainFramebuffer.ColorTargets[0].Target.Format;
        var depthFormat = _graphicsDevice.SwapchainFramebuffer.DepthTarget.Value.Target.Format;

        TextureDescription msaaColorDesc = TextureDescription.Texture2D(
            width, height, 1, 1,
            pixelFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled,
            _sampleCount);
        _msaaColorTexture = factory.CreateTexture(msaaColorDesc);

        TextureDescription msaaDepthDesc = TextureDescription.Texture2D(
            width, height, 1, 1,
            depthFormat,
            TextureUsage.DepthStencil,
            _sampleCount);
        _msaaDepthTexture = factory.CreateTexture(msaaDepthDesc);

        _msaaFramebuffer = factory.CreateFramebuffer(new FramebufferDescription
        {
            ColorTargets = new[] { new FramebufferAttachmentDescription(_msaaColorTexture, 0) },
            DepthTarget = new FramebufferAttachmentDescription(_msaaDepthTexture, 0)
        });
    }

    private void HandleWindowResize()
    {
        // This resizes the swapchain.
        _graphicsDevice.ResizeMainWindow((uint)_gameWindow.Width, (uint)_gameWindow.Height);

        // Now we need to recreate our MSAA resources with the new size.
        CreateMsaaResources();
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
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(_gameWindow.Width / (float)_gameWindow.Height);

        _commandList.Begin();
        _commandList.SetFramebuffer(_msaaFramebuffer); // Render to our offscreen MSAA framebuffer
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.ClearDepthStencil(1f);

        var skybox = SceneManager.Skybox;
        if (skybox != null)
        {
            _renderer.RenderSkybox(_commandList, skybox, view, projection);
        }

        _renderer.RenderScene(_commandList, view, projection, SceneManager.GameObjects);

        // After rendering the scene to the MSAA framebuffer, resolve it to the main swapchain.
        _commandList.ResolveTexture(_msaaColorTexture, _graphicsDevice.SwapchainFramebuffer.ColorTargets[0].Target);

        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }

    private void DisposeResources()
    {
        _gameWindow.SdlWindow.Resized -= HandleWindowResize;

        SceneManager.Dispose();
        _renderer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();

        // Dispose MSAA resources
        _msaaColorTexture.Dispose();
        _msaaDepthTexture.Dispose();
        _msaaFramebuffer.Dispose();

        _graphicsDevice.Dispose();
    }
}