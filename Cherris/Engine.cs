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
    private readonly GraphicsDevice _graphicsDevice;
    private readonly CommandList _commandList;
    private readonly Stopwatch _stopwatch;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;
    protected readonly SceneManager SceneManager;
    private Renderer _renderer; // Removed readonly

    private Framebuffer _msaaFramebuffer;
    private Veldrid.Texture _msaaColorTarget;
    private Veldrid.Texture _msaaDepthTarget;
    private TextureView _msaaColorView;
    private readonly TextureSampleCount _msaaSampleCount = TextureSampleCount.Count4; // Or Count8 for stronger AA.

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

        // Get the swapchain color format
        PixelFormat swapchainFormat = _graphicsDevice.SwapchainFramebuffer.ColorTargets[0].Target.Format;
        PixelFormat colorFormat = GetNonSrgbFormat(swapchainFormat);

        // Create MSAA framebuffer
        uint width = _graphicsDevice.SwapchainFramebuffer.Width;
        uint height = _graphicsDevice.SwapchainFramebuffer.Height;

        _msaaColorTarget = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, colorFormat, // Use non-sRGB format for correct MSAA
            TextureUsage.RenderTarget | TextureUsage.Sampled,
            sampleCount: _msaaSampleCount));

        _msaaDepthTarget = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R16_UNorm, // Match your SwapchainDepthFormat.
            TextureUsage.DepthStencil,
            sampleCount: _msaaSampleCount));

        _msaaFramebuffer = _graphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
            _msaaDepthTarget, _msaaColorTarget));

        _msaaColorView = _graphicsDevice.ResourceFactory.CreateTextureView(_msaaColorTarget);

        // Initialize systems after MSAA creation
        ResourceManager = new ResourceManager(_graphicsDevice);
        SceneLoader = new SceneLoader(ResourceManager, _graphicsDevice);
        SceneManager = new SceneManager();
        _renderer = new Renderer(_graphicsDevice, _msaaFramebuffer, _graphicsDevice.SwapchainFramebuffer); // Pass both framebuffers

        // Subscribe to resize event
        _gameWindow.SdlWindow.Resized += OnWindowResized;

        RegisterEngineComponents();
    }

    private static PixelFormat GetNonSrgbFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.B8_G8_R8_A8_UNorm_SRgb => PixelFormat.B8_G8_R8_A8_UNorm,
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb => PixelFormat.R8_G8_B8_A8_UNorm,
            _ => format
        };
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

        // Render to MSAA framebuffer.
        _commandList.SetFramebuffer(_msaaFramebuffer);
        _commandList.SetViewport(0, new Viewport(0, 0, _gameWindow.Width, _gameWindow.Height, 0, 1));
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.ClearDepthStencil(1f);

        var skybox = SceneManager.Skybox;
        if (skybox != null)
        {
            _renderer.RenderSkybox(_commandList, skybox, view, projection);
        }

        _renderer.RenderScene(_commandList, view, projection, SceneManager.GameObjects);

        // Switch to swapchain and resolve MSAA with custom shader for sRGB conversion
        _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
        _commandList.SetViewport(0, new Viewport(0, 0, _gameWindow.Width, _gameWindow.Height, 0, 1));
        _commandList.ClearColorTarget(0, RgbaFloat.Black); // Optional, since resolve covers the screen
        _renderer.ResolveMSAA(_commandList, _msaaColorView);

        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }

private void OnWindowResized()
{
    _graphicsDevice.ResizeMainWindow((uint)_gameWindow.Width, (uint)_gameWindow.Height);

    // Get the swapchain color format
    PixelFormat swapchainFormat = _graphicsDevice.SwapchainFramebuffer.ColorTargets[0].Target.Format;
    PixelFormat colorFormat = GetNonSrgbFormat(swapchainFormat);

    // Dispose old MSAA targets and framebuffer
    _msaaColorView?.Dispose();
    _msaaColorTarget?.Dispose();
    _msaaDepthTarget?.Dispose();
    _msaaFramebuffer?.Dispose();

    uint width = (uint)_gameWindow.Width;
    uint height = (uint)_gameWindow.Height;

    _msaaColorTarget = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
        width, height, 1, 1, colorFormat,
        TextureUsage.RenderTarget | TextureUsage.Sampled,
        sampleCount: _msaaSampleCount));

    _msaaDepthTarget = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
        width, height, 1, 1, PixelFormat.R16_UNorm,
        TextureUsage.DepthStencil,
        sampleCount: _msaaSampleCount));

    _msaaFramebuffer = _graphicsDevice.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
        _msaaDepthTarget, _msaaColorTarget));

    _msaaColorView = _graphicsDevice.ResourceFactory.CreateTextureView(_msaaColorTarget);

    // Recreate renderer with new MSAA framebuffer
    _renderer?.Dispose();
    _renderer = new Renderer(_graphicsDevice, _msaaFramebuffer, _graphicsDevice.SwapchainFramebuffer);

    // --- CRITICAL: Recreate every MeshRenderer's resources so their ResourceSets reference the new layouts/sampler ---
    // Best-effort: handle common patterns (GetComponent<T>() or Components collection) using reflection so you don't need to edit SceneManager/GameObject.
    if (SceneManager?.GameObjects != null)
    {
        foreach (var go in SceneManager.GameObjects)
        {
            if (go == null) continue;

            bool handled = false;
            var goType = go.GetType();

            // 1) Try GetComponent<MeshRenderer>() pattern (generic method)
            var methods = goType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var mi in methods)
            {
                if (mi.Name == "GetComponent" && mi.IsGenericMethodDefinition && mi.GetParameters().Length == 0)
                {
                    try
                    {
                        var generic = mi.MakeGenericMethod(typeof(MeshRenderer));
                        var result = generic.Invoke(go, null);
                        if (result is MeshRenderer mr)
                        {
                            mr.RecreateResources(_renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler);
                            handled = true;
                        }
                    }
                    catch
                    {
                        // ignore reflection exceptions and try other approaches
                    }
                    break;
                }
            }

            if (handled) continue;

            // 2) Try a Components property/field that is enumerable
            PropertyInfo compsProp = goType.GetProperty("Components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                ?? goType.GetProperty("components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (compsProp != null)
            {
                var compsObj = compsProp.GetValue(go);
                if (compsObj is System.Collections.IEnumerable compsEnum)
                {
                    foreach (var c in compsEnum)
                    {
                        if (c is MeshRenderer mr)
                        {
                            mr.RecreateResources(_renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler);
                        }
                    }
                    continue;
                }
            }

            FieldInfo compsField = goType.GetField("Components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? goType.GetField("components", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (compsField != null)
            {
                var compsObj = compsField.GetValue(go);
                if (compsObj is System.Collections.IEnumerable compsEnum)
                {
                    foreach (var c in compsEnum)
                    {
                        if (c is MeshRenderer mr)
                        {
                            mr.RecreateResources(_renderer.TextureLayout, _renderer.MaterialLayout, _renderer.Sampler);
                        }
                    }
                }
            }

            // If neither approach worked, you likely need to add an explicit method to SceneManager to re-initialize render-dependent resources.
        }
    }
}
private void DisposeResources()
    {
        SceneManager.Dispose();
        _renderer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();
        _msaaColorView?.Dispose();
        _msaaColorTarget?.Dispose();
        _msaaDepthTarget?.Dispose();
        _msaaFramebuffer?.Dispose();
        _graphicsDevice.Dispose();
    }
}