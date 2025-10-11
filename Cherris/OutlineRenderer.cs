using System;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class OutlineRenderer : IDisposable
{
    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _pipeline;
    private readonly ResourceLayout _layout;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly DeviceBuffer _screenSizeBuffer;
    private ResourceSet _resourceSet;

    public OutlineRenderer(GraphicsDevice gd)
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

        _screenSizeBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("IdTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ScreenSizeBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

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
            ResourceLayouts = new[] { _layout },
            ShaderSet = new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, new[] { _vertexShader, _fragmentShader }),
            Outputs = targetFramebuffer.OutputDescription
        });
    }

    public void CreateResources(TextureView sceneView, TextureView idView)
    {
        _resourceSet?.Dispose();
        _resourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            sceneView,
            idView,
            _graphicsDevice.PointSampler,
            _screenSizeBuffer));
    }

    public void Render(CommandList cl, float width, float height)
    {
        var screenSize = new Vector4(1.0f / width, 1.0f / height, 0, 0);
        cl.UpdateBuffer(_screenSizeBuffer, 0, screenSize);

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _resourceSet);
        cl.Draw(4, 1, 0, 0);
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

            layout(set = 0, binding = 0) uniform texture2D SceneTexture;
            layout(set = 0, binding = 1) uniform texture2D IdTexture;
            layout(set = 0, binding = 2) uniform sampler SourceSampler;
            layout(set = 0, binding = 3) uniform ScreenSizeBuffer { vec2 TexelSize; };

            void main() {
                vec3 centerId = texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord).rgb;
                
                // If the current pixel belongs to the outlined object, just draw the scene color.
                if (centerId.r > 0.5) {
                    fsout_Color = texture(sampler2D(SceneTexture, SourceSampler), fsin_TexCoord);
                    return;
                }

                // The current pixel is background. Check if it's adjacent to an outline-target pixel.
                float outlineStrength = 0.0;
                outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(0.0, TexelSize.y)).r);
                outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(0.0, TexelSize.y)).r);
                outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(TexelSize.x, 0.0)).r);
                outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(TexelSize.x, 0.0)).r);

                if (outlineStrength > 0.5) {
                    fsout_Color = vec4(1.0, 0.0, 0.0, 1.0); // Draw red outline
                } else {
                    fsout_Color = texture(sampler2D(SceneTexture, SourceSampler), fsin_TexCoord);
                }
            }";

        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main"));
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _layout.Dispose();
        _vertexBuffer.Dispose();
        _screenSizeBuffer.Dispose();
    }
}