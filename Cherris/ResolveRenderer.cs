using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class ResolveRenderer
{
    private readonly DeviceBuffer _vertexBuffer;
    private readonly Pipeline _pipeline;
    private readonly ResourceLayout _textureLayout;
    private readonly GraphicsDevice _graphicsDevice;

    public ResolveRenderer(GraphicsDevice gd, Framebuffer targetFramebuffer)
    {
        _graphicsDevice = gd;
        ResourceFactory factory = gd.ResourceFactory;

        // Fullscreen quad vertices (positions only)
        Vector3[] quadVertices =
        {
            new Vector3(-1.0f, -1.0f, 0.0f),
            new Vector3(-1.0f, 1.0f, 0.0f),
            new Vector3(1.0f, -1.0f, 0.0f),
            new Vector3(1.0f, 1.0f, 0.0f)
        };

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(sizeof(float) * 3 * quadVertices.Length),
            BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_vertexBuffer, 0, quadVertices);

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        _textureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MsaaTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

        (Shader vs, Shader fs) = LoadShaders(factory);

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _textureLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
            Outputs = targetFramebuffer.OutputDescription
        });
    }

    public void Render(CommandList commandList, TextureView msaaColorView)
    {
        ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout,
            msaaColorView));

        commandList.SetVertexBuffer(0, _vertexBuffer);
        commandList.SetPipeline(_pipeline);
        commandList.SetGraphicsResourceSet(0, textureSet);
        commandList.Draw(4, 1, 0, 0);

        textureSet.Dispose(); // Dispose the set after use since it's created per call
    }

    private (Shader, Shader) LoadShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
#version 450
layout(location = 0) in vec3 Position;

void main()
{
    gl_Position = vec4(Position.xy, 0, 1);
}";

        const string fragmentCode = @"
#version 450
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2DMS MsaaTexture;

void main()
{
    vec4 color = vec4(0);
    ivec2 coord = ivec2(gl_FragCoord.xy);
    for(int s = 0; s < 4; s++)
    {
        color += texelFetch(MsaaTexture, coord, s);
    }
    fsout_Color = color / 4.0;
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
        _textureLayout.Dispose();
        _vertexBuffer.Dispose();
    }
}