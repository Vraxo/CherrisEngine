using Overcore.Core;
using Overcore.Rendering;
using Overcore.Terrain;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// Sculpted ground, Unity's <c>Terrain</c>: a <see cref="TerrainData"/> heightfield drawn as a mesh, kept
/// in step with the data as it is sculpted and painted.
/// </summary>
/// <remarks>
/// <para>
/// The component owns one <see cref="MeshRenderer"/> on the same object, built by
/// <see cref="TerrainMesher"/> from the data, so the ground draws, casts and receives shadows, and bakes
/// into a NavMesh through exactly the paths every other mesh in the scene uses. Add a
/// <see cref="TerrainCollider"/> beside it and the same ground is solid to bodies, rays and character
/// controllers. Its forest, when it has one, rides on a child object the component owns outright (named
/// <c>Trees</c>, recognised by <see cref="IsTreeObject"/>): terrain property, never scene content, so the
/// scene file writes the forest into the terrain's own data and the child is rebuilt from it on load.
/// </para>
/// <para>
/// The mesh is rebuilt when the data changes: eagerly by <see cref="Sculpt"/> and <see cref="Paint"/>, and
/// once a frame by the scene for edits made straight against <see cref="TerrainData"/> (a script raising
/// a hill, say), which the data's version counters report.
/// </para>
/// </remarks>
public class Terrain : Component
{
    /// <summary>The mesh name the forest's renderer goes by, which is how it is recognised as terrain property.</summary>
    internal const string TreeMeshName = "TerrainTrees";

    /// <summary>The mesh name the detail's renderer goes by, recognised the same way.</summary>
    internal const string DetailMeshName = "TerrainDetail";

    /// <summary>The mesh name a chunk's renderer goes by, recognised the same way.</summary>
    internal const string ChunkMeshName = "TerrainChunk";

    private static readonly Mesh EmptyMesh = new([], []);

    private TerrainData _data = new();
    private int _builtHeightVersion = -1;
    private int _builtAlphamapVersion = -1;
    private int _builtHolesVersion = -1;
    private int _builtTreesVersion = -1;
    private int _builtDetailVersion = -1;
    private Func<string, Mesh?>? _treeMeshResolver;
    private Func<string, ITexture?>? _detailTextureResolver;
    private Material? _treeMaterial;
    private Material? _detailMaterial;
    private string _detailMaterialTextureName = string.Empty;
    private int _chunksPerSide = 1;
    private float _lodDistance = 80f;
    private Mesh?[] _chunkMeshes = [];
    private int[] _chunkLevels = [];
    private readonly List<MeshRenderer> _chunkRenderers = [];
    private Material? _chunkMaterial;

    /// <summary>The ground's heights and paint. Replacing it rebuilds the mesh at once.</summary>
    [HideInInspector]
    public TerrainData TerrainData
    {
        get => _data;
        set
        {
            _data = value ?? throw new ArgumentNullException(nameof(value));
            _builtHeightVersion = -1;
            _builtAlphamapVersion = -1;
            _builtHolesVersion = -1;
            _builtTreesVersion = -1;
            _builtDetailVersion = -1;
            Rebuild();
        }
    }

    /// <summary>The terrain's extent in metres, forwarded to the data so the inspector and the file see it.</summary>
    public Vector3 Size
    {
        get => _data.Size;
        set
        {
            if (_data.Size == value)
            {
                return;
            }

            _data.Size = value;
            _builtHeightVersion = -1;
            _builtHolesVersion = -1;
            Rebuild();
        }
    }

    /// <summary>
    /// The material the ground is drawn with, when one has been given. Left null, the ground draws with a
    /// plain white material and its painted layers show through their tints in the vertex colours.
    /// </summary>
    [HideInInspector]
    public Material? Material { get; set; }

    /// <summary>The mesh the ground currently draws with, or null before its first build.</summary>
    [HideInInspector]
    public Mesh? Mesh { get; private set; }

    /// <summary>The merged mesh of every standing tree, or null when the forest draws nothing.</summary>
    [HideInInspector]
    public Mesh? TreeMesh { get; private set; }

