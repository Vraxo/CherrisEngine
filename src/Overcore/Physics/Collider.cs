using BepuPhysics;
using BepuPhysics.Collidables;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// The shape physics sees, Unity's <c>Collider</c>: a box, a sphere or a capsule that bodies collide with and
/// queries can hit.
/// </summary>
/// <remarks>
/// <para>
/// A collider is a component of its own, not a property of <see cref="RigidBody"/>. On its own it is a fixed
/// shape in the world — something to stand on, or to walk into. Put a <see cref="RigidBody"/> on the same object
/// and the body takes the collider's shape and moves it, so the two are the two halves of one physical thing:
/// the body decides how it moves, the collider decides what it is.
/// </para>
/// <para>
/// A collider marked <see cref="IsTrigger"/> is a volume rather than a wall: nothing is blocked by it, and the
/// engine reports the overlaps through <see cref="Script.OnTriggerEnter(Collider)"/> and its siblings instead of
/// <see cref="Script.OnCollisionEnter(Collision)"/>.
/// </para>
/// </remarks>
public abstract class Collider : Component
{
    /// <summary>
    /// True when the collider is a volume to be reported rather than a surface to be hit. Contacts with a trigger
    /// are never solved, so whatever walks into it passes through; the overlap is reported instead.
    /// </summary>
    public bool IsTrigger
    {
        get => _isTrigger;
        set
        {
            if (_isTrigger == value)
            {
                return;
            }

            _isTrigger = value;
            RefreshShape();
        }
    }

    /// <summary>
    /// The coefficient of friction the surface has when something rubs along it. A pair of colliders combines
    /// theirs by taking the geometric mean, so the slippier of the two has the larger say.
    /// </summary>
    public float Friction { get; set; } = 0.5f;

    /// <summary>
    /// How much of its speed a surface gives back: zero is a dead stop, one is a lively bounce. Physics has no
    /// exact restitution term, so this drives how hard the solver pushes overlapping shapes apart — a little
    /// bounce, not a ball dropped on a trampoline.
    /// </summary>
    public float Bounciness { get; set; }

    /// <summary>The centre of the shape in the object's own space, for a shape that is not centred on the object.</summary>
    public virtual Vector3 Center
    {
        get => _center;
        set
        {
            _center = value;
            RefreshShape();
        }
    }

    /// <summary>True while the physics world knows about this collider.</summary>
    [HideInInspector]
    public bool IsRegistered { get; internal set; }

    /// <summary>The rigid body on the same object, if there is one.</summary>
    public RigidBody? AttachedRigidBody => GameObject?.GetComponent<RigidBody>();

    /// <summary>The smallest upright box, in world space, that holds the shape — Unity's <c>Collider.bounds</c>.</summary>
    public BoundingBox Bounds => ColliderGeometry.Bounds(ColliderGeometry.From(this));

    /// <summary>Where the shape's centre is in the world.</summary>
    public Vector3 WorldCenter
    {
        get
        {
            GetWorldPose(out Vector3 center, out _, out _);
            return center;
        }
    }

    private bool _isTrigger;
    private Vector3 _center;

    /// <summary>The fixed shape the physics world added for this collider, when the collider has no body to move it.</summary>
    internal StaticHandle? StaticBody { get; set; }

    /// <summary>The public name of the shape, for messages and the inspector.</summary>
    internal abstract string ShapeName { get; }

    /// <summary>Reads the shape's place in the world: where its centre is, how it is turned and how it is scaled.</summary>
    /// <param name="center">Receives the world centre, with <see cref="Center"/> applied.</param>
    /// <param name="rotation">Receives the world rotation.</param>
    /// <param name="scale">Receives the world scale.</param>
    public virtual void GetWorldPose(out Vector3 center, out Quaternion rotation, out Vector3 scale)
    {
        Transform transform = Transform;
        transform.GetWorldTransform(out Vector3 position, out rotation, out scale);
        center = position + Vector3.Transform(Center * scale, rotation);
    }

    /// <summary>True when a place in the world is inside the shape — Unity's <c>Collider.Contains</c>.</summary>
    /// <param name="point">The place to test, in world space.</param>
    public bool ContainsPoint(Vector3 point) => ColliderGeometry.Contains(ColliderGeometry.From(this), point);

    /// <inheritdoc />
    public override void Awake()
    {
        base.Awake();
        Register();
    }

    /// <inheritdoc />
    public override void OnEnable()
    {
        base.OnEnable();
        Register();
    }

    /// <inheritdoc />
    public override void OnDisable()
    {
        Unregister();
        base.OnDisable();
    }

    /// <inheritdoc />
    public override void OnDestroy()
    {
        Unregister();
        base.OnDestroy();
    }

    /// <summary>The collider's shape as the physics world wants it, scaled to the object's world size.</summary>
    /// <param name="shapes">The simulation's shape set, which takes ownership of the shape.</param>
    /// <param name="scale">The object's world scale.</param>
    internal abstract TypedIndex AddBepuShape(Shapes shapes, Vector3 scale);

    /// <summary>The shape's rotational inertia at a mass, which is what makes a long body harder to spin.</summary>
    /// <param name="mass">The body's mass.</param>
    /// <param name="scale">The object's world scale.</param>
    internal abstract BodyInertia ComputeInertia(float mass, Vector3 scale);

