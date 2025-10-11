using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class SceneRenderer
{
    private readonly DeviceBuffer _mvpBuffer;
    private Pipeline _pipeline;
    private readonly ResourceSet _mvpResourceSet;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly VertexLayoutDescription _vertexLayout;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly ResourceLayout _mvpLayout;

    // ID Pass Resources
    private readonly Shader _idVertexShader;
    private readonly Shader _idFragmentShader;
    private Pipeline _idPipeline;
    private readonly ResourceLayout _idColorLayout;
    private readonly DeviceBuffer _idColorBuffer;
    private readonly ResourceSet _idColorResourceSet;
    private static readonly RgbaFloat IdColorNormal = RgbaFloat.Black;

    public ResourceLayout TextureLayout { get; }
    public ResourceLayout MaterialLayout { get; }

    public SceneRenderer(GraphicsDevice gd, VertexLayoutDescription vertexLayout)
    {
        _graphicsDevice = gd;
        _vertexLayout = vertexLayout;
        ResourceFactory factory = gd.ResourceFactory;

        _mvpBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

        _mvpLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MvpBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        TextureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        MaterialLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MaterialProperties", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _mvpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_mvpLayout, _mvpBuffer));

        // Create ID Pass resources
        _idColorLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("IdColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        _idColorBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        _idColorResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_idColorLayout, _idColorBuffer));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);
        (_idVertexShader, _idFragmentShader) = LoadIdShaders(factory);
    }

    public void SetFramebuffers(Framebuffer mainFramebuffer, Framebuffer idFramebuffer)
    {
        _pipeline?.Dispose();
        _idPipeline?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        var sw = Stopwatch.StartNew();

        // Pipeline for standard objects.
        var mainDepthStencilState = new DepthStencilStateDescription(
            depthTestEnabled: true,
            depthWriteEnabled: true,
            comparisonKind: ComparisonKind.LessEqual);

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = mainDepthStencilState,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout, TextureLayout, MaterialLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _vertexShader, _fragmentShader }),
            Outputs = mainFramebuffer.OutputDescription
        });

        // Pipeline for ID Pass.
        var idDepthStencilState = new DepthStencilStateDescription(
            depthTestEnabled: true,
            depthWriteEnabled: true,
            comparisonKind: ComparisonKind.LessEqual);

        _idPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = idDepthStencilState,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout, _idColorLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _idVertexShader, _idFragmentShader }),
            Outputs = idFramebuffer.OutputDescription
        });

        sw.Stop();
        Console.WriteLine($"[PROFILE] SceneRenderer pipelines created in {sw.ElapsedMilliseconds}ms");
    }

    public void Render(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, IEnumerable<GameObject> scene)
    {
        foreach (var gameObject in scene)
        {
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer is null) continue;

            Matrix4x4 mvp = gameObject.Transform.GetModelMatrix() * view * projection;
            commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

            meshRenderer.Render(commandList, _pipeline, _mvpResourceSet);
        }
    }

    public void RenderIdPass(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, IEnumerable<GameObject> scene, GameObject selectedObject, IReadOnlyList<OutlineProfile> activeProfiles)
    {
        commandList.SetPipeline(_idPipeline);

        // Cache the mapping from profile name to index to avoid repeated lookups
        var profileIndexMap = activeProfiles
            .Select((profile, index) => new { profile.Name, Index = index + 1 }) // +1 because 0 is reserved for 'no outline'
            .ToDictionary(item => item.Name, item => item.Index);

        int selectionIndex = profileIndexMap.TryGetValue("Selection", out var idx) ? idx : -1;

        foreach (var gameObject in scene)
        {
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer is null) continue;

            int finalIndex = 0; // Default to 0 (no outline)

            if (gameObject == selectedObject && selectionIndex != -1)
            {
                finalIndex = selectionIndex;
            }
            else
            {
                var outlineComponent = gameObject.GetComponent<PermanentOutline>();
                if (outlineComponent != null && profileIndexMap.TryGetValue(outlineComponent.ProfileName, out var permanentIndex))
                {
                    finalIndex = permanentIndex;
                }
            }

            var profile = activeProfiles.FirstOrDefault(p => profileIndexMap.GetValueOrDefault(p.Name, -1) == finalIndex);

            var idColor = new RgbaFloat(
                (float)finalIndex / 255.0f, // R channel stores the consistent ID for color lookup
                (profile != null && profile.Glow > 0.0f) ? 1.0f : 0.0f, // G channel is the full-strength mask for blurring
                0,
                1);
            commandList.UpdateBuffer(_idColorBuffer, 0, idColor);

            Matrix4x4 mvp = gameObject.Transform.GetModelMatrix() * view * projection;
            commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

            commandList.SetVertexBuffer(0, meshRenderer.VertexBuffer);
            commandList.SetIndexBuffer(meshRenderer.IndexBuffer, IndexFormat.UInt16);
            commandList.SetGraphicsResourceSet(0, _mvpResourceSet);
            commandList.SetGraphicsResourceSet(1, _idColorResourceSet);
            commandList.DrawIndexed(meshRenderer.IndexCount, 1, 0, 0, 0);
        }
    }


    private (Shader, Shader) LoadShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 1) in vec4 Color;
                layout(location = 2) in vec2 TexCoord;

                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };
                layout(set = 2, binding = 0) uniform MaterialProperties { vec4 TextureTiling; }; // Use vec4 for 16-byte alignment

                layout(location = 0) out vec4 fsin_Color;
                layout(location = 1) out vec2 fsin_TexCoord;

                void main() 
                { 
                    gl_Position = mvp * vec4(Position, 1); 
                    fsin_Color = Color; 
                    fsin_TexCoord = TexCoord * TextureTiling.xy;
                }";

        const string fragmentCode = @"
                #version 450
                layout(location = 0) in vec4 fsin_Color;
                layout(location = 1) in vec2 fsin_TexCoord;

                layout(set = 1, binding = 0) uniform texture2D SourceTexture;
                layout(set = 1, binding = 1) uniform sampler SourceSampler;

                layout(location = 0) out vec4 fsout_Color;

                void main() 
                { 
                    fsout_Color = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord) * fsin_Color;
                }";

        Shader[] shaders = ShaderHelper.LoadFromGlsl(factory, vertexCode, fragmentCode);
        return (shaders[0], shaders[1]);
    }

    private (Shader, Shader) LoadIdShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 1) in vec4 Color;
                layout(location = 2) in vec2 TexCoord;
                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };
                void main() 
                { 
                    gl_Position = mvp * vec4(Position, 1); 
                }";

        const string fragmentCode = @"
                #version 450
                layout(set = 1, binding = 0) uniform IdColorBuffer { vec4 IdColor; };
                layout(location = 0) out vec4 fsout_Color;
                void main() 
                { 
                    fsout_Color = IdColor;
                }";

        Shader[] shaders = ShaderHelper.LoadFromGlsl(factory, vertexCode, fragmentCode);
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _idPipeline?.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _idVertexShader.Dispose();
        _idFragmentShader.Dispose();
        TextureLayout.Dispose();
        MaterialLayout.Dispose();
        _mvpLayout.Dispose();
        _mvpResourceSet.Dispose();
        _mvpBuffer.Dispose();
        _idColorLayout.Dispose();
        _idColorResourceSet.Dispose();
        _idColorBuffer.Dispose();
    }
}