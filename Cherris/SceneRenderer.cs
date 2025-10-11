using System.Collections.Generic;
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
                new ResourceLayoutElementDescription("MaterialProperties", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _mvpResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_mvpLayout, _mvpBuffer));

        (_vertexShader, _fragmentShader) = LoadShaders(factory);
        (_outlineVertexShader, _outlineFragmentShader) = LoadOutlineShaders(factory);
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _pipeline?.Dispose();
        _outlinePipeline?.Dispose();

        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                true, true, ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout, TextureLayout, MaterialLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _vertexShader, _fragmentShader }),
            Outputs = framebuffer.OutputDescription
        });

        _outlinePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _mvpLayout },
            ShaderSet = new ShaderSetDescription(new[] { _vertexLayout }, new[] { _outlineVertexShader, _outlineFragmentShader }),
            Outputs = framebuffer.OutputDescription
        });
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

    public void RenderOutline(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, GameObject gameObject)
    {
        var meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer is null) return;

        var modelMatrix = gameObject.Transform.GetModelMatrix();
        var scaleMatrix = Matrix4x4.CreateScale(1.05f); // Outline thickness
        Matrix4x4 mvp = scaleMatrix * modelMatrix * view * projection;

        commandList.UpdateBuffer(_mvpBuffer, 0, ref mvp);

        commandList.SetPipeline(_outlinePipeline);
        commandList.SetVertexBuffer(0, meshRenderer.VertexBuffer);
        commandList.SetIndexBuffer(meshRenderer.IndexBuffer, IndexFormat.UInt16);
        commandList.SetGraphicsResourceSet(0, _mvpResourceSet);
        commandList.DrawIndexed(meshRenderer.IndexCount, 1, 0, 0, 0);
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

        ShaderDescription vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

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
            ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main");

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