using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class SkyboxRenderer
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Sampler _sampler;
    private readonly DeviceBuffer _skyboxVpBuffer;
    private Pipeline _skyboxPipeline;
    private readonly ResourceSet _skyboxVpResourceSet;
    private readonly ResourceLayout _skyboxTextureLayout;
    private readonly DeviceBuffer _skyboxVertexBuffer;
    private readonly DeviceBuffer _skyboxIndexBuffer;
    private readonly uint _skyboxIndexCount;
    private readonly Dictionary<Texture, ResourceSet> _skyboxTextureSets = new();
    private readonly VertexLayoutDescription _vertexLayout;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly ResourceLayout _skyboxVpLayout;

    public SkyboxRenderer(GraphicsDevice gd, Sampler sampler, VertexLayoutDescription vertexLayout)
    {
        _graphicsDevice = gd;
        _sampler = sampler;
        _vertexLayout = vertexLayout;
        ResourceFactory factory = gd.ResourceFactory;

        var skyboxMesh = Mesh.CreateCube();
        _skyboxVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(Vertex.SizeInBytes * skyboxMesh.Vertices.Length), BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_skyboxVertexBuffer, 0, skyboxMesh.Vertices);

        _skyboxIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(ushort) * skyboxMesh.Indices.Length), BufferUsage.IndexBuffer));
        gd.UpdateBuffer(_skyboxIndexBuffer, 0, skyboxMesh.Indices);
        _skyboxIndexCount = (uint)skyboxMesh.Indices.Length;

        _skyboxVpBuffer = factory.CreateBuffer(new BufferDescription(128, BufferUsage.UniformBuffer));

        _skyboxVpLayout = factory.CreateResourceLayout(
            new(
                new ResourceLayoutElementDescription("ViewProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _skyboxTextureLayout = factory.CreateResourceLayout(
            new(
                new("SourceCubeMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _skyboxVpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_skyboxVpLayout, _skyboxVpBuffer));

        (_vertexShader, _fragmentShader) = LoadSkyboxShaders(factory);
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _skyboxPipeline?.Dispose();
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        _skyboxPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new(true, false, ComparisonKind.LessEqual),
            RasterizerState = new(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [_skyboxVpLayout, _skyboxTextureLayout],
            ShaderSet = new([_vertexLayout], [_vertexShader, _fragmentShader]),
            Outputs = framebuffer.OutputDescription
        });
    }

    public void Render(CommandList commandList, Skybox skybox, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 skyboxView = view;
        skyboxView.Translation = Vector3.Zero;

        commandList.UpdateBuffer(_skyboxVpBuffer, 0, ref skyboxView);
        commandList.UpdateBuffer(_skyboxVpBuffer, 64, ref projection);

        if (!_skyboxTextureSets.TryGetValue(skybox.CubeMapTexture, out var textureSet))
        {
            textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new(
                _skyboxTextureLayout,
                skybox.CubeMapTexture.VeldridTextureView,
                _sampler));

            _skyboxTextureSets.Add(skybox.CubeMapTexture, textureSet);
        }

        commandList.SetVertexBuffer(0, _skyboxVertexBuffer);
        commandList.SetIndexBuffer(_skyboxIndexBuffer, IndexFormat.UInt16);
        commandList.SetPipeline(_skyboxPipeline);
        commandList.SetGraphicsResourceSet(0, _skyboxVpResourceSet);
        commandList.SetGraphicsResourceSet(1, textureSet);
        commandList.DrawIndexed(_skyboxIndexCount, 1, 0, 0, 0);
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

        ShaderDescription vertexShaderDesc = new(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(vertexCode),
            "main");

        ShaderDescription fragmentShaderDesc = new(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(fragmentCode), 
            "main");

        Shader[] shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        _skyboxPipeline?.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _skyboxTextureLayout.Dispose();
        _skyboxVpLayout.Dispose();
        _skyboxVpResourceSet.Dispose();
        _skyboxVpBuffer.Dispose();
        _skyboxVertexBuffer.Dispose();
        _skyboxIndexBuffer.Dispose();

        foreach (ResourceSet set in _skyboxTextureSets.Values)
        {
            set.Dispose();
        }
    }
}