    /// <summary>
    /// Turns a tree prototype's mesh name into the mesh itself. Prototypes name their meshes, like layers
    /// name their textures, and resolution is a loading concern: the scene factory wires this to the
    /// resource manager, and code that grows a terrain by hand wires its own. Setting it once trees stand
    /// rebuilds the forest with it.
    /// </summary>
    [HideInInspector]
    public Func<string, Mesh?>? TreeMeshResolver
    {
        get => _treeMeshResolver;
        set
        {
            _treeMeshResolver = value;
            if (_data.Trees.Count > 0)
            {
                _builtTreesVersion = -1;
                Rebuild();
            }
        }
    }

    /// <summary>
    /// True when an object is a terrain's forest child: terrain property riding on a scene object, not
    /// scene content. The scene file leaves it out (the forest is in the terrain's data) and a NavMesh
    /// bake walks under the trees rather than over them.
    /// </summary>
    public static bool IsTreeObject(GameObject? gameObject) =>
        gameObject is not null
        && gameObject.GetComponent<MeshRenderer>() is { MeshName: TreeMeshName }
        && gameObject.Transform.Parent?.GameObject.GetComponent<Terrain>() is not null;

    /// <summary>True when an object is a terrain's detail child, terrain property like the forest's. See <see cref="IsTreeObject"/>.</summary>
    public static bool IsDetailObject(GameObject? gameObject) =>
        gameObject is not null
        && gameObject.GetComponent<MeshRenderer>() is { MeshName: DetailMeshName }
        && gameObject.Transform.Parent?.GameObject.GetComponent<Terrain>() is not null;

    /// <summary>
    /// True when an object is one of a chunked terrain's ground chunks. Unlike the forest and detail
    /// children this one is ground, not scenery: a NavMesh bake includes it (through the terrain's data,
    /// at full detail), while the scene file still leaves it out.
    /// </summary>
    public static bool IsChunkObject(GameObject? gameObject) =>
        gameObject is not null
        && gameObject.GetComponent<MeshRenderer>() is { MeshName: ChunkMeshName }
        && gameObject.Transform.Parent?.GameObject.GetComponent<Terrain>() is not null;

    /// <summary>
    /// How many chunks the ground is split into along each side. One (the default) draws the terrain as
    /// a single mesh on its own object, as it always has. More gives each chunk its own owned child
    /// renderer and a level of detail by distance (see <see cref="LodDistance"/>): the way big ground
    /// stays cheap. Physics and NavMesh bakes always work from the full-resolution data, whatever this
    /// says — chunks are a drawing decision.
    /// </summary>
    public int ChunksPerSide
    {
        get => _chunksPerSide;
        set
        {
            int clamped = Math.Clamp(value, 1, 8);
            if (clamped == _chunksPerSide)
            {
                return;
            }

            _chunksPerSide = clamped;
            _chunkLevels = [];
            Rebuild();
        }
    }

    /// <summary>
    /// The distance in metres at which a chunk drops its first level of detail, doubling per level
    /// after that: a chunk twice this far draws a quarter of its triangles.
    /// </summary>
    public float LodDistance
    {
        get => _lodDistance;
        set => _lodDistance = MathF.Max(value, 1f);
    }

    /// <summary>True when the ground draws as chunks. See <see cref="ChunksPerSide"/>.</summary>
    [HideInInspector]
    public bool UsesChunks => _chunksPerSide > 1;

    /// <summary>The chunk meshes currently drawn, row-major, or empty when the ground is one mesh.</summary>
    [HideInInspector]
    public IReadOnlyList<Mesh?> ChunkMeshes => _chunkMeshes;

    /// <summary>The level of detail each chunk currently draws at, row-major: 0 is full detail.</summary>
    [HideInInspector]
    public IReadOnlyList<int> ChunkLevels => _chunkLevels;

    /// <summary>The merged mesh of every detail tuft, or null when nothing grows.</summary>
    [HideInInspector]
    public Mesh? DetailMesh { get; private set; }

