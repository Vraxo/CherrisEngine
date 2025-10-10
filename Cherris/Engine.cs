using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    // Engine-level resources
    private DeviceBuffer _mvpBuffer;
    private Pipeline _pipeline;
    private ResourceSet _mvpResourceSet;
    private ResourceLayout _textureLayout;
    private Sampler _sampler;

    // Engine Systems
    protected readonly ResourceManager ResourceManager;
    protected readonly SceneLoader SceneLoader;

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

        RegisterEngineComponents();

        CreateGlobalResources();
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


            return new MeshRenderer(mesh, _graphicsDevice, _textureLayout, _sampler, texture);
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

    private void CreateGlobalResources()
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        _mvpBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        ResourceLayout mvpLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MvpBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _textureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _sampler = factory.CreateSampler(SamplerDescription.Linear);

        _mvpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(mvpLayout, _mvpBuffer));

        (Shader vs, Shader fs) = LoadShaders(factory);

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                true, true, ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { mvpLayout, _textureLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
            Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription
        });
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

        foreach (var gameObject in Scene)
        {
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) continue;

            Matrix4x4 mvp = gameObject.Transform.GetModelMatrix() * view * projection;
            _commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

            meshRenderer.Render(_commandList, _pipeline, _mvpResourceSet);
        }

        _commandList.End();
        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers();
    }

    private (Shader, Shader) LoadShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 1) in vec4 Color;
                layout(location = 2) in vec2 TexCoord;

                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };

                layout(location = 0) out vec4 fsin_Color;
                layout(location = 1) out vec2 fsin_TexCoord;

                void main() 
                { 
                    gl_Position = mvp * vec4(Position, 1); 
                    fsin_Color = Color; 
                    fsin_TexCoord = TexCoord;
                }";

        const string fragmentCode = @"
                #version 450
                layout(location = 0) in vec4 fsin_Color;
                layout(location = 1) in vec2 fsin_TexCoord;

                layout(set = 1, binding = 0) uniform texture2D SourceTexture;
                layout(set = 1, binding = 1) uniform sampler SourceSampler;

                layout(location = 0) out vec4 fsout_Color;

                void main() 
                { 
                    fsout_Color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord) * fsin_Color;
                }";

        ShaderDescription vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

        Shader[] shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    private void DisposeResources()
    {
        foreach (var gameObject in Scene)
        {
            gameObject.GetComponent<MeshRenderer>()?.Dispose();
        }
        _pipeline.Dispose();
        _textureLayout.Dispose();
        _sampler.Dispose();
        _mvpResourceSet.Dispose();
        _mvpBuffer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }
}