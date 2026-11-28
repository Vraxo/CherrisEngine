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

    private static readonly Mesh EmptyMesh = new([], []);

    private TerrainData _data = new();
    private int _builtHeightVersion = -1;
    private int _builtAlphamapVersion = -1;
    private int _builtHolesVersion = -1;
    private int _builtTreesVersion = -1;
    private Func<string, Mesh?>? _treeMeshResolver;
    private Material? _treeMaterial;

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

        Mesh = TerrainMesher.Build(_data);
        _builtHeightVersion = _data.Version;
        _builtAlphamapVersion = _data.AlphamapVersion;
        _builtHolesVersion = _data.HolesVersion;

        MeshRenderer renderer = GameObject.GetComponent<MeshRenderer>()
            ?? GameObject.AddComponent(new MeshRenderer
            {
                Mesh = Mesh,
                MeshName = "Terrain",
                Material = Material ?? new Material(null!, "White")
            });

        renderer.Mesh = Mesh;
        if (Material is not null)
        {
            renderer.Material = Material;
        }
        else if (renderer.Material is null)
        {
            renderer.Material = new Material(null!, "White");
        }

        TreeMesh = _treeMeshResolver is null ? null : TerrainForestMesher.Build(_data, _treeMeshResolver);
        _builtTreesVersion = _data.TreesVersion;
        EnsureTreeRenderer();

        // The collider beside this terrain holds a physics copy of the same ground; it is told to remake it.
        GameObject.GetComponent<TerrainCollider>()?.RefreshShape();
    }

    /// <summary>
    /// Keeps the forest's child object in step with <see cref="TreeMesh"/>: created on first need, emptied
    /// (and silenced) when the forest is gone. The child wears its own plain material, so the ground's
    /// texture never smears across the trees and the trees never take the ground's splat branch.
    /// </summary>
    private void EnsureTreeRenderer()
    {
        MeshRenderer? renderer = null;
        foreach (Transform child in GameObject.Transform.Children)
        {
            if (child.GameObject.GetComponent<MeshRenderer>() is { MeshName: TreeMeshName } found)
            {
                renderer = found;
                break;
            }
        }

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

    /// <summary>Rebuilds the mesh if the data has been edited since the last build. The scene calls this once a frame.</summary>
    internal void RefreshIfDirty()
    {
        if (_builtHeightVersion != _data.Version
            || _builtAlphamapVersion != _data.AlphamapVersion
            || _builtHolesVersion != _data.HolesVersion
            || _builtTreesVersion != _data.TreesVersion)
        {
            Rebuild();
        }
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
