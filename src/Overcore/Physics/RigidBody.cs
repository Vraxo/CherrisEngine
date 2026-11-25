using BepuPhysics;
using BepuPhysics.Collidables;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Core.Physics;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>The three shapes a rigid body can fall back to when its object has no collider of its own.</summary>
public enum ColliderType
{
    Box,
    Sphere,
    Capsule
}

/// <summary>How a force is meant, Unity's <c>ForceMode</c>.</summary>
public enum ForceMode
{
    /// <summary>A steady push, applied for the whole step.</summary>
    Force,

    /// <summary>An acceleration rather than a force: mass does not divide it.</summary>
    Acceleration,

    /// <summary>A kick, applied once.</summary>
    Impulse,

    /// <summary>A change in velocity, applied once, ignoring mass.</summary>
    VelocityChange
}

/// <summary>
/// What makes an object move under physics, Unity's <c>RigidBody</c>: mass, the pull of gravity, and the forces a
/// script puts on it.
/// </summary>
/// <remarks>
/// <para>
/// The body takes its shape from the <see cref="Collider"/> on the same object, and moves it: the collider says what
/// the object is, the body says how it moves. An object with no collider falls back to the size of its mesh, which
/// is how scenes built before colliders existed keep working, and an object with a body but no collider reports no
/// collision events — there is no collider to name in them.
/// </para>
/// <para>
/// Forces added with <see cref="AddForce(Vector3, ForceMode)"/> collect until the next fixed step and are applied
/// then, which is Unity's rule and what makes a push independent of how fast frames are drawn. The older
/// <see cref="ApplyForce(Vector3)"/> and <see cref="ApplyImpulse(Vector3)"/> change the velocity on the spot.
/// </para>
/// </remarks>
public class RigidBody : Script
{
    /// <summary>
    /// The shape to use when the object has no <see cref="Collider"/>. With one, the collider decides and this is
    /// ignored.
    /// </summary>
    public ColliderType Shape
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Rebuild();
        }
    } = ColliderType.Box;

    /// <summary>The body's mass, in kilograms. Heavier bodies take more force to move and push lighter ones aside.</summary>
    public float Mass
    {
        get => _mass;
        set
        {
            float mass = MathF.Max(value, 1.0e-4f);

            if (MathF.Abs(mass - _mass) < 1.0e-6f)
            {
                return;
            }

            _mass = mass;
            Rebuild();
        }
    }

    /// <summary>
    /// True when the body never moves: the ground, a wall, a crate that was never meant to fall. A fixed body holds
    /// its shape where the object is and takes no forces.
    /// </summary>
    public bool IsStatic
    {
        get => _isStatic;
        set
        {
            if (_isStatic == value)
            {
                return;
            }

            _isStatic = value;
            Rebuild();
        }
    }

    /// <summary>
    /// True when the body moves only where a script puts it: it pushes others aside but nothing pushes it. Kinematic
    /// bodies have no gravity and take no forces; move one with <see cref="MovePosition"/>, <see cref="MoveRotation"/>
    /// or by setting <see cref="Velocity"/>.
    /// </summary>
    public bool IsKinematic
    {
        get => _isKinematic;
        set
        {
            if (_isKinematic == value)
            {
                return;
            }

            _isKinematic = value;
            Rebuild();
        }
    }

    /// <summary>True when gravity pulls the body. Switch it off for something that floats or flies.</summary>
    public bool UseGravity { get; set; } = true;

    /// <summary>
    /// The coefficient of friction this body's surface has, used when the object has no <see cref="Collider"/> to
    /// say so itself.
    /// </summary>
    public float Friction { get; set; } = 0.5f;

    /// <summary>How much of its speed this body's surface gives back, when there is no collider to say so.</summary>
    public float Bounciness { get; set; } = 0.5f;

    /// <summary>The body's speed, in world units per second. Reading it is cheap; setting it stops the body where it is.</summary>
    public Vector3 Velocity
    {
        get => GetVelocity();
        set => SetVelocity(value);
    }

    /// <summary>The body's spin, in radians per second about each world axis.</summary>
    public Vector3 AngularVelocity
    {
        get
        {
            if (GetBody() is not { } body)
            {
                return Vector3.Zero;
            }

            return body.Velocity.Angular;
        }
        set
        {
            if (GetBody() is not { } body)
            {
                return;
            }

            body.Velocity.Angular = value;
            body.Awake = true;
        }
    }

    /// <summary>The simulation's handle for the body, when it has one. Null for a fixed body.</summary>
    [HideInInspector]
    public BodyHandle? BepuBodyHandle { get; internal set; }

    /// <summary>The simulation's handle for a fixed body's shape, when it has one.</summary>
    [HideInInspector]
    public StaticHandle? BepuStaticHandle { get; internal set; }

    private PhysicsSystem? _physicsSystem;
    private Vector3 _lastScale;
    private float _mass = 1.0f;
    private bool _isStatic;
    private bool _isKinematic;
    private TypedIndex? _shapeIndex;

    // Where the body sits relative to the object: the collider's centre can be off the object's origin.
    private Vector3 _bodyOffset;

    private Vector3 _pendingForce;
    private Vector3 _pendingAcceleration;
    private Vector3 _pendingImpulse;
    private Vector3 _pendingVelocityChange;

    internal void Initialize(PhysicsSystem physicsSystem)
    {
        _physicsSystem = physicsSystem;
    }

    /// <summary>True once the body exists in the simulation.</summary>
    public bool IsInitialized => BepuBodyHandle.HasValue || BepuStaticHandle.HasValue;

    public override void Start()
    {
        if (_physicsSystem is null)
        {
            return;
        }

        CreateAndRegisterBepuBody();
    }

    public override void Update(float deltaTime)
    {
        if (GameObject is not null && GameObject.Transform.Scale != _lastScale)
        {
            Rebuild();
            return;
        }

        if (IsKinematic)
        {
            // A kinematic body goes where the object is put; the world never moves it.
            SyncBodyToTransform();
        }
    }

    public override void FixedUpdate()
    {
        if (IsStatic || IsKinematic)
        {
            return;
        }

        // The frame's forces are spent here, all at once, so a push does not depend on how many frames went by.
        Vector3 force = _pendingForce;
        Vector3 acceleration = _pendingAcceleration;
        Vector3 impulse = _pendingImpulse;
        Vector3 velocityChange = _pendingVelocityChange;

        _pendingForce = Vector3.Zero;
        _pendingAcceleration = Vector3.Zero;
        _pendingImpulse = Vector3.Zero;
        _pendingVelocityChange = Vector3.Zero;

        if (UseGravity)
        {
            // Gravity is applied by the integrator; nothing to do.
        }
        else
        {
            force -= Physics.Gravity * Mass;
        }

        if (GetBody() is not { } body)
        {
            return;
        }

        Vector3 change = (force / Mass * Time.FixedDeltaTime) + (acceleration * Time.FixedDeltaTime);
        body.Velocity.Linear += change + (impulse / Mass) + velocityChange;
        body.Awake = true;
    }

    /// <summary>
    /// Adds a force for the next fixed step, Unity's <c>RigidBody.AddForce</c>. Several calls in one frame add up.
    /// </summary>
    /// <param name="force">The force, in newtons (or the matching unit for the mode).</param>
    /// <param name="mode">How the number is meant.</param>
    public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
    {
        switch (mode)
        {
            case ForceMode.Acceleration:
                _pendingAcceleration += force;
                break;
            case ForceMode.Impulse:
                _pendingImpulse += force;
                break;
            case ForceMode.VelocityChange:
                _pendingVelocityChange += force;
                break;
            default:
                _pendingForce += force;
                break;
        }
    }

    /// <summary>Adds a force at a place other than the centre of mass, which also spins the body.</summary>
    /// <param name="force">The force, in newtons.</param>
    /// <param name="position">Where it is applied, in world space.</param>
    /// <param name="mode">How the number is meant.</param>
    public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
    {
        AddForce(force, mode);

        if (GetBody() is not { } body)
        {
            return;
        }

        Vector3 lever = position - body.Pose.Position;
        Vector3 torque = Vector3.Cross(lever, force);
        float scale = mode is ForceMode.Impulse or ForceMode.VelocityChange ? 1.0f : Time.FixedDeltaTime;
        body.Velocity.Angular += torque / Mass * scale;
        body.Awake = true;
    }

    /// <summary>Adds a turning force for the next fixed step, Unity's <c>RigidBody.AddTorque</c>.</summary>
    /// <param name="torque">The torque, about each world axis.</param>
    /// <param name="mode">How the number is meant.</param>
    public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
    {
        if (GetBody() is not { } body)
        {
            return;
        }

        float scale = mode is ForceMode.Impulse or ForceMode.VelocityChange ? 1.0f : Time.FixedDeltaTime;
        body.Velocity.Angular += torque / Mass * scale;
        body.Awake = true;
    }

    /// <summary>
    /// Changes the body's velocity at once. Kept for scenes written before <see cref="AddForce(Vector3, ForceMode)"/>:
    /// a force spread over the step and an impulse are not the same thing, and this is the impulse.
    /// </summary>
    /// <param name="force">The impulse, in newton-seconds.</param>
    public void ApplyForce(Vector3 force) => ApplyImpulse(force);

    /// <summary>Changes the body's velocity at once by an impulse over its mass.</summary>
    /// <param name="impulse">The impulse, in newton-seconds.</param>
    public void ApplyImpulse(Vector3 impulse)
    {
        if (IsStatic || IsKinematic || GetBody() is not { } body)
        {
            return;
        }

        body.Velocity.Linear += impulse / Mass;
        body.Awake = true;
    }

    /// <summary>Sets the body's speed, in world units per second.</summary>
    /// <param name="velocity">The speed to give it.</param>
    public void SetVelocity(Vector3 velocity)
    {
        if (IsStatic || IsKinematic || GetBody() is not { } body)
        {
            return;
        }

        body.Velocity.Linear = velocity;
        body.Awake = true;
    }

    /// <summary>The body's speed, in world units per second.</summary>
    public Vector3 GetVelocity()
    {
        if (GetBody() is not { } body)
        {
            return Vector3.Zero;
        }

        return body.Velocity.Linear;
    }

    /// <summary>Puts a kinematic body somewhere new; a dynamic body is simply teleported.</summary>
    /// <param name="position">Where the object should be, in world space.</param>
    public void MovePosition(Vector3 position) => Teleport(position);

    /// <summary>Turns a kinematic body; a dynamic body is simply turned where it stands.</summary>
    /// <param name="rotation">How the object should be turned, in world space.</param>
    public void MoveRotation(Quaternion rotation) => Teleport(GameObject?.Transform.WorldPosition ?? Vector3.Zero, rotation);

    /// <summary>Puts the body, and the object with it, at a place and angle, without letting physics push back.</summary>
    /// <param name="position">Where the object should be, in world space.</param>
    /// <param name="rotation">How it should be turned, in world space; left alone when omitted.</param>
    public void Teleport(Vector3 position, Quaternion? rotation = null)
    {
        if (GameObject is not { } gameObject)
        {
            return;
        }

        Quaternion orientation = rotation ?? gameObject.Transform.WorldRotation;

        if (GetBody() is { } body)
        {
            body.Pose.Position = position + _bodyOffset;
            body.Pose.Orientation = orientation;
            body.Velocity.Linear = Vector3.Zero;
            body.Velocity.Angular = Vector3.Zero;
            body.Awake = true;
        }

        gameObject.Transform.WorldPosition = position - _bodyOffset;
        gameObject.Transform.WorldRotation = orientation;
    }

    /// <summary>Rebuilds the body in the simulation, after its shape or its kind of motion changed.</summary>
    public void Rebuild()
    {
        if (_physicsSystem is null)
        {
            return;
        }

        RemovePhysicsBody();
        CreateAndRegisterBepuBody();
    }

    /// <summary>Copies a body's place in the world back onto the object it belongs to.</summary>
    /// <param name="position">The body's position.</param>
    /// <param name="orientation">The body's rotation.</param>
    internal void ApplyBodyPose(Vector3 position, Quaternion orientation)
    {
        if (GameObject is not { } gameObject)
        {
            return;
        }

        gameObject.Transform.WorldPosition = position - _bodyOffset;
        gameObject.Transform.WorldRotation = orientation;
    }

    public override void OnDestroy()
    {
        RemovePhysicsBody();
        base.OnDestroy();
    }

    /// <summary>Forgets the handles a clone would otherwise share with the original.</summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        BepuBodyHandle = null;
        BepuStaticHandle = null;
        _shapeIndex = null;
        _physicsSystem = null;
        _pendingForce = Vector3.Zero;
        _pendingAcceleration = Vector3.Zero;
        _pendingImpulse = Vector3.Zero;
        _pendingVelocityChange = Vector3.Zero;
    }

    private BodyReference? GetBody()
    {
        if (_physicsSystem?.Simulation is not { } simulation || BepuBodyHandle is not { } handle)
        {
            return null;
        }

        BodyReference body = simulation.Bodies.GetBodyReference(handle);
        return body.Exists ? body : null;
    }

    private void RemovePhysicsBody()
    {
        if (_physicsSystem?.Simulation is not { } simulation)
        {
            BepuBodyHandle = null;
            BepuStaticHandle = null;
            return;
        }

        if (BepuBodyHandle is { } bodyHandle)
        {
            if (simulation.Bodies.BodyExists(bodyHandle))
            {
                simulation.Bodies.Remove(bodyHandle);
            }

            _physicsSystem.UnregisterBody(this);
            BepuBodyHandle = null;
        }

        if (BepuStaticHandle is { } staticHandle)
        {
            if (simulation.Statics.StaticExists(staticHandle))
            {
                simulation.Statics.Remove(staticHandle);
            }

            _physicsSystem.UnregisterBody(this);
            BepuStaticHandle = null;
        }

        if (_shapeIndex is { } shape)
        {
            simulation.Shapes.RemoveAndDispose(shape, _physicsSystem.BufferPool);
            _shapeIndex = null;
        }
    }

    private void SyncBodyToTransform()
    {
        if (GetBody() is not { } body || GameObject is not { } gameObject)
        {
            return;
        }

        body.Pose.Position = gameObject.Transform.WorldPosition + _bodyOffset;
        body.Pose.Orientation = gameObject.Transform.WorldRotation;
    }

    private void CreateAndRegisterBepuBody()
    {
        if (_physicsSystem?.Simulation is not { } simulation || GameObject is not { } gameObject)
        {
            return;
        }

        _lastScale = gameObject.Transform.Scale;

        Collider? collider = gameObject.GetComponent<Collider>();
        TypedIndex shapeIndex;
        BodyInertia inertia;
        Vector3 bodyPosition;
        Quaternion bodyRotation;

        if (collider is not null)
        {
            collider.GetWorldPose(out bodyPosition, out bodyRotation, out Vector3 scale);
            _bodyOffset = bodyPosition - gameObject.Transform.WorldPosition;
            shapeIndex = collider.AddBepuShape(simulation.Shapes, scale);
            inertia = collider.ComputeInertia(Mass, scale);
        }
        else if (gameObject.GetComponent<MeshRenderer>() is { Mesh: { } mesh })
        {
            // No collider: the object is as big as its mesh, which keeps older scenes working.
            BoundingBox aabb = mesh.AABB;
            Vector3 size = (aabb.Max - aabb.Min) * Vector3.Abs(gameObject.Transform.Scale);
            (shapeIndex, inertia) = AddShapeTo(simulation, size);
            bodyPosition = gameObject.Transform.WorldPosition;
            bodyRotation = gameObject.Transform.WorldRotation;
            _bodyOffset = Vector3.Zero;
        }
        else
        {
            Logger.Warning(
                $"[RigidBody] '{gameObject.Name}' has neither a collider nor a mesh, so it has no shape to move.");
            return;
        }

        _shapeIndex = shapeIndex;

        RigidPose pose = new(bodyPosition, bodyRotation);
        CollidableDescription collidable = new(shapeIndex, 0.1f);
        BodyActivityDescription activity = new(0.01f);

        if (IsStatic)
        {
            BepuStaticHandle = simulation.Statics.Add(new StaticDescription(pose, shapeIndex));
            BepuBodyHandle = null;
        }
        else if (IsKinematic)
        {
            BepuBodyHandle = simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, collidable, activity));
            BepuStaticHandle = null;
        }
        else
        {
            BepuBodyHandle = simulation.Bodies.Add(BodyDescription.CreateDynamic(pose, inertia, collidable, activity));
            BepuStaticHandle = null;
        }

        _physicsSystem.RegisterBody(this);
    }

    private (TypedIndex Shape, BodyInertia Inertia) AddShapeTo(Simulation simulation, Vector3 size)
    {
        Vector3 safeSize = Vector3.Max(size, new Vector3(1.0e-4f));

        switch (Shape)
        {
            case ColliderType.Sphere:
            {
                float radius = (safeSize.X + safeSize.Y + safeSize.Z) / 6.0f;
                Sphere sphere = new(radius);
                return (simulation.Shapes.Add(sphere), sphere.ComputeInertia(Mass));
            }

            case ColliderType.Capsule:
            {
                float radius = MathF.Max(safeSize.X, safeSize.Z) / 2.0f;
                float length = MathF.Max(0.0f, safeSize.Y - (2.0f * radius));
                Capsule capsule = new(radius, length);
                return (simulation.Shapes.Add(capsule), capsule.ComputeInertia(Mass));
            }

            default:
            {
                Box box = new(safeSize.X, safeSize.Y, safeSize.Z);
                return (simulation.Shapes.Add(box), box.ComputeInertia(Mass));
            }
        }
    }
}
