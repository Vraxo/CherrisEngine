using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Overcore.Terrain;
using Overcore.Utils;
using System.Numerics;
using BepuMesh = BepuPhysics.Collidables.Mesh;
using BepuTriangle = BepuPhysics.Collidables.Triangle;

namespace Overcore.Components;

/// <summary>
/// The solid half of a <see cref="Terrain"/>, Unity's <c>TerrainCollider</c>: the heightfield as a shape
/// bodies rest on, rays hit and character controllers walk over.
/// </summary>
/// <remarks>
/// <para>
/// Place it on the same object as the <see cref="Terrain"/> and it takes that component's data; given data
/// of its own it stands alone. Dynamic bodies meet a Bepu triangle mesh built from the heightfield, while
/// the engine's own queries (rays, sphere and capsule casts, overlaps) test the heightfield analytically in
/// <see cref="Overcore.Core.Physics.ColliderGeometry"/> — the mesh is for the solver, the arithmetic is for
/// questions.
/// </para>
/// <para>
/// A terrain is ground, not a vehicle: putting a dynamic <see cref="RigidBody"/> on the same object is not
/// supported, and the inertia below is the honest box approximation that case would get.
/// </para>
/// </remarks>
public sealed class TerrainCollider : Collider
{
    private TerrainData? _data;

    /// <summary>
    /// The ground this collider is solid for: its own data when set, otherwise the data of the
    /// <see cref="Terrain"/> on the same object.
    /// </summary>
    [HideInInspector]
    public TerrainData? Data
    {
        get => _data ?? GameObject?.GetComponent<Terrain>()?.TerrainData;
        set
        {
            _data = value;
            RefreshShape();
        }
    }

    internal override string ShapeName => "Terrain collider";

    /// <summary>
    /// The terrain's place in the world: its corner at the object's position, not its centre. The base
    /// collider pose centres shapes on the object; a heightfield's origin is its corner, as Unity's is.
    /// </summary>
    public override void GetWorldPose(out Vector3 center, out Quaternion rotation, out Vector3 scale)
    {
        Transform transform = Transform;
        transform.GetWorldTransform(out Vector3 position, out rotation, out scale);
        center = position;
    }

    /// <summary>
    /// A terrain has no centred shape to offset: its centre is its corner, so the base <see cref="Collider.Center"/>
    /// does not apply and is not offered.
    /// </summary>
    public override Vector3 Center
    {
        get => Vector3.Zero;
        set { }
    }

    internal override TypedIndex AddBepuShape(Shapes shapes, Vector3 scale)
    {
        // The two-argument path has no pool to build a triangle mesh from, and only a rigid body asks through
        // it — a case this collider does not support. The box it gets is the footprint, honestly labelled.
        return shapes.Add(new Box(
            MathF.Max(MathF.Abs((Data?.Size.X ?? 1f) * scale.X), 1.0e-4f),
            MathF.Max(MathF.Abs((Data?.Size.Y ?? 1f) * scale.Y), 1.0e-4f),
            MathF.Max(MathF.Abs((Data?.Size.Z ?? 1f) * scale.Z), 1.0e-4f)));
    }

    internal override TypedIndex AddBepuShape(Shapes shapes, Vector3 scale, BufferPool pool)
    {
        if (Data is not { } data)
        {
            return AddBepuShape(shapes, scale);
        }

        List<(Vector3 A, Vector3 B, Vector3 C)> triangles = TerrainMesher.BuildTriangles(data);
        BepuTriangle[] bepuTriangles = new BepuTriangle[triangles.Count];
        for (int index = 0; index < triangles.Count; index++)
        {
            (Vector3 a, Vector3 b, Vector3 c) = triangles[index];
            bepuTriangles[index] = new BepuTriangle(a, b, c);
        }

        // The static's pose already carries the object's place and turn, so the mesh is built in the
        // terrain's own metres and the pose's scale is applied by the shape itself.
        return shapes.Add(new BepuMesh(bepuTriangles, Vector3.Abs(scale), pool));
    }

    internal override BodyInertia ComputeInertia(float mass, Vector3 scale)
    {
        Vector3 size = Vector3.Max(
            Vector3.Abs((Data?.Size ?? Vector3.One) * scale), new Vector3(1.0e-4f));
        return new Box(size.X, size.Y, size.Z).ComputeInertia(mass);
    }
}
