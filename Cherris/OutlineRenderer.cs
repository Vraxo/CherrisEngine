using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class OutlineRenderer : IDisposable
{
    private const int MaxOutlineProfiles = 16;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PropertiesBufferData
    {
        public uint ProfileCount;
        private readonly uint _padding1, _padding2, _padding3;

        public fixed float Colors[MaxOutlineProfiles * 4];
        public fixed float Thicknesses[MaxOutlineProfiles * 4]; // Use float array for thickness, store in first component of each 'vec4'
    }

    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _pipeline;
    private readonly ResourceLayout _layout;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly DeviceBuffer _screenSizeBuffer;
    private readonly DeviceBuffer _propertiesBuffer;
    private ResourceSet _resourceSet;
    private readonly Sampler _clampSampler;

    public unsafe OutlineRenderer(GraphicsDevice gd)
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

        uint propertiesBufferSize;
        unsafe
        {
            propertiesBufferSize = (uint)sizeof(PropertiesBufferData);
        }
        _propertiesBuffer = factory.CreateBuffer(new BufferDescription(propertiesBufferSize, BufferUsage.UniformBuffer));

        _clampSampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinPoint_MagPoint_MipPoint,
            LodBias = 0,
            MinimumLod = 0,
            MaximumLod = 0,
            MaximumAnisotropy = 1
        });

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("IdTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ScreenSizeBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PropertiesBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
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
            _clampSampler,
            _screenSizeBuffer,
            _propertiesBuffer));
    }

    public unsafe void Render(CommandList cl, float width, float height, IReadOnlyList<OutlineProfile> activeProfiles)
    {
        var screenSize = new Vector4(1.0f / width, 1.0f / height, 0, 0);
        cl.UpdateBuffer(_screenSizeBuffer, 0, screenSize);

        var properties = new PropertiesBufferData();
        properties.ProfileCount = (uint)Math.Min(activeProfiles.Count, MaxOutlineProfiles);

        for (int i = 0; i < properties.ProfileCount; i++)
        {
            var profile = activeProfiles[i];

            int colorIndex = i * 4;
            properties.Colors[colorIndex + 0] = profile.Color.R;
            properties.Colors[colorIndex + 1] = profile.Color.G;
            properties.Colors[colorIndex + 2] = profile.Color.B;
            properties.Colors[colorIndex + 3] = profile.Color.A;

            int thicknessIndex = i * 4;
            properties.Thicknesses[thicknessIndex] = profile.Thickness;
        }
        cl.UpdateBuffer(_propertiesBuffer, 0, properties);

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

            layout(std140, set = 0, binding = 4) uniform PropertiesBuffer
            {
                uint ProfileCount;
                uint _padding1;
                uint _padding2;
                uint _padding3;
                vec4 Colors[16];
                vec4 Thicknesses[16]; // Thickness is in the .x component
            };

            void main() {
                float centerIdRaw = texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord).r;
                
                if (centerIdRaw > 0.0) {
                    fsout_Color = texture(sampler2D(SceneTexture, SourceSampler), fsin_TexCoord);
                    return;
                }

                float maxIdRaw = 0.0;
                maxIdRaw = max(maxIdRaw, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(0.0, TexelSize.y)).r);
                maxIdRaw = max(maxIdRaw, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(0.0, TexelSize.y)).r);
                maxIdRaw = max(maxIdRaw, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(TexelSize.x, 0.0)).r);
                maxIdRaw = max(maxIdRaw, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(TexelSize.x, 0.0)).r);

                if (maxIdRaw > 0.0) {
                    int profileIndex = int(round(maxIdRaw * 255.0)) - 1;

                    if (profileIndex >= 0 && profileIndex < ProfileCount) {
                        vec4 outlineColor = Colors[profileIndex];
                        float thickness = Thicknesses[profileIndex].x;

                        float h = TexelSize.x * thickness;
                        float v = TexelSize.y * thickness;

                        float outlineStrength = 0.0;
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(0, v)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(0, v)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(h, 0)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord - vec2(h, 0)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(h, v)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(-h, v)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(h, -v)).r > 0.0 ? 1.0 : 0.0);
                        outlineStrength = max(outlineStrength, texture(sampler2D(IdTexture, SourceSampler), fsin_TexCoord + vec2(-h, -v)).r > 0.0 ? 1.0 : 0.0);

                        vec4 sceneColor = texture(sampler2D(SceneTexture, SourceSampler), fsin_TexCoord);
                        fsout_Color = mix(sceneColor, outlineColor, outlineStrength);
                        return;
                    }
                }
                
                fsout_Color = texture(sampler2D(SceneTexture, SourceSampler), fsin_TexCoord);
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
        _propertiesBuffer.Dispose();
        _clampSampler.Dispose();
    }
}