    /// <summary>
    /// Turns a detail prototype's texture name into the texture itself, wired by the scene factory like
    /// <see cref="TreeMeshResolver"/>. Only the first prototype's texture can ride the one detail
    /// material; the rest tint their tufts.
    /// </summary>
    [HideInInspector]
    public Func<string, ITexture?>? DetailTextureResolver
    {
        get => _detailTextureResolver;
        set
        {
            _detailTextureResolver = value;
            if (_data.DetailPrototypes.Count > 0)
            {
                _builtDetailVersion = -1;
                Rebuild();
            }
        }
    }

    private readonly List<ITexture?> _layerTextures = [];

    /// <summary>
    /// The resolved texture of one layer, by layer index, or null when the layer names no texture or none
    /// has resolved it yet. Layers name their textures in the data; resolution is a loading concern (the
    /// scene factory resolves them through the resource manager), and a renderer that finds null paints
    /// the layer's tint alone.
    /// </summary>
    public ITexture? GetLayerTexture(int layerIndex) =>
        layerIndex >= 0 && layerIndex < _layerTextures.Count ? _layerTextures[layerIndex] : null;

    /// <summary>Records the resolved texture of one layer. See <see cref="GetLayerTexture"/>.</summary>
    public void SetLayerTexture(int layerIndex, ITexture? texture)
    {
        if (layerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }

        while (_layerTextures.Count <= layerIndex)
        {
            _layerTextures.Add(null);
        }

        _layerTextures[layerIndex] = texture;
    }

    /// <inheritdoc />
    public override void Awake()
    {
        base.Awake();
        Rebuild();
    }

    /// <inheritdoc />
    public override void OnEnable()
    {
        base.OnEnable();
        Rebuild();
    }

    /// <summary>Rebuilds the drawn mesh from the data now, whatever the versions say.</summary>
    public void Rebuild()
    {
        if (GameObject is null || IsDestroyed)
        {
            return;
        }

        _builtHeightVersion = _data.Version;
        _builtAlphamapVersion = _data.AlphamapVersion;
        _builtHolesVersion = _data.HolesVersion;

        if (_chunksPerSide <= 1)
        {
            Mesh = TerrainMesher.Build(_data);
            SetChunkChildrenActive(false);

            MeshRenderer renderer = GameObject.GetComponent<MeshRenderer>()
                ?? GameObject.AddComponent(new MeshRenderer
                {
                    Mesh = Mesh,
                    MeshName = "Terrain",
                    Material = Material ?? new Material(null!, "White")
                });

            renderer.Mesh = Mesh;
            renderer.Enabled = true;
            if (Material is not null)
            {
                renderer.Material = Material;
            }
            else if (renderer.Material is null)
            {
                renderer.Material = new Material(null!, "White");
            }
        }
        else
        {
            // The ground moves onto its chunk children; this object's own renderer falls silent so no
            // path — bakes included — can mistake it for the ground.
            Mesh = null;
            if (GameObject.GetComponent<MeshRenderer>() is { } groundRenderer)
            {
                groundRenderer.Enabled = false;
            }

            EnsureChunkRenderers();
        }

        TreeMesh = _treeMeshResolver is null ? null : TerrainForestMesher.Build(_data, _treeMeshResolver);
        _builtTreesVersion = _data.TreesVersion;
        EnsureTreeRenderer();

        DetailMesh = TerrainDetailMesher.Build(_data);
        _builtDetailVersion = _data.DetailVersion;
        EnsureDetailRenderer();

        // The collider beside this terrain holds a physics copy of the same ground; it is told to remake it.
        GameObject.GetComponent<TerrainCollider>()?.RefreshShape();
    }

