using System;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class BloomRenderer : IDisposable
{
    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _brightPassPipeline;
    private Pipeline _blurPipeline;
    private readonly ResourceLayout _textureLayout;
    private readonly ResourceLayout _blurParamsLayout;
    private readonly DeviceBuffer _blurParamsBuffer;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _brightPassFragmentShader;
    private readonly Shader _blurFragmentShader;
    private readonly Sampler _clampSampler;

    public BloomRenderer(GraphicsDevice gd)
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

        _blurParamsLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BlurParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

        _blurParamsBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));

        _clampSampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipPoint
        });


        (_vertexShader, _brightPassFragmentShader, _blurFragmentShader) = LoadShaders(factory);
    }

    public void OnWindowResized(Framebuffer brightPassTarget, Framebuffer blurTarget)
    {
        _brightPassPipeline?.Dispose();
        _blurPipeline?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        var quadLayout = new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        _brightPassPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _textureLayout },
            ShaderSet = new ShaderSetDescription(new[] { quadLayout }, new[] { _vertexShader, _brightPassFragmentShader }),
            Outputs = brightPassTarget.OutputDescription
        });

        _blurPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _textureLayout, _blurParamsLayout },
            ShaderSet = new ShaderSetDescription(new[] { quadLayout }, new[] { _vertexShader, _blurFragmentShader }),
            Outputs = blurTarget.OutputDescription
        });
    }

    public void RenderBrightPass(CommandList cl, TextureView sourceView)
    {
        ResourceSet textureSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout, sourceView, _graphicsDevice.LinearSampler));

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetPipeline(_brightPassPipeline);
        cl.SetGraphicsResourceSet(0, textureSet);
        cl.Draw(4, 1, 0, 0);

        textureSet.Dispose();
    }

    public void RenderBlur(CommandList cl,
        TextureView sourceView, Framebuffer sourceFramebuffer,
        TextureView tempView, Framebuffer tempFramebuffer)
    {
        // Use ClampSampler for blurring to prevent edge wrapping artifacts
        ResourceSet sourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout, sourceView, _clampSampler));
        ResourceSet tempSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout, tempView, _clampSampler));

        ResourceSet blurParamsSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _blurParamsLayout, _blurParamsBuffer));

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetPipeline(_blurPipeline);
        cl.SetGraphicsResourceSet(1, blurParamsSet);

        // Pass 1: Horizontal Blur
        // Source: sourceView, Target: tempFramebuffer
        cl.UpdateBuffer(_blurParamsBuffer, 0, new Vector4(1.0f / sourceView.Target.Width, 0, 0, 0));
        cl.SetFramebuffer(tempFramebuffer);
        cl.SetGraphicsResourceSet(0, sourceSet);
        cl.Draw(4, 1, 0, 0);

        // Pass 2: Vertical Blur
        // Source: tempView, Target: sourceFramebuffer
        cl.UpdateBuffer(_blurParamsBuffer, 0, new Vector4(0, 1.0f / sourceView.Target.Height, 0, 0));
        cl.SetFramebuffer(sourceFramebuffer);
        cl.SetGraphicsResourceSet(0, tempSet);
        cl.Draw(4, 1, 0, 0);

        sourceSet.Dispose();
        tempSet.Dispose();
        blurParamsSet.Dispose();
    }

    private (Shader, Shader, Shader) LoadShaders(ResourceFactory factory)
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

        const string brightPassFragmentCode = @"
            #version 450
            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;
            layout(set = 0, binding = 0) uniform texture2D SourceTexture;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;
            const float threshold = 1.1;
            void main() {
                vec3 color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord).rgb;
                float brightness = max(color.r, max(color.g, color.b));
                vec3 finalColor = color * step(threshold, brightness);
                fsout_Color = vec4(finalColor, 1.0);
            }";

        const string blurFragmentCode = @"
            #version 450
            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;
            layout(set = 0, binding = 0) uniform texture2D SourceTexture;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;
            layout(set = 1, binding = 0) uniform BlurParams { vec2 TexelSize; };

            // 5-tap Gaussian blur
            const float weights[3] = float[](0.227027, 0.316216, 0.070270);
            const float offsets[3] = float[](0.0, 1.384615, 3.230769);

            void main() {
                vec3 result = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord).rgb * weights[0];
                for (int i = 1; i < 3; i++) {
                    result += texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord + offsets[i] * TexelSize).rgb * weights[i];
                    result += texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord - offsets[i] * TexelSize).rgb * weights[i];
                }
                fsout_Color = vec4(result, 1.0);
            }";

        var vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");

        var brightPassFragmentDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(brightPassFragmentCode), "main");
        var blurFragmentDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(blurFragmentCode), "main");

        Shader[] brightPassShaders = factory.CreateFromSpirv(vertexShaderDesc, brightPassFragmentDesc);
        Shader[] blurShaders = factory.CreateFromSpirv(vertexShaderDesc, blurFragmentDesc);

        return (brightPassShaders[0], brightPassShaders[1], blurShaders[1]);
    }

    public void Dispose()
    {
        _brightPassPipeline?.Dispose();
        _blurPipeline?.Dispose();
        _vertexShader.Dispose();
        _brightPassFragmentShader.Dispose();
        _blurFragmentShader.Dispose();
        _textureLayout.Dispose();
        _blurParamsLayout.Dispose();
        _blurParamsBuffer.Dispose();
        _vertexBuffer.Dispose();
        _clampSampler.Dispose();
    }
}