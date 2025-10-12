using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

// Struct to hold bloom parameters, will be expanded later.
[StructLayout(LayoutKind.Sequential)]
public struct BloomParameters
{
    public float Threshold;
    private float _padding1;
    private float _padding2;
    private float _padding3;
}

public class BloomRenderer : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private Pipeline _brightPassPipeline;
    private readonly ResourceLayout _textureLayout;
    private readonly ResourceLayout _paramsLayout;
    private readonly DeviceBuffer _vertexBuffer;
    private readonly DeviceBuffer _paramsBuffer;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;

    // We will create our own render target for the bright pass result.
    public Veldrid.Texture BrightPassTarget { get; private set; }
    public TextureView BrightPassTargetView { get; private set; }
    public Framebuffer BrightPassFramebuffer { get; private set; }

    public BloomRenderer(GraphicsDevice gd, uint width, uint height, PixelFormat format)
    {
        _graphicsDevice = gd;
        ResourceFactory factory = gd.ResourceFactory;

        // Create the render target for the bright pass
        BrightPassTarget = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, format, TextureUsage.RenderTarget | TextureUsage.Sampled));
        BrightPassTargetView = factory.CreateTextureView(BrightPassTarget);
        BrightPassFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, BrightPassTarget));

        // Fullscreen quad setup
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

        _paramsLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BloomParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

        _paramsBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<BloomParameters>(), BufferUsage.UniformBuffer));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);

        CreatePipelines();
    }

    private void CreatePipelines()
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        _brightPassPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _textureLayout, _paramsLayout },
            ShaderSet = new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, new[] { _vertexShader, _fragmentShader }),
            Outputs = BrightPassFramebuffer.OutputDescription
        });
    }

    public void RenderBrightPass(CommandList cl, TextureView sourceView, float threshold)
    {
        // Update parameters
        var parameters = new BloomParameters { Threshold = threshold };
        cl.UpdateBuffer(_paramsBuffer, 0, parameters);

        // Create resource set for the source texture
        ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout, sourceView, _graphicsDevice.PointSampler));

        // Create resource set for parameters
        ResourceSet paramsSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _paramsLayout, _paramsBuffer));

        cl.SetFramebuffer(BrightPassFramebuffer);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetPipeline(_brightPassPipeline);
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
                uv.y = 1.0 - uv.y; // Flip Y for consistency with BlitRenderer
                fsin_TexCoord = uv;
            }";
        const string fragmentCode = @"
            #version 450
            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;

            layout(set = 0, binding = 0) uniform texture2D SourceTexture;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;

            layout(set = 1, binding = 0) uniform BloomParams
            {
                float Threshold;
            } Params;

            // NTSC weights for calculating luminance
            const vec3 LUMINANCE_VECTOR = vec3(0.299, 0.587, 0.114);

            void main() {
                vec4 color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord);
                float brightness = dot(color.rgb, LUMINANCE_VECTOR);
                if (brightness > Params.Threshold)
                {
                    fsout_Color = color;
                }
                else
                {
                    fsout_Color = vec4(0.0, 0.0, 0.0, 1.0);
                }
            }";

        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main"));
        return (shaders[0], shaders[1]);
    }

    public void OnWindowResized(uint width, uint height)
    {
        BrightPassTarget.Dispose();
        BrightPassTargetView.Dispose();
        BrightPassFramebuffer.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        BrightPassTarget = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, BrightPassTarget.Format, TextureUsage.RenderTarget | TextureUsage.Sampled));
        BrightPassTargetView = factory.CreateTextureView(BrightPassTarget);
        BrightPassFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, BrightPassTarget));
    }

    public void Dispose()
    {
        _brightPassPipeline?.Dispose();
        _textureLayout.Dispose();
        _paramsLayout.Dispose();
        _vertexBuffer.Dispose();
        _paramsBuffer.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        BrightPassTarget.Dispose();
        BrightPassTargetView.Dispose();
        BrightPassFramebuffer.Dispose();
    }
}