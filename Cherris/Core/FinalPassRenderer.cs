using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public partial class VeldridRenderer
{
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