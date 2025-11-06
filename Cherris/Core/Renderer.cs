using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

// This class now acts as the Veldrid implementation of IRenderer
public class Renderer : IRenderer
{
    private readonly VeldridGraphicsManager _graphicsManager;
    private readonly GraphicsDevice _graphicsDevice;
    private SceneRenderer _sceneRenderer;
    private SkyboxRenderer _skyboxRenderer;
    private ResolveRenderer _resolveRenderer;
    private FinalPassRenderer _finalPassRenderer;
    private BloomRenderer _bloomRenderer;
    private Sampler _sampler;
    private Snapshotter _snapshotter;
    private bool _snapshotRequested;
    private string _snapshotPath;

    // Veldrid-specific data associated with a MeshRenderer component
    internal class VeldridMeshRendererData : IDisposable
    {
        public readonly DeviceBuffer VertexBuffer;
        public readonly DeviceBuffer IndexBuffer;
        public readonly uint IndexCount;
        public readonly ResourceSet TextureResourceSet;
        public readonly DeviceBuffer MaterialPropertiesBuffer;
        public readonly ResourceSet MaterialResourceSet;

        public VeldridMeshRendererData(GraphicsDevice gd, MeshRenderer meshRenderer, ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler)
        {
            var material = meshRenderer.Material;
            var texture = (Texture)material.Texture.GetBackendHandle();
            ResourceFactory factory = gd.ResourceFactory;

            VertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(Vertex.SizeInBytes * meshRenderer.Mesh.Vertices.Length), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(VertexBuffer, 0, meshRenderer.Mesh.Vertices);

            IndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(ushort) * meshRenderer.Mesh.Indices.Length), BufferUsage.IndexBuffer));
            gd.UpdateBuffer(IndexBuffer, 0, meshRenderer.Mesh.Indices);
            IndexCount = (uint)meshRenderer.Mesh.Indices.Length;

            TextureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(textureLayout, texture.VeldridTextureView, sampler));

            MaterialPropertiesBuffer = factory.CreateBuffer(new BufferDescription(32, BufferUsage.UniformBuffer));
            var materialData = new Vector4[2];
            materialData[0] = new Vector4(material.TextureTiling.X, material.TextureTiling.Y, 0, 0);
            materialData[1] = new Vector4(material.EmissiveColor, 1.0f);
            gd.UpdateBuffer(MaterialPropertiesBuffer, 0, materialData);

            MaterialResourceSet = factory.CreateResourceSet(new ResourceSetDescription(materialLayout, MaterialPropertiesBuffer));
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
            TextureResourceSet.Dispose();
            MaterialPropertiesBuffer.Dispose();
            MaterialResourceSet.Dispose();
        }
    }

    public Renderer(VeldridGraphicsManager graphicsManager, GraphicsDevice graphicsDevice)
    {
        _graphicsManager = graphicsManager;
        _graphicsDevice = graphicsDevice;
        _snapshotter = new Snapshotter(_graphicsDevice);
        CreateResources();
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
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

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

        _sceneRenderer = new SceneRenderer(_graphicsDevice, vertexLayout);
        _skyboxRenderer = new SkyboxRenderer(_graphicsDevice, _sampler, vertexLayout);
        _resolveRenderer = new ResolveRenderer(_graphicsDevice);
        _finalPassRenderer = new FinalPassRenderer(_graphicsDevice);
        _bloomRenderer = new BloomRenderer(_graphicsDevice);

        OnWindowResized();
    }

    private VeldridMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
    {
        if (mr.BackendData is VeldridMeshRendererData data)
        {
            return data;
        }
        var newData = new VeldridMeshRendererData(_graphicsDevice, mr, _sceneRenderer.TextureLayout, _sceneRenderer.MaterialLayout, _sampler);
        mr.BackendData = newData;
        return newData;
    }

    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
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

        if (skybox is not null) _skyboxRenderer.Render(cl, skybox, view, projection);

        _sceneRenderer.Render(cl, view, projection, gameObjects, GetOrCreateBackendData);
        if (selectedObject?.GetComponent<MeshRenderer>() is not null)
        {
            _sceneRenderer.RenderOutline(cl, view, projection, selectedObject, GetOrCreateBackendData(selectedObject.GetComponent<MeshRenderer>()));
        }

        // Pass 2: Resolve MSAA
        cl.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        _resolveRenderer.Render(cl, _graphicsManager.MsaaColorView);

        // Pass 3: Bloom
        var bloomViewport = new Viewport(0, 0, _graphicsManager.BloomColorTarget.Width, _graphicsManager.BloomColorTarget.Height, 0, 1);
        cl.SetViewport(0, bloomViewport);
        cl.SetFramebuffer(_graphicsManager.BloomFramebuffer);
        _bloomRenderer.RenderBrightPass(cl, _graphicsManager.FinalColorView);
        _bloomRenderer.RenderBlur(cl, _graphicsManager.BloomColorView, _graphicsManager.BloomFramebuffer, _graphicsManager.BloomTempColorView, _graphicsManager.BloomTempFramebuffer);

        // Pass 4: Composite and draw to screen
        cl.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        _finalPassRenderer.Render(cl, _graphicsManager.FinalColorView, exposure);
        _finalPassRenderer.RenderBloom(cl, _graphicsManager.BloomColorView);

        if (_snapshotRequested && _snapshotter is not null)
        {
            _snapshotter.RecordCopyCommand(cl, _graphicsManager.FinalColorTarget);
            _snapshotRequested = false;
        }

        cl.End();
        _graphicsDevice.SubmitCommands(cl);
    }

    public void OnWindowResized()
    {
        _graphicsManager.Resize((int)_graphicsManager.SwapchainFramebuffer.Width, (int)_graphicsManager.SwapchainFramebuffer.Height);
        _sceneRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _skyboxRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _resolveRenderer.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        _finalPassRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        _bloomRenderer.OnWindowResized(_graphicsManager.BloomFramebuffer, _graphicsManager.BloomTempFramebuffer);
    }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _finalPassRenderer.Dispose();
        _bloomRenderer.Dispose();
        _sampler.Dispose();
        _snapshotter.Dispose();
        _graphicsManager.Dispose();
    }

    /// <summary>
    /// Renders a fullscreen texture, applying exposure and tonemapping.
    /// Also handles additive blending for bloom.
    /// </summary>
    private class FinalPassRenderer : IDisposable
    {
        private readonly DeviceBuffer _vertexBuffer;
        private Pipeline _pipeline;
        private Pipeline _bloomPipeline;
        private readonly ResourceLayout _textureLayout;
        private readonly ResourceLayout _paramsLayout;
        private readonly DeviceBuffer _paramsBuffer;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Shader _vertexShader;
        private readonly Shader _fragmentShader;
        private readonly Sampler _clampSampler;

        public FinalPassRenderer(GraphicsDevice gd)
        {
            _graphicsDevice = gd;
            ResourceFactory factory = gd.ResourceFactory;

            Vector3[] quadVertices =
            {
                new Vector3(-1.0f, -1.0f, 0.0f), new Vector3(1.0f, -1.0f, 0.0f),
                new Vector3(-1.0f, 1.0f, 0.0f), new Vector3(1.0f, 1.0f, 0.0f)
            };
            _vertexBuffer = factory.CreateBuffer(new Veldrid.BufferDescription((uint)(sizeof(float) * 3 * quadVertices.Length), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(_vertexBuffer, 0, quadVertices);

            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            _paramsLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            _paramsBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));

            _clampSampler = factory.CreateSampler(new SamplerDescription
            {
                AddressModeU = SamplerAddressMode.Clamp,
                AddressModeV = SamplerAddressMode.Clamp,
                AddressModeW = SamplerAddressMode.Clamp,
                Filter = SamplerFilter.MinLinear_MagLinear_MipPoint
            });

            (_vertexShader, _fragmentShader) = LoadShaders(factory);
        }

        public void SetFramebuffer(Framebuffer targetFramebuffer)
        {
            _pipeline?.Dispose();
            _bloomPipeline?.Dispose();
            ResourceFactory factory = _graphicsDevice.ResourceFactory;

            var shaderSet = new ShaderSetDescription(
                new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) },
                new[] { _vertexShader, _fragmentShader });

            // Standard pipeline for the main scene pass
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
                PrimitiveTopology = PrimitiveTopology.TriangleStrip,
                ResourceLayouts = new[] { _textureLayout, _paramsLayout },
                ShaderSet = shaderSet,
                Outputs = targetFramebuffer.OutputDescription
            });

            // Additive blend pipeline for the bloom pass
            _bloomPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAdditiveBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
                PrimitiveTopology = PrimitiveTopology.TriangleStrip,
                ResourceLayouts = new[] { _textureLayout, _paramsLayout },
                ShaderSet = shaderSet,
                Outputs = targetFramebuffer.OutputDescription
            });
        }

        public void Render(CommandList cl, TextureView sourceView, float exposure)
        {
            cl.UpdateBuffer(_paramsBuffer, 0, new Vector4(exposure, 1, 0, 0)); // Use Y=1 to signal main pass

            ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout, sourceView, _graphicsDevice.LinearSampler)); // Main scene can wrap
            ResourceSet paramsSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _paramsLayout, _paramsBuffer));

            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, textureSet);
            cl.SetGraphicsResourceSet(1, paramsSet);
            cl.Draw(4, 1, 0, 0);

            textureSet.Dispose();
            paramsSet.Dispose();
        }

        public void RenderBloom(CommandList cl, TextureView bloomView)
        {
            cl.UpdateBuffer(_paramsBuffer, 0, new Vector4(0, 0, 0, 0)); // Use Y=0 to signal bloom pass

            ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout, bloomView, _clampSampler)); // Bloom must clamp
            ResourceSet paramsSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _paramsLayout, _paramsBuffer));

            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetPipeline(_bloomPipeline);
            cl.SetGraphicsResourceSet(0, textureSet);
            cl.SetGraphicsResourceSet(1, paramsSet);
            cl.Draw(4, 1, 0, 0);

            textureSet.Dispose();
            paramsSet.Dispose();
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
                layout(set = 1, binding = 0) uniform Params { float Exposure; float IsMainPass; }; // IsMainPass is 1.0 or 0.0

                vec3 tonemap_reinhard(vec3 color) {
                    return color / (color + vec3(1.0));
                }

                void main() {
                    vec3 color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord).rgb;
                    
                    if (IsMainPass > 0.5) {
                        color *= Exposure;
                        color = tonemap_reinhard(color);
                    }

                    fsout_Color = vec4(color, 1.0);
                }";

            Shader[] shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main"));
            return (shaders[0], shaders[1]);
        }

        public void Dispose()
        {
            _pipeline?.Dispose();
            _bloomPipeline?.Dispose();
            _vertexShader.Dispose();
            _fragmentShader.Dispose();
            _textureLayout.Dispose();
            _paramsLayout.Dispose();
            _paramsBuffer.Dispose();
            _vertexBuffer.Dispose();
            _clampSampler.Dispose();
        }
    }
}