    /// <summary>An owned child's renderer by its mesh name, or null when the child has not been grown yet.</summary>
    private MeshRenderer? FindOwnedRenderer(string meshName)
    {
        foreach (Transform child in GameObject.Transform.Children)
        {
            if (child.GameObject.GetComponent<MeshRenderer>() is { } found && found.MeshName == meshName)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the forest's child object in step with <see cref="TreeMesh"/>: created on first need, emptied
    /// (and silenced) when the forest is gone. The child wears its own plain material, so the ground's
    /// texture never smears across the trees and the trees never take the ground's splat branch.
    /// </summary>
    private void EnsureTreeRenderer()
    {
        MeshRenderer? renderer = FindOwnedRenderer(TreeMeshName);

        if (renderer is null)
        {
            if (TreeMesh is null)
            {
                return;
            }

            GameObject treesObject = new("Trees");
            treesObject.Transform.SetParent(GameObject.Transform, worldPositionStays: false);
            _treeMaterial ??= new Material(null!, "White");
            renderer = treesObject.AddComponent(new MeshRenderer
            {
                Mesh = TreeMesh,
                MeshName = TreeMeshName,
                Material = _treeMaterial
            });
        }

        renderer.Mesh = TreeMesh ?? EmptyMesh;
        renderer.Enabled = TreeMesh is not null;
    }

    /// <summary>
    /// Keeps the detail's child object in step with <see cref="DetailMesh"/>, the forest child's smaller
    /// sibling. Its material carries the first prototype's cutout texture when one resolves, and is
    /// otherwise plain white so the tufts' own colours show.
    /// </summary>
    private void EnsureDetailRenderer()
    {
        MeshRenderer? renderer = FindOwnedRenderer(DetailMeshName);

        if (renderer is null)
        {
            if (DetailMesh is null)
            {
                return;
            }

            GameObject detailObject = new("Detail");
            detailObject.Transform.SetParent(GameObject.Transform, worldPositionStays: false);
            renderer = detailObject.AddComponent(new MeshRenderer
            {
                Mesh = DetailMesh,
                MeshName = DetailMeshName,
                Material = DetailMaterial()
            });
        }

        renderer.Mesh = DetailMesh ?? EmptyMesh;
        renderer.Enabled = DetailMesh is not null;
        renderer.Material = DetailMaterial();
    }

    /// <summary>
    /// Keeps the chunk children in step with <see cref="ChunksPerSide"/>: one owned child per chunk,
    /// created on first need, each wearing the ground's material so the splat branch paints it exactly
    /// as it would the single mesh. Chunks rebuild at their standing levels of detail.
    /// </summary>
    private void EnsureChunkRenderers()
    {
        int chunks = _chunksPerSide;
        int count = chunks * chunks;

        _chunkRenderers.Clear();
        foreach (Transform child in GameObject.Transform.Children)
        {
            if (child.GameObject.GetComponent<MeshRenderer>() is { } found && found.MeshName == ChunkMeshName)
            {
                _chunkRenderers.Add(found);
            }
        }

        _chunkMaterial ??= Material ?? new Material(null!, "White");
        while (_chunkRenderers.Count < count)
        {
            int index = _chunkRenderers.Count;
            GameObject chunkObject = new($"Chunk {index % chunks},{index / chunks}");
            chunkObject.Transform.SetParent(GameObject.Transform, worldPositionStays: false);
            _chunkRenderers.Add(chunkObject.AddComponent(new MeshRenderer
            {
                Mesh = EmptyMesh,
                MeshName = ChunkMeshName,
                Material = _chunkMaterial
            }));
        }

        if (_chunkMeshes.Length != count)
        {
            _chunkMeshes = new Mesh?[count];
            _chunkLevels = new int[count];
        }

        for (int index = 0; index < _chunkRenderers.Count; index++)
        {
            MeshRenderer renderer = _chunkRenderers[index];
            if (index >= count)
            {
                renderer.Enabled = false;
                continue;
            }

            int level = Math.Min(_chunkLevels[index], MaxChunkLevel(index));
            _chunkLevels[index] = level;
            _chunkMeshes[index] = BuildChunkAtLevel(index, level);
            renderer.Mesh = _chunkMeshes[index]!;
            renderer.Enabled = true;
            if (Material is not null)
            {
                renderer.Material = Material;
            }
        }
    }

    private void SetChunkChildrenActive(bool active)
    {
        foreach (Transform child in GameObject.Transform.Children)
        {
            if (child.GameObject.GetComponent<MeshRenderer>() is { MeshName: ChunkMeshName } renderer)
            {
                renderer.Enabled = active;
            }
        }
    }

    private Mesh BuildChunkAtLevel(int index, int level)
    {
        (int startX, int startZ, int pointsX, int pointsZ) = TerrainMesher.ChunkRect(
            _data.HeightmapResolution, _chunksPerSide, index % _chunksPerSide, index / _chunksPerSide);
        return TerrainMesher.BuildChunk(_data, startX, startZ, pointsX, pointsZ, 1 << level);
    }

    /// <summary>The highest level of detail a chunk's spans divide into exactly.</summary>
    private int MaxChunkLevel(int index)
    {
        (_, _, int pointsX, int pointsZ) = TerrainMesher.ChunkRect(
            _data.HeightmapResolution, _chunksPerSide, index % _chunksPerSide, index / _chunksPerSide);

        int level = 0;
        while ((pointsX - 1) % (1 << (level + 1)) == 0 && (pointsZ - 1) % (1 << (level + 1)) == 0
            && (1 << (level + 1)) <= Math.Min(pointsX, pointsZ) - 1)
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// Re-aims each chunk's level of detail at where the camera stands: full detail within
    /// <see cref="LodDistance"/>, one level coarser per doubling beyond it. Only a chunk whose level
    /// actually changes is remeshed. The scene calls this once a frame with its camera's place.
    /// </summary>
    /// <param name="cameraPosition">The camera's world position, or null when the scene has none.</param>
    public void UpdateLod(Vector3? cameraPosition)
    {
        if (_chunksPerSide <= 1 || cameraPosition is null || GameObject is null || IsDestroyed)
        {
            return;
        }

        Matrix4x4 world = Transform.GetModelMatrix();
        int resolution = _data.HeightmapResolution;

        for (int index = 0; index < _chunkLevels.Length && index < _chunkRenderers.Count; index++)
        {
            (int startX, int startZ, int pointsX, int pointsZ) = TerrainMesher.ChunkRect(
                resolution, _chunksPerSide, index % _chunksPerSide, index / _chunksPerSide);

            float centerU = (startX + ((pointsX - 1) * 0.5f)) / (resolution - 1);
            float centerV = (startZ + ((pointsZ - 1) * 0.5f)) / (resolution - 1);
            Vector3 localCentre = new(
                centerU * _data.Size.X,
                _data.SampleHeight(centerU, centerV),
                centerV * _data.Size.Z);
            float distance = Vector3.Distance(
                Vector3.Transform(localCentre, world), cameraPosition.Value);

            int level = 0;
            float threshold = MathF.Max(_lodDistance, 1f);
            int maxLevel = MaxChunkLevel(index);
            while (level < maxLevel && distance > threshold)
            {
                level++;
                threshold *= 2f;
            }

            if (level == _chunkLevels[index])
            {
                continue;
            }

            _chunkLevels[index] = level;
            _chunkMeshes[index] = BuildChunkAtLevel(index, level);
            _chunkRenderers[index].Mesh = _chunkMeshes[index]!;
        }
    }

    /// <summary>The detail material, remade only when the texture it should carry changes its name.</summary>
    private Material DetailMaterial()
    {
        string textureName = _data.DetailPrototypes.Count > 0
            ? _data.DetailPrototypes[0].TextureName
            : string.Empty;

        if (_detailMaterial is null || _detailMaterialTextureName != textureName)
        {
            _detailMaterialTextureName = textureName;
            ITexture? texture = string.IsNullOrWhiteSpace(textureName)
                ? null
                : _detailTextureResolver?.Invoke(textureName);
            _detailMaterial = texture is null
                ? new Material(null!, "White")
                : new Material(texture, textureName) { AlphaCutoff = 0.5f };
        }

        return _detailMaterial;
    }

    /// <summary>Rebuilds the mesh if the data has been edited since the last build. The scene calls this once a frame.</summary>
    internal void RefreshIfDirty()
    {
        if (_builtHeightVersion != _data.Version
            || _builtAlphamapVersion != _data.AlphamapVersion
            || _builtHolesVersion != _data.HolesVersion
            || _builtTreesVersion != _data.TreesVersion
            || _builtDetailVersion != _data.DetailVersion)
        {
            Rebuild();
        }
    }

    /// <summary>One detail stroke against the ground, detail mesh rebuilt after it. See <see cref="TerrainBrush.ApplyDetail"/>.</summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="strength">Density per stroke at the centre; negative thins.</param>
    /// <param name="prototypeIndex">Which prototype grows.</param>
    /// <returns>How many pixels changed.</returns>
    public int ApplyDetail(Vector2 center, float radius, float strength, int prototypeIndex)
    {
        int changed = TerrainBrush.ApplyDetail(_data, center, radius, strength, prototypeIndex);
        if (changed > 0)
        {
            Rebuild();
        }

        return changed;
    }

    /// <summary>One planting stroke against the ground, forest rebuilt after it. See <see cref="TerrainBrush.PlantTrees"/>.</summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="prototypeIndex">Which prototype to plant.</param>
    /// <param name="attempts">How many seeds the stroke drops.</param>
    /// <param name="minSpacing">The closest two trees may stand, normalised.</param>
    /// <param name="random">The scatter's dice.</param>
    /// <returns>How many trees took root.</returns>
    public int PlantTrees(Vector2 center, float radius, int prototypeIndex, int attempts, float minSpacing, Random random)
    {
        int planted = TerrainBrush.PlantTrees(_data, center, radius, prototypeIndex, attempts, minSpacing, random);
        if (planted > 0)
        {
            Rebuild();
        }

        return planted;
    }

    /// <summary>One clearing stroke against the ground, forest rebuilt after it. See <see cref="TerrainBrush.EraseTrees"/>.</summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <returns>How many trees came up.</returns>
    public int EraseTrees(Vector2 center, float radius)
    {
        int removed = TerrainBrush.EraseTrees(_data, center, radius);
        if (removed > 0)
        {
            Rebuild();
        }

        return removed;
    }

    /// <summary>
    /// One hole stroke against the ground, mesh (and physics mesh) rebuilt after it: cells are cut out of
    /// the ground, or filled back in. See <see cref="TerrainBrush.ApplyHoles"/>.
    /// </summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="hole">True to cut holes, false to fill them back in.</param>
    /// <returns>How many cells changed state.</returns>
    public int ApplyHoles(Vector2 center, float radius, bool hole)
    {
        int changed = TerrainBrush.ApplyHoles(_data, center, radius, hole);
        if (changed > 0)
        {
            Rebuild();
        }

        return changed;
    }

    /// <summary>
    /// True when a world ray meets this terrain's ground, with where it met it and the normalised place
    /// of the meeting — the question an editor's brush asks every frame, answered with the same arithmetic
    /// a physics raycast uses, so the ground you sculpt is the ground you collide with.
    /// </summary>
    /// <param name="ray">The ray, in world space.</param>
    /// <param name="worldPoint">Receives where the ray met the surface, in world space.</param>
    /// <param name="uv">Receives the meeting's normalised place across the terrain, for a brush stroke.</param>
    /// <returns>True when the ray met the ground.</returns>
    public bool TryHit(Ray ray, out Vector3 worldPoint, out Vector2 uv)
    {
        worldPoint = Vector3.Zero;
        uv = Vector2.Zero;

        if (GameObject is null)
        {
            return false;
        }

        Matrix4x4 world = Transform.GetModelMatrix();
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverse))
        {
            return false;
        }

        Vector3 localOrigin = Vector3.Transform(ray.Origin, inverse);
        Vector3 localDirection = Vector3.TransformNormal(ray.Direction, inverse);

        if (!TerrainRaycast.TryHitLocal(_data, localOrigin, localDirection, out Vector3 localPoint, out float _))
        {
            return false;
        }

        worldPoint = Vector3.Transform(localPoint, world);
        uv = new Vector2(localPoint.X / _data.Size.X, localPoint.Z / _data.Size.Z);
        return true;
    }

    /// <summary>
    /// Sews one of this terrain's edges to the neighbour across it: both grounds take the averaged edge
    /// and both meshes rebuild. See <see cref="TerrainData.StitchEdge"/>. Nothing is remembered — move a
    /// terrain or sculpt an edge and the seam is whatever the heights say until it is stitched again.
    /// </summary>
    /// <param name="other">The terrain across the edge.</param>
    /// <param name="edge">Which of this terrain's edges faces it.</param>
    public void StitchTo(Terrain other, TerrainEdge edge)
    {
        ArgumentNullException.ThrowIfNull(other);
        _data.StitchEdge(other._data, edge);
        Rebuild();
        other.Rebuild();
    }

    /// <summary>
    /// The terrain standing across one of this terrain's edges, found geometrically: the candidate whose
    /// facing edge lies within a metre of this one's, overlapping along it. Terrains are compared by
    /// their corners and sizes, so both are assumed unturned — the ordinary way ground is tiled.
    /// </summary>
    /// <param name="candidates">Every terrain that might neighbour this one (this one may be among them).</param>
    /// <param name="edge">Which edge to look across.</param>
    /// <returns>The nearest abutting terrain, or null when the edge is the world's end.</returns>
    public Terrain? FindNeighbour(IEnumerable<Terrain> candidates, TerrainEdge edge)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (GameObject is null)
        {
            return null;
        }

        const float tolerance = 1f;
        Vector3 origin = GameObject.Transform.WorldPosition;
        float myMinX = origin.X;
        float myMaxX = origin.X + _data.Size.X;
        float myMinZ = origin.Z;
        float myMaxZ = origin.Z + _data.Size.Z;

        Terrain? best = null;
        float bestGap = tolerance;
        foreach (Terrain candidate in candidates)
        {
            if (ReferenceEquals(candidate, this) || candidate.GameObject is null)
            {
                continue;
            }

            Vector3 otherOrigin = candidate.GameObject.Transform.WorldPosition;
            float otherMinX = otherOrigin.X;
            float otherMaxX = otherOrigin.X + candidate._data.Size.X;
            float otherMinZ = otherOrigin.Z;
            float otherMaxZ = otherOrigin.Z + candidate._data.Size.Z;

            float gap;
            bool overlaps;
            switch (edge)
            {
                case TerrainEdge.Left:
                    gap = MathF.Abs(otherMaxX - myMinX);
                    overlaps = MathF.Min(myMaxZ, otherMaxZ) > MathF.Max(myMinZ, otherMinZ);
                    break;
                case TerrainEdge.Right:
                    gap = MathF.Abs(otherMinX - myMaxX);
                    overlaps = MathF.Min(myMaxZ, otherMaxZ) > MathF.Max(myMinZ, otherMinZ);
                    break;
                case TerrainEdge.Bottom:
                    gap = MathF.Abs(otherMaxZ - myMinZ);
                    overlaps = MathF.Min(myMaxX, otherMaxX) > MathF.Max(myMinX, otherMinX);
                    break;
                default:
                    gap = MathF.Abs(otherMinZ - myMaxZ);
                    overlaps = MathF.Min(myMaxX, otherMaxX) > MathF.Max(myMinX, otherMinX);
                    break;
            }

            if (overlaps && gap < bestGap)
            {
                bestGap = gap;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>One sculpting stroke against the ground, mesh rebuilt after it. See <see cref="TerrainBrush"/>.</summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="strength">Metres of lift for Raise/Lower; a 0..1 pull for Flatten/Smooth.</param>
    /// <param name="mode">What the stroke does.</param>
    /// <param name="flattenHeight">The height Flatten pulls toward, in metres.</param>
    /// <returns>How many grid points moved.</returns>
    public int Sculpt(Vector2 center, float radius, float strength, TerrainBrushMode mode, float flattenHeight = 0f)
    {
        int moved = TerrainBrush.ApplyHeight(_data, center, radius, strength, mode, flattenHeight);
        if (moved > 0)
        {
            Rebuild();
        }

        return moved;
    }

    /// <summary>One painting stroke against the ground, mesh rebuilt after it. See <see cref="TerrainBrush"/>.</summary>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="strength">How hard the stroke paints at its centre, 0..1.</param>
    /// <param name="layerIndex">The layer to paint with.</param>
    /// <returns>How many alphamap pixels changed.</returns>
    public int Paint(Vector2 center, float radius, float strength, int layerIndex)
    {
        int painted = TerrainBrush.ApplyPaint(_data, center, radius, strength, layerIndex);
        if (painted > 0)
        {
            Rebuild();
        }

        return painted;
    }
}
