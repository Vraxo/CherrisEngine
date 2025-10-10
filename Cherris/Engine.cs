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

    // Engine-level resources
    private DeviceBuffer _mvpBuffer;
    private Pipeline _pipeline;
    private ResourceSet _mvpResourceSet;

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

            if (propsDict.TryGetValue("Mesh", out var meshNameObj) && meshNameObj is string meshName)
            {
                Mesh mesh = ResourceManager.GetMesh(meshName);
                if (mesh != null)
                {
                    return new MeshRenderer(mesh, _graphicsDevice);
                }
            }
            return null;
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) => new Camera());
    }

    public void Run()
    {
        LoadContent();
        Start();
        _stopwatch.Start();

        Sdl2Native.SDL_SetRelativeMouseMode(true);
        _window.PumpEvents(); // Pump once to flush initial mouse position.

        while (_window.Exists)
        {
            InputSnapshot snapshot = _window.PumpEvents();
            Input.UpdateSnapshot(snapshot);

            if (!_window.Exists) break;

            float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            Update(deltaTime);
            Draw();
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
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        ResourceLayout mvpLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MvpBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

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
            ResourceLayouts = new[] { mvpLayout },
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
                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };
                layout(location = 0) out vec4 fsin_Color;
                void main() { gl_Position = mvp * vec4(Position, 1); fsin_Color = Color; }";

        const string fragmentCode = @"
                #version 450
                layout(location = 0) in vec4 fsin_Color;
                layout(location = 0) out vec4 fsout_Color;
                void main() { fsout_Color = fsin_Color; }";

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
        _mvpResourceSet.Dispose();
        _mvpBuffer.Dispose();
        ResourceManager.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }
}