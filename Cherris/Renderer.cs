using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class Renderer : IDisposable
{
    private readonly GraphicsManager _graphicsManager;
    private SceneRenderer _sceneRenderer;
    private SkyboxRenderer _skyboxRenderer;
    private ResolveRenderer _resolveRenderer;
    private BlitRenderer _blitRenderer;
    private Sampler _sampler;
    private Snapshotter _snapshotter;
    private bool _snapshotRequested;
    private string _snapshotPath;

    public ResourceLayout TextureLayout => _sceneRenderer.TextureLayout;
    public ResourceLayout MaterialLayout => _sceneRenderer.MaterialLayout;
    public Sampler Sampler => _sampler;

    public Renderer(GraphicsManager graphicsManager)
    {
        _graphicsManager = graphicsManager;
        CreateResources();
    }

    public void SetSnapshotter(Snapshotter snapshotter)
    {
        _snapshotter = snapshotter;
    }

    public void RequestSnapshot(string path)
    {
        _snapshotRequested = true;
        _snapshotPath = path;
    }

    public void ProcessSnapshot()
    {
        _snapshotter?.SaveCopiedData(_snapshotPath);
    }

    private void CreateResources()
    {
        GraphicsDevice gd = _graphicsManager.GraphicsDevice;
        ResourceFactory factory = gd.ResourceFactory;

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _sampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Wrap,
            AddressModeV = SamplerAddressMode.Wrap,
            AddressModeW = SamplerAddressMode.Wrap,
            Filter = SamplerFilter.Anisotropic,
            MaximumAnisotropy = 16,
            LodBias = 0,
            MinimumLod = 0,
            MaximumLod = uint.MaxValue
        });

        _sceneRenderer = new SceneRenderer(gd, vertexLayout);
        _skyboxRenderer = new SkyboxRenderer(gd, _sampler, vertexLayout);
        _resolveRenderer = new ResolveRenderer(gd);
        _blitRenderer = new BlitRenderer(gd);

        OnWindowResized();
    }

    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<GameObject> objectsToOutline, float windowWidth, float windowHeight)
    {
        if (mainCamera is null) return;

        Matrix4x4 view = mainCamera.GetViewMatrix();
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(windowWidth / windowHeight);

        CommandList cl = _graphicsManager.CommandList;
        cl.Begin();

        // Pass 1: Render scene to MSAA framebuffer
        cl.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f, 0);

        if (skybox is not null)
        {
            _skyboxRenderer.Render(cl, skybox, view, projection);
        }
        _sceneRenderer.Render(cl, view, projection, gameObjects);
        if (objectsToOutline is not null)
        {
            foreach (var obj in objectsToOutline)
            {
                _sceneRenderer.RenderOutline(cl, view, projection, obj);
            }
        }

        // Pass 2: Resolve MSAA to our final intermediate texture
        cl.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        _resolveRenderer.Render(cl, _graphicsManager.MsaaColorView);

        // Pass 3: Blit the final texture to the screen's swapchain
        cl.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        _blitRenderer.Render(cl, _graphicsManager.FinalColorView);

        if (_snapshotRequested && _snapshotter != null)
        {
            // Copy from our stable intermediate texture, not the swapchain
            _snapshotter.RecordCopyCommand(cl, _graphicsManager.FinalColorTarget);
            _snapshotRequested = false;
        }

        cl.End();
        _graphicsManager.GraphicsDevice.SubmitCommands(cl);
        _graphicsManager.GraphicsDevice.SwapBuffers();
    }

    public void OnWindowResized()
    {
        _sceneRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _skyboxRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _resolveRenderer.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        _blitRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
    }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _blitRenderer.Dispose();
        _sampler.Dispose();
    }

    /// <summary>
    /// A simple renderer to draw a texture to a fullscreen quad.
    /// </summary>
    private class BlitRenderer : IDisposable
    {
        private readonly DeviceBuffer _vertexBuffer;
        private Pipeline _pipeline;
        private readonly ResourceLayout _textureLayout;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Shader _vertexShader;
        private readonly Shader _fragmentShader;

        public BlitRenderer(GraphicsDevice gd)
        {
            _graphicsDevice = gd;
            ResourceFactory factory = gd.ResourceFactory;

            Vector3[] quadVertices =
            {
                new Vector3(-1.0f, -1.0f, 0.0f), new Vector3(1.0f, -1.0f, 0.0f),
                new Vector3(-1.0f, 1.0f, 0.0f), new Vector3(1.0f, 1.0f, 0.0f)
            };
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(float) * 3 * quadVertices.Length), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(_vertexBuffer, 0, quadVertices);

            var vertexLayout = new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            (_vertexShader, _fragmentShader) = LoadShaders(factory);
        }

        public void SetFramebuffer(Framebuffer targetFramebuffer)
        {
            _pipeline?.Dispose();
            ResourceFactory factory = _graphicsDevice.ResourceFactory;
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
                PrimitiveTopology = PrimitiveTopology.TriangleStrip,
                ResourceLayouts = new[] { _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, new[] { _vertexShader, _fragmentShader }),
                Outputs = targetFramebuffer.OutputDescription
            });
        }

        public void Render(CommandList cl, TextureView sourceView)
        {
            ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout, sourceView, _graphicsDevice.PointSampler));

            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, textureSet);
            cl.Draw(4, 1, 0, 0);

            textureSet.Dispose();
        }

        private (Shader, Shader) LoadShaders(ResourceFactory factory)
        {
            const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 0) out vec2 fsin_TexCoord;
                void main() {
                    gl_Position = vec4(Position.xy, 0, 1);
                    vec2 uv = (Position.xy + vec2(1.0, 1.0)) / 2.0;
                    uv.y = 1.0 - uv.y; // Flip Y-coordinate
                    fsin_TexCoord = uv;
                }";
            const string fragmentCode = @"
                #version 450
                layout(location = 0) in vec2 fsin_TexCoord;
                layout(location = 0) out vec4 fsout_Color;
                layout(set = 0, binding = 0) uniform texture2D SourceTexture;
                layout(set = 0, binding = 1) uniform sampler SourceSampler;
                void main() {
                    fsout_Color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord);
                }";

            Shader[] shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main"));
            return (shaders[0], shaders[1]);
        }

        public void Dispose()
        {
            _pipeline?.Dispose();
            _vertexShader.Dispose();
            _fragmentShader.Dispose();
            _textureLayout.Dispose();
            _vertexBuffer.Dispose();
        }
    }
}