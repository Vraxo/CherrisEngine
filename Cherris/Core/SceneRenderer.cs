using Cherris.Components;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class SceneRenderer : IDisposable
{
    private readonly DeviceBuffer _mvpBuffer;
    private Pipeline _pipeline;
    private readonly ResourceSet _mvpResourceSet;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly VertexLayoutDescription _vertexLayout;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private readonly ResourceLayout _mvpLayout;

    // Outline rendering resources
    private readonly Shader _outlineVertexShader;
    private readonly Shader _outlineFragmentShader;
    private Pipeline _outlinePipeline;

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
                new ResourceLayoutElementDescription("MaterialProperties", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment))); // FIX: Visible to both shaders

        _mvpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_mvpLayout, _mvpBuffer));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);
        (_outlineVertexShader, _outlineFragmentShader) = LoadOutlineShaders(factory);
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _pipeline?.Dispose();
        _outlinePipeline?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        // Pipeline for standard objects: writes to the stencil buffer.
        var mainDepthStencilState = new DepthStencilStateDescription(
            depthTestEnabled: true,
            depthWriteEnabled: true,
            comparisonKind: ComparisonKind.LessEqual,
            stencilTestEnabled: true,
            stencilFront: new StencilBehaviorDescription(
                fail: StencilOperation.Keep,
                pass: StencilOperation.Replace,
                depthFail: StencilOperation.Keep,
                comparison: ComparisonKind.Always),
            stencilBack: new StencilBehaviorDescription(StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Keep, ComparisonKind.Always),
            stencilReadMask: 0xff,
            stencilWriteMask: 0xff,
            stencilReference: 1);

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = mainDepthStencilState,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout, TextureLayout, MaterialLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _vertexShader, _fragmentShader }),
            Outputs = framebuffer.OutputDescription
        });

        // Pipeline for outlines: tests against the stencil buffer.
        var outlineDepthStencilState = new DepthStencilStateDescription(
            depthTestEnabled: true,
            depthWriteEnabled: false,
            comparisonKind: ComparisonKind.LessEqual,
            stencilTestEnabled: true,
            stencilFront: new StencilBehaviorDescription(
                fail: StencilOperation.Keep,
                pass: StencilOperation.Keep,
                depthFail: StencilOperation.Keep,
                comparison: ComparisonKind.NotEqual),
            stencilBack: new StencilBehaviorDescription(StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Keep, ComparisonKind.Always),
            stencilReadMask: 0xff,
            stencilWriteMask: 0x00,
            stencilReference: 1);

        _outlinePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = outlineDepthStencilState,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _outlineVertexShader, _outlineFragmentShader }),
            Outputs = framebuffer.OutputDescription
        });
    }

    public void Render(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, IEnumerable<GameObject> scene, Func<MeshRenderer, object> backendDataProvider)
    {
        commandList.SetPipeline(_pipeline);
        foreach (var gameObject in scene)
        {
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer?.Mesh is null) continue; // Added null check for Mesh

            var backendData = backendDataProvider(meshRenderer) as dynamic;
            if (backendData is null) continue;

            Matrix4x4 mvp = gameObject.Transform.GetModelMatrix() * view * projection;
            commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

            commandList.SetVertexBuffer(0, backendData.VertexBuffer);
            commandList.SetIndexBuffer(backendData.IndexBuffer, IndexFormat.UInt16);
            commandList.SetGraphicsResourceSet(0, _mvpResourceSet);
            commandList.SetGraphicsResourceSet(1, backendData.TextureResourceSet);
            commandList.SetGraphicsResourceSet(2, backendData.MaterialResourceSet);
            commandList.DrawIndexed(backendData.IndexCount, 1, 0, 0, 0);
        }
    }

    public void RenderOutline(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, GameObject gameObject, dynamic backendData)
    {
        if (backendData is null) return;

        var transform = gameObject.Transform;
        const float outlineFactor = 1.05f;
        var outlineModelMatrix =
            Matrix4x4.CreateScale(transform.Scale * outlineFactor) *
            Matrix4x4.CreateFromQuaternion(transform.Rotation) *
            Matrix4x4.CreateTranslation(transform.Position);

        Matrix4x4 mvp = outlineModelMatrix * view * projection;
        commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

        commandList.SetPipeline(_outlinePipeline);
        commandList.SetVertexBuffer(0, backendData.VertexBuffer);
        commandList.SetIndexBuffer(backendData.IndexBuffer, IndexFormat.UInt16);
        commandList.SetGraphicsResourceSet(0, _mvpResourceSet);
        commandList.DrawIndexed(backendData.IndexCount, 1, 0, 0, 0);
    }

    private (Shader, Shader) LoadShaders(ResourceFactory factory)
    {
        const string vertexCode = @"
                #version 450
                layout(location = 0) in vec3 Position;
                layout(location = 1) in vec4 Color;
                layout(location = 2) in vec2 TexCoord;

                layout(set = 0, binding = 0) uniform MvpBuffer { mat4 mvp; };
                // The Tiling component is still needed by the vertex shader
                layout(set = 2, binding = 0) uniform MaterialProperties { vec4 TextureTiling; vec4 EmissiveColor; };

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
                
                // EmissiveColor is now read in the fragment shader
                layout(set = 2, binding = 0) uniform MaterialProperties { vec4 TextureTiling; vec4 EmissiveColor; };

                layout(location = 0) out vec4 fsout_Color;

                void main() 
                { 
                    vec4 baseColor = texture(sampler2D(SourceTexture, SourceSampler), fsin_TexCoord) * fsin_Color;
                    fsout_Color = vec4(baseColor.rgb + EmissiveColor.rgb, baseColor.a);
                }";

        ShaderDescription vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");

        Shader[] shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    private (Shader, Shader) LoadOutlineShaders(ResourceFactory factory)
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
                layout(location = 0) out vec4 fsout_Color;

                void main() 
                { 
                    fsout_Color = vec4(1.0, 0.8, 0.0, 1.0); // Yellow
                }";

        ShaderDescription vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");

        Shader[] shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _outlinePipeline?.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _outlineVertexShader.Dispose();
        _outlineFragmentShader.Dispose();
        TextureLayout.Dispose();
        MaterialLayout.Dispose();
        _mvpLayout.Dispose();
        _mvpResourceSet.Dispose();
        _mvpBuffer.Dispose();
    }
}