    /// <summary>Forgets the handles a clone would otherwise share with the original.</summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        StaticBody = null;
        IsRegistered = false;
    }

    /// <summary>Adds the collider to the physics world, which is what makes it collide and be found by queries.</summary>
    internal void Register()
    {
        if (IsRegistered || IsDestroyed)
        {
            return;
        }

        PhysicsSystem.Active?.RegisterCollider(this);
    }

    /// <summary>Takes the collider out of the physics world, and its fixed shape with it.</summary>
    internal void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        PhysicsSystem.Active?.UnregisterCollider(this);
    }

    /// <summary>
    /// Rebuilds the shape after a setting moved. A collider that is not in the world yet has nothing to rebuild.
    /// </summary>
    internal void RefreshShape()
    {
        if (!IsRegistered)
        {
            return;
        }

        PhysicsSystem.Active?.RefreshCollider(this);
    }

    /// <summary>The name of the object the collider is on, for messages.</summary>
    internal string OwnerName => GameObject?.Name ?? "Unassigned";

    /// <summary>The transform, checked, for shape maths that cannot do without it.</summary>
    internal Transform RequiredTransform => Transform;

    /// <inheritdoc />
    public override string ToString() => $"{ShapeName} on '{OwnerName}'";
}

/// <summary>A box-shaped collider, Unity's <c>BoxCollider</c>: the workhorse of walls and crates.</summary>
public sealed class BoxCollider : Collider
{
    /// <summary>The box's full size along each of the object's axes, before the object's own scale.</summary>
    public Vector3 Size
    {
        get => _size;
        set
        {
            _size = value;
            RefreshShape();
        }
    }

    private Vector3 _size = Vector3.One;

    internal override string ShapeName => "Box collider";

    internal override TypedIndex AddBepuShape(Shapes shapes, Vector3 scale)
    {
        Vector3 size = Vector3.Max(Vector3.Abs(Size) * Vector3.Abs(scale), new Vector3(1.0e-4f));
        return shapes.Add(new Box(size.X, size.Y, size.Z));
    }

    internal override BodyInertia ComputeInertia(float mass, Vector3 scale)
    {
        Vector3 size = Vector3.Max(Vector3.Abs(Size) * Vector3.Abs(scale), new Vector3(1.0e-4f));
        return new Box(size.X, size.Y, size.Z).ComputeInertia(mass);
    }
}

/// <summary>A ball-shaped collider, Unity's <c>SphereCollider</c>.</summary>
public sealed class SphereCollider : Collider
{
    /// <summary>The ball's radius, before the object's own scale; the largest axis of the scale is the one used.</summary>
    public float Radius
    {
        get => _radius;
        set
        {
            _radius = MathF.Max(value, 1.0e-4f);
            RefreshShape();
        }
    }

    private float _radius = 0.5f;

    internal override string ShapeName => "Sphere collider";

    internal override TypedIndex AddBepuShape(Shapes shapes, Vector3 scale)
    {
        return shapes.Add(new Sphere(MathF.Max(_radius * LargestAxis(scale), 1.0e-4f)));
    }

    internal override BodyInertia ComputeInertia(float mass, Vector3 scale)
    {
        return new Sphere(MathF.Max(_radius * LargestAxis(scale), 1.0e-4f)).ComputeInertia(mass);
    }

    private static float LargestAxis(Vector3 scale) =>
        MathF.Max(Vector3.Abs(scale).X, MathF.Max(Vector3.Abs(scale).Y, Vector3.Abs(scale).Z));
}

/// <summary>
/// A capsule-shaped collider, Unity's <c>CapsuleCollider</c>: a cylinder with rounded ends standing on the
/// object's own +Y, the shape a character or a barrel has.
/// </summary>
public sealed class CapsuleCollider : Collider
{
    /// <summary>The radius of the cylinder and of its rounded ends, before the object's own scale.</summary>
    public float Radius
    {
        get => _radius;
        set
        {
            _radius = MathF.Max(value, 1.0e-4f);
            RefreshShape();
        }
    }

    /// <summary>The total height from end to end, caps included, before the object's own scale.</summary>
    public float Height
    {
        get => _height;
        set
        {
            _height = MathF.Max(value, 1.0e-4f);
            RefreshShape();
        }
    }

    private float _radius = 0.5f;
    private float _height = 2.0f;

    internal override string ShapeName => "Capsule collider";

    internal override TypedIndex AddBepuShape(Shapes shapes, Vector3 scale)
    {
        GetCapsule(scale, out float radius, out float length);
        return shapes.Add(new Capsule(radius, length));
    }

    internal override BodyInertia ComputeInertia(float mass, Vector3 scale)
    {
        GetCapsule(scale, out float radius, out float length);
        return new Capsule(radius, length).ComputeInertia(mass);
    }

    private void GetCapsule(Vector3 scale, out float radius, out float length)
    {
        Vector3 absoluteScale = Vector3.Abs(scale);
        radius = MathF.Max(_radius * MathF.Max(absoluteScale.X, absoluteScale.Z), 1.0e-4f);
        float height = MathF.Max(_height * absoluteScale.Y, radius * 2.0f);
        length = MathF.Max(height - (radius * 2.0f), 0.0f);
    }
}
