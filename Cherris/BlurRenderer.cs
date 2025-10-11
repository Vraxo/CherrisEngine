using System.Diagnostics;
using System.Numerics;
using System.Text;
using Veldrid;

namespace Cherris;

public class BlurRenderer : IDisposable
{
    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _pipeline;
    private readonly ResourceLayout _layout;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly DeviceBuffer _blurPropertiesBuffer;
    private ResourceSet _resourceSet;
    private readonly Sampler _sampler;

    public BlurRenderer(GraphicsDevice gd)
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

        _blurPropertiesBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));

        _sampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipPoint
        });

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BlurProperties", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);
    }

    public void SetFramebuffer(Framebuffer targetFramebuffer)
    {
        _pipeline?.Dispose();
        var sw = Stopwatch.StartNew();
        _pipeline = _graphicsDevice.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _layout },
            ShaderSet = new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, new[] { _vertexShader, _fragmentShader }),
            Outputs = targetFramebuffer.OutputDescription
        });
        sw.Stop();
        Console.WriteLine($"[PROFILE] BlurRenderer pipeline created in {sw.ElapsedMilliseconds}ms");
    }

    public void CreateResources(TextureView sourceView)
    {
        _resourceSet?.Dispose();
        _resourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            sourceView,
            _sampler,
            _blurPropertiesBuffer));
    }

    public void Render(CommandList cl, Vector2 direction)
    {
        cl.UpdateBuffer(_blurPropertiesBuffer, 0, direction);
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
                vec2 uv = (Position.xy + 1.0) / 2.0;
                uv.y = 1.0 - uv.y; // Flip Y-coordinate
                fsin_TexCoord = uv;
            }";

        const string fragmentCode = @"
            #version 450
            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;

            layout(set = 0, binding = 0) uniform texture2D SourceTexture;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;
            layout(set = 0, binding = 2) uniform BlurProperties { vec2 BlurDirection; };

            // Gaussian weights for a 9-tap blur
            const float weights[5] = float[](0.227027, 0.1945946, 0.1216216, 0.05405405, 0.016216216);

            void main() {
                vec2 texelSize = 1.0 / textureSize(sampler2D(SourceTexture, SourceSampler), 0);
                // Sample the GREEN channel for the glow mask
                float result = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord).g * weights[0];

                for (int i = 1; i < 5; ++i) {
                    result += texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord + vec2(texelSize.x * i, 0.0) * BlurDirection).g * weights[i];
                    result += texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord - vec2(texelSize.x * i, 0.0) * BlurDirection).g * weights[i];
                }

                fsout_Color = vec4(result, result, result, 1.0);
            }";

        Shader[] shaders = ShaderHelper.LoadFromGlsl(factory, vertexCode, fragmentCode);
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
        _blurPropertiesBuffer.Dispose();
        _sampler.Dispose();
    }
}