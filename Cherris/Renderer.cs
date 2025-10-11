using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public class Renderer : IDisposable
{
    private readonly GraphicsManager _graphicsManager;
    private SceneRenderer _sceneRenderer;
    private SkyboxRenderer _skyboxRenderer;
    private ResolveRenderer _resolveRenderer;
    private OutlineRenderer _outlineRenderer;
    private Sampler _sampler;
    private Snapshotter _snapshotter;
    private bool _snapshotRequested;
    private string _snapshotPath;

    public ResourceLayout TextureLayout => _sceneRenderer.TextureLayout;
    public ResourceLayout MaterialLayout => _sceneRenderer.MaterialLayout;
    public Sampler Sampler => _sampler;

    public Renderer(GraphicsManager graphicsManager)
    {
        _graphicsManager = graphicsManager;
        CreateResources();
    }

    public void SetSnapshotter(Snapshotter snapshotter)
    {
        _snapshotter = snapshotter;
    }

    public void RequestSnapshot(string path)
    {
        _snapshotRequested = true;
        _snapshotPath = path;
    }

    public void ProcessSnapshot()
    {
        _snapshotter?.SaveCopiedData(_snapshotPath);
    }

    private void CreateResources()
    {
        GraphicsDevice gd = _graphicsManager.GraphicsDevice;
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

        _sceneRenderer = new SceneRenderer(gd, vertexLayout);
        _skyboxRenderer = new SkyboxRenderer(gd, _sampler, vertexLayout);
        _resolveRenderer = new ResolveRenderer(gd);
        _outlineRenderer = new OutlineRenderer(gd);

        OnWindowResized();
    }

    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, IReadOnlyList<OutlineProfile> activeProfiles, float windowWidth, float windowHeight)
    {
        if (mainCamera is null) return;

        Matrix4x4 view = mainCamera.GetViewMatrix();
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(windowWidth / windowHeight);

        CommandList cl = _graphicsManager.CommandList;
        cl.Begin();

        // Pass 1: Render Object IDs to ID framebuffer
        cl.SetFramebuffer(_graphicsManager.IdFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f, 0);
        _sceneRenderer.RenderIdPass(cl, view, projection, gameObjects, selectedObject, activeProfiles);

        // Pass 2: Render scene to MSAA framebuffer
        cl.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f, 0);

        if (skybox is not null)
        {
            _skyboxRenderer.Render(cl, skybox, view, projection);
        }
        _sceneRenderer.Render(cl, view, projection, gameObjects);

        // Pass 3: Resolve MSAA to our final intermediate texture
        cl.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        _resolveRenderer.Render(cl, _graphicsManager.MsaaColorView);

        // Pass 4: Composite outlines and render to screen's swapchain
        cl.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        _outlineRenderer.Render(cl, windowWidth, windowHeight, activeProfiles);

        if (_snapshotRequested && _snapshotter != null)
        {
            // Copy from our stable intermediate texture, not the swapchain
            _snapshotter.RecordCopyCommand(cl, _graphicsManager.FinalColorTarget);
            _snapshotRequested = false;
        }

        cl.End();
        _graphicsManager.GraphicsDevice.SubmitCommands(cl);
        _graphicsManager.GraphicsDevice.SwapBuffers();
    }

    public void OnWindowResized()
    {
        _sceneRenderer.SetFramebuffers(_graphicsManager.MsaaFramebuffer, _graphicsManager.IdFramebuffer);
        _skyboxRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _resolveRenderer.SetFramebuffer(_graphicsManager.FinalFramebuffer);

        // Update outline renderer with new textures and set its framebuffer
        _outlineRenderer.CreateResources(_graphicsManager.FinalColorView, _graphicsManager.ObjectIdView);
        _outlineRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
    }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _outlineRenderer.Dispose();
        _sampler.Dispose();
    }
}