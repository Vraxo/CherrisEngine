using System.Collections.Generic;
using System.Diagnostics;
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
    private BlurRenderer _blurRenderer;
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

        var sw = Stopwatch.StartNew();
        _sceneRenderer = new SceneRenderer(gd, vertexLayout);
        sw.Stop();
        Console.WriteLine($"[PROFILE] SceneRenderer created in {sw.ElapsedMilliseconds}ms");
        sw.Restart();

        _skyboxRenderer = new SkyboxRenderer(gd, _sampler, vertexLayout);
        sw.Stop();
        Console.WriteLine($"[PROFILE] SkyboxRenderer created in {sw.ElapsedMilliseconds}ms");
        sw.Restart();

        _resolveRenderer = new ResolveRenderer(gd);
        sw.Stop();
        Console.WriteLine($"[PROFILE] ResolveRenderer created in {sw.ElapsedMilliseconds}ms");
        sw.Restart();

        _outlineRenderer = new OutlineRenderer(gd);
        sw.Stop();
        Console.WriteLine($"[PROFILE] OutlineRenderer created in {sw.ElapsedMilliseconds}ms");
        sw.Restart();

        _blurRenderer = new BlurRenderer(gd);
        sw.Stop();
        Console.WriteLine($"[PROFILE] BlurRenderer created in {sw.ElapsedMilliseconds}ms");
        sw.Restart();

        OnWindowResized();
        sw.Stop();
        Console.WriteLine($"[PROFILE] Initial OnWindowResized (pipeline creation) took {sw.ElapsedMilliseconds}ms");
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

        // --- New Blur Passes ---
        // Pass 1.1: Horizontal Blur
        cl.SetFramebuffer(_graphicsManager.BlurFramebufferA);
        cl.SetViewport(0, new Viewport(0, 0, _graphicsManager.BlurFramebufferA.Width, _graphicsManager.BlurFramebufferA.Height, 0, 1));
        _blurRenderer.CreateResources(_graphicsManager.ObjectIdView);
        _blurRenderer.Render(cl, new Vector2(1, 0));

        // Pass 1.2: Vertical Blur
        cl.SetFramebuffer(_graphicsManager.BlurFramebufferB);
        cl.SetViewport(0, new Viewport(0, 0, _graphicsManager.BlurFramebufferB.Width, _graphicsManager.BlurFramebufferB.Height, 0, 1));
        _blurRenderer.CreateResources(_graphicsManager.BlurViewA);
        _blurRenderer.Render(cl, new Vector2(0, 1));
        // The result of the full blur is now in _graphicsManager.BlurViewB

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

        // Pass 4: Composite outlines and glow, then render to screen's swapchain
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
        _blurRenderer.SetFramebuffer(_graphicsManager.BlurFramebufferA); // Framebuffer is reusable for both passes

        // Update outline renderer with new textures and set its framebuffer
        _outlineRenderer.CreateResources(_graphicsManager.FinalColorView, _graphicsManager.ObjectIdView, _graphicsManager.BlurViewB);
        _outlineRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
    }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _outlineRenderer.Dispose();
        _blurRenderer.Dispose();
        _sampler.Dispose();
    }
}