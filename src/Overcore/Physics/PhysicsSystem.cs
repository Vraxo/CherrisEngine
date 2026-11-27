using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Overcore.Components;
using Overcore.Core.Logging;
using System.Numerics;
using System.Runtime.CompilerServices;
using BoundingBox = Overcore.Components.BoundingBox;

namespace Overcore.Core.Physics;

/// <summary>
/// The world bodies move in, stepped at a fixed rate: the simulation itself, the colliders it knows about, and the
/// collision and trigger events it hands to scripts.
/// </summary>
/// <remarks>
/// <para>
/// A frame is cut into fixed steps of <see cref="FixedTimeStep"/>, usually 1/60 s, and each step runs
/// <see cref="FixedStep"/> (where scripts get their <c>FixedUpdate</c>), then simulates, so a slow frame runs the
/// step more than once and a fast one not at all. Bodies' transforms are copied back once the steps are done.
/// </para>
/// <para>
/// The world a frame runs against is <see cref="Active"/>: one per running scene, set when the scene starts, and
/// the thing <see cref="Physics"/> and every <see cref="Collider"/> reach for.
/// </para>
/// </remarks>
public partial class PhysicsSystem : IDisposable
{
    /// <summary>The default pull of gravity, in metres per second squared, straight down.</summary>
    public static readonly Vector3 DefaultGravity = new(0.0f, -9.81f, 0.0f);

    /// <summary>
    /// The world scripts query and register with — the last one started. Null while nothing is running, which is
    /// why every query answers "nothing" rather than throwing.
    /// </summary>
    public static PhysicsSystem? Active { get; internal set; }

    /// <summary>The live simulation, for code that needs to reach past this wrapper.</summary>
    public Simulation Simulation { get; private set; }

    /// <summary>How long one step is, in seconds. Raise it for a coarser, cheaper world; lower it for a finer one.</summary>
    public float FixedTimeStep
    {
        get => _fixedTimeStep;
        set => _fixedTimeStep = Math.Clamp(value, 1.0e-4f, 1.0f);
    }

    /// <summary>Gravity, in metres per second squared: the acceleration every dynamic body falls at.</summary>
    public Vector3 Gravity
    {
        get => Settings.Gravity;
        set => Settings.Gravity = value;
    }

    /// <summary>
    /// Raised before every fixed step is simulated. The scene ticks <c>FixedUpdate</c> here, and coroutines waiting
    /// on <see cref="WaitForFixedUpdate"/> carry on here.
    /// </summary>
    public event Action<float>? FixedStep;

    /// <summary>Every collider the world knows about, fixed or held by a body.</summary>
    public IReadOnlyList<Collider> Colliders => _colliders;


    internal SimulationState Settings { get; }

    /// <summary>The pool shapes and buffers come from, for a body that has to give one back.</summary>
    internal BufferPool BufferPool => _bufferPool;

    private readonly BufferPool _bufferPool;
    private readonly ThreadDispatcher _threadDispatcher;
    private readonly Dictionary<BodyHandle, RigidBody> _bodyHandleToComponent = [];
    private readonly Dictionary<StaticHandle, RigidBody> _staticHandleToComponent = [];
    private readonly Dictionary<int, Collider> _bodyColliders = [];
    private readonly Dictionary<int, Collider> _staticColliders = [];
    private readonly Dictionary<int, TypedIndex> _staticShapes = [];
    private readonly List<Collider> _colliders = [];
    private readonly List<Collider> _triggers = [];

    private readonly ContactBuffers _contacts = new();
    // Swapped at the end of every step, so yesterday's pairs become the ones to compare against.
    private Dictionary<long, CollisionPair> _activeCollisions = [];
    private Dictionary<long, CollisionPair> _currentCollisions = [];
    private Dictionary<long, TriggerPair> _activeTriggers = [];
    private Dictionary<long, TriggerPair> _currentTriggers = [];

    private float _fixedTimeStep = 1.0f / 60.0f;
    private float _accumulator;

    private const float MaxDeltaTime = 0.25f;

