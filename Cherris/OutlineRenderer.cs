using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public fixed float Thicknesses[MaxOutlineProfiles * 4]; // No longer used for glow, but kept for standard outlines
        public fixed float Glows[MaxOutlineProfiles * 4]; // Glow intensity
    }

    private readonly DeviceBuffer _vertexBuffer;
    private Pipeline _pipeline;
    private readonly ResourceLayout _layout;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly DeviceBuffer _propertiesBuffer;
    private ResourceSet _resourceSet;
    private readonly Sampler _clampSampler;
    private readonly Sampler _linearSampler;

    public unsafe OutlineRenderer(GraphicsDevice gd)
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

        uint propertiesBufferSize;
        unsafe
        {
            propertiesBufferSize = (uint)sizeof(PropertiesBufferData);
        }
        _propertiesBuffer = factory.CreateBuffer(new Veldrid.BufferDescription(propertiesBufferSize, BufferUsage.UniformBuffer));

        _clampSampler = factory.CreateSampler(new Veldrid.SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinPoint_MagPoint_MipPoint,
        });

        _linearSampler = factory.CreateSampler(new Veldrid.SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipPoint,
        });

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("IdTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("GlowTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PointSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LinearSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PropertiesBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);
    }

    public void SetFramebuffer(Framebuffer targetFramebuffer)
    {
        _pipeline?.Dispose();
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        var sw = Stopwatch.StartNew();
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
        sw.Stop();
        Console.WriteLine($"[PROFILE] OutlineRenderer pipeline created in {sw.ElapsedMilliseconds}ms");
    }

    public void CreateResources(TextureView sceneView, TextureView idView, TextureView glowView)
    {
        _resourceSet?.Dispose();
        _resourceSet = _graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            sceneView,
            idView,
            glowView,
            _clampSampler,
            _linearSampler,
            _propertiesBuffer));
    }

    public unsafe void Render(CommandList cl, float width, float height, IReadOnlyList<OutlineProfile> activeProfiles)
    {
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

            int glowIndex = i * 4;
            properties.Glows[glowIndex] = profile.Glow;
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
                vec2 uv = (Position.xy + 1.0) / 2.0;
                uv.y = 1.0 - uv.y; // Flip Y-coordinate
                fsin_TexCoord = uv;
            }";

        const string fragmentCode = @"
            #version 450
            layout(location = 0) in vec2 fsin_TexCoord;
            layout(location = 0) out vec4 fsout_Color;

            layout(set = 0, binding = 0) uniform texture2D SceneTexture;
            layout(set = 0, binding = 1) uniform texture2D IdTexture;
            layout(set = 0, binding = 2) uniform texture2D GlowTexture;
            layout(set = 0, binding = 3) uniform sampler PointSampler;
            layout(set = 0, binding = 4) uniform sampler LinearSampler;

            layout(std140, set = 0, binding = 5) uniform PropertiesBuffer
            {
                uint ProfileCount;
                uint _padding1;
                uint _padding2;
                uint _padding3;
                vec4 Colors[16];
                vec4 Thicknesses[16];
                vec4 Glows[16];
            };

            void main() {
                vec4 sceneColor = texture(sampler2D(SceneTexture, LinearSampler), fsin_TexCoord);
                float glowAmount = texture(sampler2D(GlowTexture, LinearSampler), fsin_TexCoord).r;
                
                if (glowAmount > 0.001) {
                    // Find the ID of the nearest object to determine color.
                    // This is a small search on the UN-BLURRED ID texture's RED channel.
                    float nearestIdRaw = 0.0f;
                    float minSqDist = 10000.0f;
                    vec2 texelSize = 1.0 / textureSize(sampler2D(IdTexture, PointSampler), 0);
                    
                    // A small search radius is enough to find the object from its glow halo
                    const int searchRadius = 5;
                    for (int y = -searchRadius; y <= searchRadius; y++) {
                        for (int x = -searchRadius; x <= searchRadius; x++) {
                            vec2 offset = vec2(x, y) * texelSize;
                            // Sample the RED channel for the profile ID
                            float currentId = texture(sampler2D(IdTexture, PointSampler), fsin_TexCoord + offset).r;
                            if (currentId > 0.0) {
                                float sqDist = float(x*x + y*y);
                                if (sqDist < minSqDist) {
                                    minSqDist = sqDist;
                                    nearestIdRaw = currentId;
                                }
                            }
                        }
                    }

                    if (nearestIdRaw > 0.0) {
                        int profileIndex = int(round(nearestIdRaw * 255.0)) - 1;
                        if (profileIndex >= 0 && profileIndex < ProfileCount) {
                            float glowIntensity = Glows[profileIndex].x;
                            if (glowIntensity > 0.0) {
                                 // Additive blend: Use profile color, multiplied by the blurred falloff and intensity
                                sceneColor += Colors[profileIndex] * glowAmount * glowIntensity;
                            }
                        }
                    }
                }
                
                fsout_Color = sceneColor;
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
        _propertiesBuffer.Dispose();
        _clampSampler.Dispose();
        _linearSampler.Dispose();
    }
}