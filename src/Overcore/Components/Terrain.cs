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
/// controllers.
/// </para>
/// <para>
/// The mesh is rebuilt when the data changes: eagerly by <see cref="Sculpt"/> and <see cref="Paint"/>, and
/// once a frame by the scene for edits made straight against <see cref="TerrainData"/> (a script raising
/// a hill, say), which the data's version counters report.
/// </para>
/// </remarks>
public class Terrain : Component
{
    private TerrainData _data = new();
    private int _builtHeightVersion = -1;
    private int _builtAlphamapVersion = -1;
    private int _builtHolesVersion = -1;

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

        // The collider beside this terrain holds a physics copy of the same ground; it is told to remake it.
        GameObject.GetComponent<TerrainCollider>()?.RefreshShape();
    }

    /// <summary>Rebuilds the mesh if the data has been edited since the last build. The scene calls this once a frame.</summary>
    internal void RefreshIfDirty()
    {
        if (_builtHeightVersion != _data.Version
            || _builtAlphamapVersion != _data.AlphamapVersion
            || _builtHolesVersion != _data.HolesVersion)
        {
            Rebuild();
        }
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
