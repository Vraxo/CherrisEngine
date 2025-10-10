using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace Cherris;

public class Renderer
{
    private readonly SceneRenderer _sceneRenderer;
    private readonly SkyboxRenderer _skyboxRenderer;
    private readonly ResolveRenderer _resolveRenderer;
    private readonly Sampler _sampler;

    public ResourceLayout TextureLayout => _sceneRenderer.TextureLayout;
    public ResourceLayout MaterialLayout => _sceneRenderer.MaterialLayout;
    public Sampler Sampler => _sampler;

    public Renderer(GraphicsDevice gd, Framebuffer msaaFramebuffer, Framebuffer swapchainFramebuffer)
    {
        ResourceFactory factory = gd.ResourceFactory;

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _sampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Wrap,
            AddressModeV = SamplerAddressMode.Wrap,
            AddressModeW = SamplerAddressMode.Wrap,
            Filter = SamplerFilter.Anisotropic,
            MaximumAnisotropy = 16,
            LodBias = 0,
            MinimumLod = 0,
            MaximumLod = uint.MaxValue
        });

        _sceneRenderer = new SceneRenderer(gd, vertexLayout, msaaFramebuffer, _sampler);
        _skyboxRenderer = new SkyboxRenderer(gd, _sampler, vertexLayout, msaaFramebuffer);
        _resolveRenderer = new ResolveRenderer(gd, swapchainFramebuffer);
    }

    public void RenderSkybox(CommandList commandList, Skybox skybox, Matrix4x4 view, Matrix4x4 projection)
    {
        _skyboxRenderer.Render(commandList, skybox, view, projection);
    }

    public void RenderScene(CommandList commandList, Matrix4x4 view, Matrix4x4 projection, IEnumerable<GameObject> scene)
    {
        _sceneRenderer.Render(commandList, view, projection, scene);
    }

    public void ResolveMSAA(CommandList commandList, TextureView msaaColorView)
    {
        _resolveRenderer.Render(commandList, msaaColorView);
    }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _sampler.Dispose();
    }
}