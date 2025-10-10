using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class Renderer
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly DeviceBuffer _mvpBuffer;
    private readonly Pipeline _pipeline;
    private readonly ResourceSet _mvpResourceSet;

    // Skybox resources
    private DeviceBuffer _skyboxVpBuffer;
    private Pipeline _skyboxPipeline;
    private ResourceSet _skyboxVpResourceSet;
    private ResourceLayout _skyboxTextureLayout;
    private DeviceBuffer _skyboxVertexBuffer;
    private DeviceBuffer _skyboxIndexBuffer;
    private uint _skyboxIndexCount;
    private readonly Dictionary<Texture, ResourceSet> _skyboxTextureSets = new();


    public ResourceLayout TextureLayout { get; }
    public ResourceLayout MaterialLayout { get; }
    public Sampler Sampler { get; }


    public Renderer(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
        ResourceFactory factory = gd.ResourceFactory;

        _mvpBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        ResourceLayout mvpLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MvpBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        TextureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        MaterialLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MaterialProperties", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        Sampler = factory.CreateSampler(new SamplerDescription
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
            ResourceLayouts = new[] { mvpLayout, TextureLayout, MaterialLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
            Outputs = gd.SwapchainFramebuffer.OutputDescription
        });

        CreateSkyboxResources(gd, vertexLayout);
    }

    private void CreateSkyboxResources(GraphicsDevice gd, VertexLayoutDescription vertexLayout)
    {
        ResourceFactory factory = gd.ResourceFactory;

        var skyboxMesh = Mesh.CreateCube();
        _skyboxVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(Vertex.SizeInBytes * skyboxMesh.Vertices.Length), BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_skyboxVertexBuffer, 0, skyboxMesh.Vertices);

        _skyboxIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(ushort) * skyboxMesh.Indices.Length), BufferUsage.IndexBuffer));
        gd.UpdateBuffer(_skyboxIndexBuffer, 0, skyboxMesh.Indices);
        _skyboxIndexCount = (uint)skyboxMesh.Indices.Length;

        _skyboxVpBuffer = factory.CreateBuffer(new BufferDescription(128, BufferUsage.UniformBuffer));

        ResourceLayout skyboxVpLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ViewProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _skyboxTextureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceCubeMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _skyboxVpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(skyboxVpLayout, _skyboxVpBuffer));

        (Shader vs, Shader fs) = LoadSkyboxShaders(factory);

        _skyboxPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { skyboxVpLayout, _skyboxTextureLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
            Outputs = gd.SwapchainFramebuffer.OutputDescription
        });
    }

    public void RenderSkybox(CommandList commandList, Skybox skybox, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 skyboxView = view;
        skyboxView.Translation = Vector3.Zero;

        commandList.UpdateBuffer(_skyboxVpBuffer, 0, ref skyboxView);
        commandList.UpdateBuffer(_skyboxVpBuffer, 64, ref projection);

        if (!_skyboxTextureSets.TryGetValue(skybox.CubeMapTexture, out var textureSet))
        {
            textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _skyboxTextureLayout,
                skybox.CubeMapTexture.VeldridTextureView,
                Sampler));
            _skyboxTextureSets.Add(skybox.CubeMapTexture, textureSet);
        }

        commandList.SetVertexBuffer(0, _skyboxVertexBuffer);
        commandList.SetIndexBuffer(_skyboxIndexBuffer, IndexFormat.UInt16);
        commandList.SetPipeline(_skyboxPipeline);
        commandList.SetGraphicsResourceSet(0, _skyboxVpResourceSet);
        commandList.SetGraphicsResourceSet(1, textureSet);
        commandList.DrawIndexed(_skyboxIndexCount, 1, 0, 0, 0);
    }

    public void RenderScene(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, IEnumerable<GameObject> scene)
    {
        foreach (var gameObject in scene)
        {
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) continue;

            Matrix4x4 mvp = gameObject.Transform.GetModelMatrix() * view * projection;
            commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

            meshRenderer.Render(commandList, _pipeline, _mvpResourceSet);
        }
    }

    private (Shader, Shader) LoadShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 1) in vec4 Color;
                layout(location = 2) in vec2 TexCoord;

                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };
                layout(set = 2, binding = 0) uniform MaterialProperties { vec4 TextureTiling; }; // Use vec4 for 16-byte alignment

                layout(location = 0) out vec4 fsin_Color;
                layout(location = 1) out vec2 fsin_TexCoord;

                void main() 
                { 
                    gl_Position = mvp * vec4(Position, 1); 
                    fsin_Color = Color; 
                    fsin_TexCoord = TexCoord * TextureTiling.xy;
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

    private (Shader, Shader) LoadSkyboxShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
            #version 450
            layout(location = 0) in vec3 Position;

            layout(set = 0, binding = 0) uniform ViewProjectionBuffer 
            { 
                mat4 View; 
                mat4 Projection; 
            };

            layout(location = 0) out vec3 fsin_TexCoord;

            void main()
            {
                fsin_TexCoord = Position;
                vec4 pos = Projection * View * vec4(Position, 1.0);
                gl_Position = pos.xyww;
            }";

        const string fragmentCode = @"
            #version 450
            layout(location = 0) in vec3 fsin_TexCoord;

            layout(set = 1, binding = 0) uniform textureCube SourceCubeMap;
            layout(set = 1, binding = 1) uniform sampler SourceSampler;

            layout(location = 0) out vec4 fsout_Color;

            void main()
            {
                fsout_Color = texture(samplerCube(SourceCubeMap, SourceSampler), fsin_TexCoord);
            }";

        ShaderDescription vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

        Shader[] shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        TextureLayout.Dispose();
        MaterialLayout.Dispose();
        Sampler.Dispose();
        _mvpResourceSet.Dispose();
        _mvpBuffer.Dispose();

        _skyboxPipeline.Dispose();
        _skyboxTextureLayout.Dispose();
        _skyboxVpResourceSet.Dispose();
        _skyboxVpBuffer.Dispose();
        _skyboxVertexBuffer.Dispose();
        _skyboxIndexBuffer.Dispose();
        foreach (var set in _skyboxTextureSets.Values)
        {
            set.Dispose();
        }
    }
}