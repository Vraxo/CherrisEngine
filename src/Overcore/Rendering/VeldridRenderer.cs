using Overcore.Components;
using Overcore.Core.Logging;
using Overcore.Rendering;
using Overcore.Terrain;
using System.Linq;
using System.Numerics;
using Veldrid;
using TerrainComponent = Overcore.Components.Terrain;
namespace Overcore.Core;
// This class now acts as the Veldrid implementation of IRenderer
public partial class VeldridRenderer : IRenderer
{
    private readonly VeldridGraphicsManager _graphicsManager;
    private readonly GraphicsDevice _graphicsDevice;
    private SceneRenderer _sceneRenderer;
    private SkyboxRenderer _skyboxRenderer;
    private ResolveRenderer _resolveRenderer;
    private FinalPassRenderer _finalPassRenderer;
    private BloomRenderer _bloomRenderer;
    private VeldridUIRenderer _uiRenderer;
    private readonly IResourceManager _resourceManager;
    private Sampler _sampler;
    private readonly Snapshotter _snapshotter;
    private bool _snapshotRequested;
    private string _snapshotPath;
    public bool ShowGrid { get; set; }
    public bool ShowPhysicsColliders { get; set; }
    public VeldridRenderer(VeldridGraphicsManager graphicsManager, GraphicsDevice graphicsDevice, IResourceManager resourceManager)
    {
        _graphicsManager = graphicsManager;
        _graphicsDevice = graphicsDevice;
        _resourceManager = resourceManager;
        _snapshotter = new Snapshotter(_graphicsDevice);
        CreateResources();
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
        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        // Field order, not size: Veldrid walks the elements against the struct as laid out, so a missing element
        // shifts every one after it and the mesh draws with garbage vertex data.
        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));
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
        _sceneRenderer = new SceneRenderer(_graphicsDevice, vertexLayout);
        _skyboxRenderer = new SkyboxRenderer(_graphicsDevice, _sampler, vertexLayout);
        _resolveRenderer = new ResolveRenderer(_graphicsDevice);
        _finalPassRenderer = new FinalPassRenderer(_graphicsDevice);
        _bloomRenderer = new BloomRenderer(_graphicsDevice);
        _uiRenderer = new VeldridUIRenderer(_graphicsDevice, _resourceManager);
        OnWindowResized();
    }
    /// <summary>
    /// Set once, so a scene full of skinned meshes logs a line rather than one per object per frame.
    /// </summary>
    private bool _warnedAboutSkinning;

    private VeldridMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
    {
        if (mr.BackendData is VeldridMeshRendererData data)
        {
            return data;
        }

        // The Veldrid backend has the static pipeline only: a skinned mesh drawn here sits at its bind pose, because
        // this path never reads a bone palette. Saying so once beats a character that quietly does not move.
        if (!_warnedAboutSkinning && mr.GameObject?.GetComponent<SkinnedMesh>() is not null)
        {
            _warnedAboutSkinning = true;

            Logger.Warning(
                "[Veldrid] Skinned meshes draw at their bind pose on this backend. " +
                "GPU skinning is implemented for the OpenGL backend; see docs/animation.md section 3.4.");
        }
        var newData = new VeldridMeshRendererData(_graphicsDevice, mr, _sceneRenderer.TextureLayout, _sceneRenderer.MaterialLayout, _sampler);
        mr.BackendData = newData;
        return newData;
    }
    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
    {
        List<GameObject> sceneObjects = [.. gameObjects];
        _uiRenderer.Prepare(sceneObjects, (int)windowWidth, (int)windowHeight);
        if (mainCamera is null)
        {
            CommandList uiOnly = _graphicsManager.CommandList;
            uiOnly.Begin();
            uiOnly.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
            uiOnly.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
            uiOnly.ClearColorTarget(0, RgbaFloat.Black);
            _uiRenderer.Render(uiOnly, sceneObjects, (int)windowWidth, (int)windowHeight);
            uiOnly.End();
            _graphicsDevice.SubmitCommands(uiOnly);
            return;
        }

        Matrix4x4 view = mainCamera.GetViewMatrix();
        Matrix4x4 projection = mainCamera.GetProjectionMatrix(windowWidth / windowHeight);
        CommandList cl = _graphicsManager.CommandList;
        cl.Begin();
        // Pass 1: Render scene to MSAA framebuffer
        cl.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1f, 0);
        if (skybox is not null)
        {
            _skyboxRenderer.Render(cl, skybox, view, projection);
        }

        _sceneRenderer.Render(cl, view, projection, sceneObjects, GetOrCreateBackendData, GetTerrainResourceSet);
        if (selectedObject?.GetComponent<MeshRenderer>() is not null)
        {
            _sceneRenderer.RenderOutline(cl, view, projection, selectedObject, GetOrCreateBackendData(selectedObject.GetComponent<MeshRenderer>()));
        }
        // Pass 2: Resolve MSAA
        cl.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        _resolveRenderer.Render(cl, _graphicsManager.MsaaColorView);
        // Pass 3: Bloom
        var bloomViewport = new Viewport(0, 0, _graphicsManager.BloomColorTarget.Width, _graphicsManager.BloomColorTarget.Height, 0, 1);
        cl.SetViewport(0, bloomViewport);
        cl.SetFramebuffer(_graphicsManager.BloomFramebuffer);
        _bloomRenderer.RenderBrightPass(cl, _graphicsManager.FinalColorView);
        _bloomRenderer.RenderBlur(cl, _graphicsManager.BloomColorView, _graphicsManager.BloomFramebuffer, _graphicsManager.BloomTempColorView, _graphicsManager.BloomTempFramebuffer);
        // Pass 4: Composite and draw to screen
        cl.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        cl.SetViewport(0, new Viewport(0, 0, windowWidth, windowHeight, 0, 1));
        _finalPassRenderer.Render(cl, _graphicsManager.FinalColorView, exposure);
        _finalPassRenderer.RenderBloom(cl, _graphicsManager.BloomColorView);
        _uiRenderer.Render(cl, sceneObjects, (int)windowWidth, (int)windowHeight);
        if (_snapshotRequested && _snapshotter is not null)
        {
            _snapshotter.RecordCopyCommand(cl, _graphicsManager.FinalColorTarget);
            _snapshotRequested = false;
        }
        cl.End();
        _graphicsDevice.SubmitCommands(cl);
    }
    public void OnWindowResized()
    {
        _graphicsManager.Resize((int)_graphicsManager.SwapchainFramebuffer.Width, (int)_graphicsManager.SwapchainFramebuffer.Height);
        _sceneRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _skyboxRenderer.SetFramebuffer(_graphicsManager.MsaaFramebuffer);
        _resolveRenderer.SetFramebuffer(_graphicsManager.FinalFramebuffer);
        _finalPassRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        _uiRenderer.SetFramebuffer(_graphicsManager.SwapchainFramebuffer);
        _bloomRenderer.OnWindowResized(_graphicsManager.BloomFramebuffer, _graphicsManager.BloomTempFramebuffer);
    }
    /// <summary>One terrain's set-3 bundle, rebuilt when its paint or its layer textures change.</summary>
    private sealed class TerrainRenderEntry : IDisposable
    {
        public required ResourceSet Set { get; init; }
        public required DeviceBuffer Properties { get; init; }
        public required Veldrid.Texture Alphamap { get; init; }
        public required TextureView AlphamapView { get; init; }
        public required Veldrid.Texture Alphamap2 { get; init; }
        public required TextureView Alphamap2View { get; init; }
        public required int Version { get; init; }
        public required ITexture?[] LayerTextures { get; init; }

        public void Dispose()
        {
            Set.Dispose();
            Properties.Dispose();
            AlphamapView.Dispose();
            Alphamap.Dispose();
            Alphamap2View.Dispose();
            Alphamap2.Dispose();
        }
    }

    private readonly Dictionary<TerrainComponent, TerrainRenderEntry> _terrainSets = [];

    /// <summary>
    /// The set-3 bundle for one draw: a terrain's alphamaps and layer textures, or the shared
    /// no-terrain set for everything else. The properties buffer is re-uploaded per draw — 272 bytes —
    /// so a tint edit shows without waiting for a paint stroke to invalidate the cache.
    /// </summary>
    private ResourceSet GetTerrainResourceSet(GameObject gameObject)
    {
        if (gameObject.GetComponent<TerrainComponent>() is not { } terrain)
        {
            return _sceneRenderer.NoTerrainResourceSet;
        }

        TerrainData data = terrain.TerrainData;
        ITexture?[] layerTextures = [.. Enumerable.Range(0, 8).Select(terrain.GetLayerTexture)];

        if (_terrainSets.TryGetValue(terrain, out TerrainRenderEntry? entry)
            && (entry.Version != data.AlphamapVersion || !entry.LayerTextures.SequenceEqual(layerTextures)))
        {
            entry.Dispose();
            _terrainSets.Remove(terrain);
            entry = null;
        }

        entry ??= CreateTerrainEntry(terrain, data, layerTextures);
        _terrainSets[terrain] = entry;

        _graphicsDevice.UpdateBuffer(entry.Properties, 0, SceneRenderer.PackTerrainProperties(true, data));
        return entry.Set;
    }

    private TerrainRenderEntry CreateTerrainEntry(
        TerrainComponent terrain, TerrainData data, ITexture?[] layerTextures)
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        (Veldrid.Texture alphamap, TextureView alphamapView) = CreateAlphamapTexture(data, 0);
        (Veldrid.Texture alphamap2, TextureView alphamap2View) = CreateAlphamapTexture(data, 4);

        DeviceBuffer properties = factory.CreateBuffer(
            new BufferDescription(SceneRenderer.TerrainPropertiesSize, BufferUsage.UniformBuffer));

        BindableResource[] resources = new BindableResource[12];
        resources[0] = properties;
        resources[1] = alphamapView;
        resources[2] = alphamap2View;
        for (int layer = 0; layer < 8; layer++)
        {
            resources[3 + layer] = (layerTextures[layer]?.GetBackendHandle() as Texture)?.VeldridTextureView
                ?? _sceneRenderer.WhiteTextureView;
        }

        resources[11] = _sampler;

        return new TerrainRenderEntry
        {
            Set = factory.CreateResourceSet(new ResourceSetDescription(_sceneRenderer.TerrainLayout, resources)),
            Properties = properties,
            Alphamap = alphamap,
            AlphamapView = alphamapView,
            Alphamap2 = alphamap2,
            Alphamap2View = alphamap2View,
            Version = data.AlphamapVersion,
            LayerTextures = layerTextures
        };
    }

    /// <summary>Four alphamap layers as an RGBA texture, the second set riding layers four to seven.</summary>
    private (Veldrid.Texture, TextureView) CreateAlphamapTexture(TerrainData data, int layerOffset)
    {
        int resolution = data.AlphamapResolution;
        int layers = data.Layers.Count;
        float[] weights = data.GetAlphamap();
        byte[] pixels = new byte[resolution * resolution * 4];
        for (int pixel = 0; pixel < resolution * resolution; pixel++)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                int layer = layerOffset + channel;
                float weight = layer < layers ? weights[(pixel * layers) + layer] : 0f;
                pixels[(pixel * 4) + channel] = (byte)Math.Clamp((int)(weight * 255f), 0, 255);
            }
        }

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        Veldrid.Texture texture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)resolution, (uint)resolution, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _graphicsDevice.UpdateTexture(texture, pixels, 0, 0, 0, (uint)resolution, (uint)resolution, 1, 0, 0);
        return (texture, factory.CreateTextureView(texture));
    }

    public void Dispose()
    {
        foreach (TerrainRenderEntry entry in _terrainSets.Values)
        {
            entry.Dispose();
        }

        _terrainSets.Clear();
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _resolveRenderer.Dispose();
        _finalPassRenderer.Dispose();
        _bloomRenderer.Dispose();
        _uiRenderer.Dispose();
        _sampler.Dispose();
        _snapshotter.Dispose();
        _graphicsManager.Dispose();
    }
}