    public PhysicsSystem()
    {
        _bufferPool = new BufferPool();

        int threadCount = Math.Max(1, Environment.ProcessorCount - 1);
        _threadDispatcher = new ThreadDispatcher(threadCount);

        Settings = new SimulationState(Physics.Gravity);

        Simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(this),
            new PoseIntegratorCallbacks(Settings),
            new(8, 1));
    }

    /// <summary>Makes this the world scripts reach for. The scene does this when it starts.</summary>
    public void MakeActive() => Active = this;

    /// <summary>Steps the world by the time that has passed, and reports what touched what.</summary>
    /// <param name="deltaTime">Seconds since the previous frame.</param>
    public void Update(float deltaTime)
    {
        RefreshStaticPoses();

        float effectiveDeltaTime = Math.Min(MathF.Max(deltaTime, 0.0f), MaxDeltaTime);
        _accumulator += effectiveDeltaTime;

        float step = FixedTimeStep;
        int steps = 0;

        while (_accumulator >= step)
        {
            FixedStep?.Invoke(step);
            Simulation.Timestep(step, _threadDispatcher);
            Time.AdvanceFixed(step);

            _accumulator -= step;
            steps++;
        }

        if (steps == 0)
        {
            return;
        }

        SyncTransforms();
        DispatchCollisions();
        DispatchTriggers();
        _contacts.Clear();
    }

    /// <summary>
    /// Adds a collider to the world. One without a <see cref="RigidBody"/> becomes a fixed shape here; one with a
    /// body is given its shape by that body, unless it is a trigger, which is a volume and never a surface.
    /// </summary>
    /// <param name="collider">The collider to add.</param>
    public void RegisterCollider(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);

        if (collider.IsRegistered || collider.IsDestroyed || _colliders.Contains(collider))
        {
            return;
        }

        _colliders.Add(collider);
        collider.IsRegistered = true;

        if (collider.IsTrigger && collider is not CharacterController)
        {
            _triggers.Add(collider);
        }

        if (collider.AttachedRigidBody is { } body)
        {
            // A body that is already running has to be told its shape changed; one that has not started yet
            // takes the collider's shape when it does.
            if (body.HasStarted)
            {
                body.Rebuild();
            }

            return;
        }

        // Controller motion is constrained by Move, but its static solver shape still blocks incoming rigid bodies.
        if (!collider.IsTrigger && (collider is not CharacterController controller || controller.DetectCollisions))
        {
            AddStaticShape(collider);
        }
    }

    /// <summary>Takes a collider, and any fixed shape made for it, out of the world.</summary>
    /// <param name="collider">The collider to remove.</param>
    public void UnregisterCollider(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);

        _colliders.Remove(collider);
        _triggers.Remove(collider);
        collider.IsRegistered = false;

        if (collider.StaticBody is { } handle)
        {
            if (Simulation.Statics.StaticExists(handle))
            {
                Simulation.Statics.Remove(handle);
            }

            if (_staticShapes.Remove(handle.Value, out TypedIndex shape))
            {
                Simulation.Shapes.RemoveAndDispose(shape, _bufferPool);
            }

            _staticColliders.Remove(handle.Value);
            collider.StaticBody = null;
        }

        RemoveColliderFromBodies(collider);
    }

    /// <summary>
    /// Rebuilds a collider's shape after a setting moved: a different size, a different centre, or a switch to or
    /// from being a trigger. A body on the same object is rebuilt with it.
    /// </summary>
    /// <param name="collider">The collider whose shape moved.</param>
    public void RefreshCollider(Collider collider)
    {
        if (!collider.IsRegistered)
        {
            return;
        }

        UnregisterCollider(collider);
        RegisterCollider(collider);
    }

    /// <summary>Remembers a rigid body's collidable so contacts and queries can find the object behind it.</summary>
    /// <param name="bodyComponent">The body that was just added to the simulation.</param>
    public void RegisterBody(RigidBody bodyComponent)
    {
        ArgumentNullException.ThrowIfNull(bodyComponent);

        Collider? collider = bodyComponent.GameObject?.GetComponent<Collider>();

        if (bodyComponent.BepuBodyHandle is { } bodyHandle)
        {
            _bodyHandleToComponent[bodyHandle] = bodyComponent;

            if (collider is not null)
            {
                _bodyColliders[bodyHandle.Value] = collider;
            }
        }
        else if (bodyComponent.BepuStaticHandle is { } staticHandle)
        {
            _staticHandleToComponent[staticHandle] = bodyComponent;

            if (collider is not null)
            {
                _staticColliders[staticHandle.Value] = collider;
            }
        }
    }

    /// <summary>Forgets a rigid body's collidable, because the body is leaving the simulation.</summary>
    /// <param name="bodyComponent">The body that is going away.</param>
    public void UnregisterBody(RigidBody bodyComponent)
    {
        ArgumentNullException.ThrowIfNull(bodyComponent);

        if (bodyComponent.BepuBodyHandle is { } bodyHandle)
        {
            _bodyHandleToComponent.Remove(bodyHandle);
            _bodyColliders.Remove(bodyHandle.Value);
        }

        if (bodyComponent.BepuStaticHandle is { } staticHandle)
        {
            _staticHandleToComponent.Remove(staticHandle);
            _staticColliders.Remove(staticHandle.Value);
        }
    }

    /// <summary>The collider behind a collidable, or null when the collidable is a body without one.</summary>
    /// <param name="reference">The collidable to look up.</param>
    internal Collider? FindCollider(CollidableReference reference)
    {
        int handle = reference.RawHandleValue;

        return reference.Mobility == CollidableMobility.Static
            ? _staticColliders.GetValueOrDefault(handle)
            : _bodyColliders.GetValueOrDefault(handle);
    }

    /// <summary>The rigid body behind a collidable, or null when the collidable is a fixed shape.</summary>
    /// <param name="reference">The collidable to look up.</param>
    internal RigidBody? FindRigidBody(CollidableReference reference) =>
        reference.Mobility == CollidableMobility.Static
            ? _staticHandleToComponent.GetValueOrDefault(new StaticHandle(reference.RawHandleValue))
            : _bodyHandleToComponent.GetValueOrDefault(new BodyHandle(reference.RawHandleValue));

    /// <summary>True when contacts between two collidables are solved, which triggers and ignored layers never are.</summary>
    /// <param name="a">One collidable.</param>
    /// <param name="b">The other collidable.</param>
    internal bool ContactsAllowed(CollidableReference a, CollidableReference b)
    {
        Collider? colliderA = FindCollider(a);
        Collider? colliderB = FindCollider(b);

        if (colliderA is null && colliderB is null)
        {
            // Two bodies without colliders: nothing to report and nothing to filter, so they behave as they always did.
            return true;
        }

        if (colliderA?.IsTrigger == true || colliderB?.IsTrigger == true)
        {
            return false;
        }

        return colliderA is null || colliderB is null || Physics.LayersInteract(colliderA, colliderB);
    }

    /// <summary>The friction and bounce of a pair of collidables, which the solver uses for that contact only.</summary>
    /// <param name="a">One collidable.</param>
    /// <param name="b">The other collidable.</param>
    internal PairMaterialProperties MaterialFor(CollidableReference a, CollidableReference b)
    {
        ReadSurface(a, out float frictionA, out float bouncinessA);
        ReadSurface(b, out float frictionB, out float bouncinessB);

        // Friction combines as a geometric mean, so the slippier surface has the larger say; bounce takes the
        // livelier of the two, which is what makes a dropped ball bounce off a floor that does not.
        float friction = MathF.Sqrt(MathF.Max(frictionA, 0.0f) * MathF.Max(frictionB, 0.0f));
        float bounciness = Math.Clamp(MathF.Max(bouncinessA, bouncinessB), 0.0f, 1.0f);

        return new PairMaterialProperties(
            friction,
            2.0f * (1.0f + bounciness),
            new SpringSettings(30.0f, 1.0f));
    }

    /// <summary>Records a manifold's contacts, so the objects behind them can be told after the step.</summary>
    /// <param name="workerIndex">The worker that found them, which decides which buffer they go in.</param>
    /// <param name="pair">The collidables that met.</param>
    /// <param name="manifold">Their contacts.</param>
    internal void RecordContact<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        List<ContactSample> buffer = _contacts.For(workerIndex);
        int count = Math.Min(manifold.Count, ContactSample.MaxPerPair);

        if (count <= 0)
        {
            return;
        }

        for (int index = 0; index < count; index++)
        {
            manifold.GetContact(index, out Vector3 offset, out Vector3 normal, out float depth, out _);
            buffer.Add(new ContactSample(pair, offset, normal, depth));
        }
    }

    private void ReadSurface(CollidableReference reference, out float friction, out float bounciness)
    {
        if (FindCollider(reference) is { } collider)
        {
            friction = collider.Friction;
            bounciness = collider.Bounciness;
            return;
        }

        if (FindRigidBody(reference) is { } body)
        {
            friction = body.Friction;
            bounciness = body.Bounciness;
            return;
        }

        friction = 0.5f;
        bounciness = 0.0f;
    }

    private void AddStaticShape(Collider collider)
    {
        collider.GetWorldPose(out Vector3 center, out Quaternion rotation, out Vector3 scale);
        TypedIndex shape = collider.AddBepuShape(Simulation.Shapes, scale);
        StaticHandle handle = Simulation.Statics.Add(new StaticDescription(new RigidPose(center, rotation), shape));

        collider.StaticBody = handle;
        _staticColliders[handle.Value] = collider;
        _staticShapes[handle.Value] = shape;
    }

    private void RemoveColliderFromBodies(Collider collider)
    {
        foreach (int handle in _bodyColliders.Where(pair => pair.Value == collider).Select(pair => pair.Key).ToList())
        {
            _bodyColliders.Remove(handle);
        }

        foreach (int handle in _staticColliders.Where(pair => pair.Value == collider).Select(pair => pair.Key).ToList())
        {
            _staticColliders.Remove(handle);
        }
    }

    /// <summary>
    /// Moves the fixed shapes whose objects have been moved since the last step.
    /// </summary>
    /// <remarks>
    /// A collider with no rigid body is a static in Bepu, and Bepu never looks at the transform again once it has
    /// the pose: this is where the two are kept in agreement, so a floor slid into place before play, a moving
    /// platform, or a wall moved out of the way collides where it is drawn. Queries read the transform directly
    /// and so never needed this, which is exactly why it has to be done here or not at all.
    /// </remarks>
    private void RefreshStaticPoses()
    {
        if (_staticColliders.Count == 0)
        {
            return;
        }

        foreach ((int handleValue, Collider collider) in _staticColliders)
        {
            StaticHandle handle = new(handleValue);

            if (!Simulation.Statics.StaticExists(handle) || !_staticShapes.TryGetValue(handleValue, out TypedIndex shape))
            {
                continue;
            }

            collider.GetWorldPose(out Vector3 center, out Quaternion rotation, out _);

            Simulation.Statics.GetDescription(handle, out StaticDescription current);

            bool samePlace = (current.Pose.Position - center).LengthSquared() <= 1.0e-8f;
            bool sameTurn = MathF.Abs(Quaternion.Dot(current.Pose.Orientation, rotation)) >= 0.9999995f;

            if (samePlace && sameTurn)
            {
                continue;
            }

            Simulation.Statics.ApplyDescription(handle, new StaticDescription(new RigidPose(center, rotation), shape));
        }
    }

    private void SyncTransforms()
    {
        foreach ((BodyHandle handle, RigidBody bodyComponent) in _bodyHandleToComponent)
        {
            BodyReference bodyReference = Simulation.Bodies.GetBodyReference(handle);

            if (!bodyReference.Exists)
            {
                continue;
            }

            bodyComponent.ApplyBodyPose(bodyReference.Pose.Position, bodyReference.Pose.Orientation);
        }
    }

    /// <summary>Turns the contacts recorded during the steps into enter, stay and exit calls on the scripts involved.</summary>
    private void DispatchCollisions()
    {
        _currentCollisions.Clear();

        foreach (ContactSample sample in _contacts.Samples())
        {
            long key = PairKey(sample.Pair);

            if (!_currentCollisions.TryGetValue(key, out CollisionPair? pair))
            {
                Collider? colliderA = FindCollider(sample.Pair.A);
                Collider? colliderB = FindCollider(sample.Pair.B);

                if (colliderA is null || colliderB is null)
                {
                    continue;
                }

                pair = new CollisionPair(colliderA, colliderB);
                _currentCollisions[key] = pair;
            }

            pair.Add(sample);
        }

        foreach ((long key, CollisionPair pair) in _currentCollisions)
        {
            bool enter = !_activeCollisions.ContainsKey(key);
            NotifyCollision(pair, enter ? CollisionPhase.Enter : CollisionPhase.Stay);
        }

        foreach ((long key, CollisionPair pair) in _activeCollisions)
        {
            if (!_currentCollisions.ContainsKey(key))
            {
                NotifyCollision(pair, CollisionPhase.Exit);
            }
        }

        (_activeCollisions, _currentCollisions) = (_currentCollisions, _activeCollisions);
        _currentCollisions.Clear();
    }

    /// <summary>Reports the trigger volumes that have something inside them, and those that just lost it.</summary>
    private void DispatchTriggers()
    {
        _currentTriggers.Clear();

        foreach (Collider trigger in _triggers)
        {
            if (trigger.GameObject is null)
            {
                continue;
            }

            ShapeInstance triggerShape = ColliderGeometry.From(trigger);
            BoundingBox bounds = ColliderGeometry.Bounds(triggerShape);

            foreach (Collider other in _colliders)
            {
                if (ReferenceEquals(other, trigger)
                    || other.IsTrigger
                    || other is CharacterController
                    || other.GameObject is null
                    || ReferenceEquals(other.GameObject, trigger.GameObject)
                    || !Physics.LayersInteract(trigger, other))
                {
                    continue;
                }

                BoundingBox otherBounds = ColliderGeometry.Bounds(ColliderGeometry.From(other));

                if (!BoxesOverlap(bounds, otherBounds))
                {
                    continue;
                }

                if (!ColliderGeometry.Overlaps(triggerShape, ColliderGeometry.From(other)))
                {
                    continue;
                }

                long key = PairKey(trigger, other);

                if (!_currentTriggers.ContainsKey(key))
                {
                    _currentTriggers[key] = new TriggerPair(trigger, other);
                }
            }
        }

        foreach ((long key, TriggerPair pair) in _currentTriggers)
        {
            NotifyTrigger(pair, _activeTriggers.ContainsKey(key) ? CollisionPhase.Stay : CollisionPhase.Enter);
        }

        foreach ((long key, TriggerPair pair) in _activeTriggers)
        {
            if (!_currentTriggers.ContainsKey(key))
            {
                NotifyTrigger(pair, CollisionPhase.Exit);
            }
        }

        (_activeTriggers, _currentTriggers) = (_currentTriggers, _activeTriggers);
        _currentTriggers.Clear();
    }

    private void NotifyCollision(CollisionPair pair, CollisionPhase phase)
    {
        Vector3 relativeVelocity = VelocityOf(pair.A) - VelocityOf(pair.B);

        if (pair.A is not CharacterController)
            Notify(pair.A, pair.B, pair.ContactsForA(), relativeVelocity, phase);
        if (pair.B is not CharacterController)
            Notify(pair.B, pair.A, pair.ContactsForB(), -relativeVelocity, phase);
    }

    private void NotifyTrigger(TriggerPair pair, CollisionPhase phase)
    {
        NotifyTrigger(pair.A, pair.B, phase);
        NotifyTrigger(pair.B, pair.A, phase);
    }

    private void NotifyTrigger(Collider receiver, Collider other, CollisionPhase phase)
    {
        foreach (Script script in ScriptsOf(receiver))
        {
            try
            {
                switch (phase)
                {
                    case CollisionPhase.Enter:
                        script.OnTriggerEnter(other);
                        break;
                    case CollisionPhase.Stay:
                        script.OnTriggerStay(other);
                        break;
                    default:
                        script.OnTriggerExit(other);
                        break;
                }
            }
            catch (Exception exception)
            {
                ReportCallbackFailure(script, phase, exception);
            }
        }
    }

    private void Notify(
        Collider receiver,
        Collider other,
        IReadOnlyList<ContactPoint> contacts,
        Vector3 relativeVelocity,
        CollisionPhase phase)
    {
        foreach (Script script in ScriptsOf(receiver))
        {
            try
            {
                switch (phase)
                {
                    case CollisionPhase.Enter:
                        script.OnCollisionEnter(new Collision(other, contacts, relativeVelocity));
                        break;
                    case CollisionPhase.Stay:
                        script.OnCollisionStay(new Collision(other, contacts, relativeVelocity));
                        break;
                    default:
                        // An exit has no contacts left to report; Unity keeps the last set, and so does this.
                        script.OnCollisionExit(new Collision(other, [], relativeVelocity));
                        break;
                }
            }
            catch (Exception exception)
            {
                ReportCallbackFailure(script, phase, exception);
            }
        }
    }

    private static IReadOnlyList<Script> ScriptsOf(Collider collider)
    {
        if (collider.GameObject is not { } gameObject || !collider.IsActive)
        {
            return [];
        }

        List<Script> scripts = [];

        foreach (Script script in gameObject.GetComponents<Script>())
        {
            // A script the author switched off, or its object did, hears nothing: Unity's rule.
            if (script.IsActive && !script.IsSuspended)
            {
                scripts.Add(script);
            }
        }

        return scripts;
    }

    private static void ReportCallbackFailure(Script script, CollisionPhase phase, Exception exception) =>
        Logger.Error(
            $"[Physics] {script.GetType().Name}.{phase} threw on " +
            $"'{script.GameObject?.Name}': {exception.Message}");

    private Vector3 VelocityOf(Collider collider) => collider.AttachedRigidBody?.GetVelocity() ?? Vector3.Zero;

    private static bool BoxesOverlap(BoundingBox a, BoundingBox b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
        && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
        && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

    private static long PairKey(in CollidablePair pair)
    {
        uint low = Math.Min(pair.A.Packed, pair.B.Packed);
        uint high = Math.Max(pair.A.Packed, pair.B.Packed);
        return ((long)low << 32) | high;
    }

    private static long PairKey(Collider a, Collider b)
    {
        int first = RuntimeHelpers.GetHashCode(a);
        int second = RuntimeHelpers.GetHashCode(b);
        return ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);
    }

    /// <summary>Which of the three callbacks a pair of colliders is being reported with.</summary>
    private enum CollisionPhase
    {
        Enter,
        Stay,
        Exit
    }

    /// <summary>One contact as the solver found it: an offset from the first collidable, a normal and a depth.</summary>
    private readonly struct ContactSample
    {
        public const int MaxPerPair = 4;

        public CollidablePair Pair { get; }
        public Vector3 Offset { get; }
        public Vector3 Normal { get; }
        public float Depth { get; }

        public ContactSample(CollidablePair pair, Vector3 offset, Vector3 normal, float depth)
        {
            Pair = pair;
            Offset = offset;
            Normal = normal;
            Depth = depth;
        }
    }

    /// <summary>Two colliders that are touching in this step, with the places where they touch.</summary>
    /// <remarks>
    /// <see cref="A"/> is always the collider of the pair's first collidable, which is the one the solver measures
    /// its contact offsets from, so a contact point is that object's centre plus the offset.
    /// </remarks>
    private sealed class CollisionPair
    {
        public Collider A { get; }
        public Collider B { get; }

        private readonly List<(Vector3 Offset, Vector3 Normal, float Depth)> _contacts = [];

        public CollisionPair(Collider a, Collider b)
        {
            A = a;
            B = b;
        }

        public void Add(ContactSample sample)
        {
            if (_contacts.Count < ContactSample.MaxPerPair)
            {
                _contacts.Add((sample.Offset, sample.Normal, sample.Depth));
            }
        }

        /// <summary>The contacts as A sees them: the normals already point away from B.</summary>
        public IReadOnlyList<ContactPoint> ContactsForA() => Build(A, B, flip: false);

        /// <summary>The contacts as B sees them: the normals are turned round to point away from A.</summary>
        public IReadOnlyList<ContactPoint> ContactsForB() => Build(B, A, flip: true);

        private List<ContactPoint> Build(Collider receiver, Collider other, bool flip)
        {
            List<ContactPoint> points = new(_contacts.Count);

            foreach ((Vector3 offset, Vector3 normal, float depth) in _contacts)
            {
                points.Add(new ContactPoint
                {
                    ThisCollider = receiver,
                    OtherCollider = other,
                    Point = A.WorldCenter + offset,
                    Normal = flip ? -normal : normal,
                    Separation = -depth
                });
            }

            return points;
        }
    }

    /// <summary>Two colliders overlapping because one of them is a trigger.</summary>
    private sealed class TriggerPair
    {
        public Collider A { get; }
        public Collider B { get; }

        public TriggerPair(Collider a, Collider b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>The buffers the workers fill with contacts during a step, one each.</summary>
    private sealed class ContactBuffers
    {
        // Bepu hands out worker indices from the dispatcher's own count; this leaves room for any of them.
        private const int Capacity = 128;

        private readonly List<ContactSample>[] _buffers = new List<ContactSample>[Capacity];

        public List<ContactSample> For(int workerIndex)
        {
            int index = workerIndex >= 0 && workerIndex < Capacity ? workerIndex : 0;
            return _buffers[index] ??= [];
        }

        public IEnumerable<ContactSample> Samples()
        {
            foreach (List<ContactSample>? buffer in _buffers)
            {
                if (buffer is null)
                {
                    continue;
                }

                foreach (ContactSample sample in buffer)
                {
                    yield return sample;
                }
            }
        }

        public void Clear()
        {
            foreach (List<ContactSample>? buffer in _buffers)
            {
                buffer?.Clear();
            }
        }
    }

    public void Dispose()
    {
        FixedStep = null;
        _colliders.Clear();
        _triggers.Clear();
        _bodyColliders.Clear();
        _staticColliders.Clear();
        _staticShapes.Clear();
        _bodyHandleToComponent.Clear();
        _staticHandleToComponent.Clear();

        Simulation.Dispose();
        _threadDispatcher.Dispose();
        _bufferPool.Clear();

        if (ReferenceEquals(Active, this))
        {
            Active = null;
        }
    }
}
