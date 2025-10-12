using System;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class BloomRenderer : IDisposable
{
    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _brightPassPipeline;
    private readonly ResourceLayout _textureLayout;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _brightPassFragmentShader;

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

        (_vertexShader, _brightPassFragmentShader) = LoadShaders(factory);
    }

    public void OnWindowResized(Framebuffer targetFramebuffer)
    {
        _brightPassPipeline?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        _brightPassPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ResourceLayouts = new[] { _textureLayout },
            ShaderSet = new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, new[] { _vertexShader, _brightPassFragmentShader }),
            Outputs = targetFramebuffer.OutputDescription
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

            const float threshold = 1.1; // Increased to prevent LDR textures from blooming

            void main() 
            {
                vec3 color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord).rgb;
                float brightness = max(color.r, max(color.g, color.b));
                
                // 'step' function returns 0.0 if brightness < threshold, and 1.0 otherwise.
                // This effectively blacks out any pixels that aren't bright enough.
                vec3 finalColor = color * step(threshold, brightness);
                
                fsout_Color = vec4(finalColor, 1.0);
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
        _brightPassPipeline?.Dispose();
        _vertexShader.Dispose();
        _brightPassFragmentShader.Dispose();
        _textureLayout.Dispose();
        _vertexBuffer.Dispose();
    }
}