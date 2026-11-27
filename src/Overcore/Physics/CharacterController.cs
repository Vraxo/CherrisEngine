using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Core.Logging;
using System.Numerics;
using ControllerCollisionFlags = Overcore.Components.CollisionFlags;

namespace Overcore.Components;

/// <summary>The flags returned by <see cref="CharacterController.Move"/>.</summary>
[Flags]
public enum CollisionFlags
{
    /// <summary>No collision occurred.</summary>
    None = 0,
    /// <summary>The controller hit a wall or another surface to its side.</summary>
    Sides = 1,
    /// <summary>The controller hit a surface above it.</summary>
    Above = 2,
    /// <summary>The controller hit a walkable surface below it.</summary>
    Below = 4
}

/// <summary>A character controller's contact with a collider during a call to <see cref="CharacterController.Move"/>.</summary>
public readonly struct ControllerColliderHit
{
    /// <summary>The collider the controller touched.</summary>
    public Collider Collider { get; init; }
    /// <summary>The object the touched collider belongs to.</summary>
    public GameObject GameObject => Collider.GameObject!;
    /// <summary>The world-space point of contact.</summary>
    public Vector3 Point { get; init; }
    /// <summary>The contact normal, pointing away from the surface.</summary>
    public Vector3 Normal { get; init; }
    /// <summary>The movement supplied to <see cref="CharacterController.Move"/>.</summary>
    public Vector3 MoveDirection { get; init; }
    /// <summary>The magnitude of the movement supplied to <see cref="CharacterController.Move"/>.</summary>
    public float MoveLength { get; init; }
}

/// <summary>
/// A non-rigidbody capsule controller for player and NPC movement. Movement is explicit, collision-constrained, and
/// independent of the fixed-step rigid-body solver, matching Unity's <c>CharacterController</c> model.
/// </summary>
/// <remarks>
/// <para><see cref="Move"/> consumes a world-space displacement and does not apply gravity. <see cref="SimpleMove"/>
/// consumes a world-space speed, applies <see cref="Physics.Gravity"/>, and ignores its vertical input component.</para>
/// <para>Capsule sweeps use conservative advancement over capsule-to-collider separation, independent of Bepu's body
/// solver and valid for the engine's box, sphere, and capsule colliders.</para>
/// </remarks>
public sealed class CharacterController : Component
{
    private enum TriggerPhase { Enter, Stay, Exit }
    private readonly record struct MotionSegment(Vector3 Start, Vector3 End);

    private float _height = 2f;
    private float _radius = 0.5f;
    private Vector3 _center = new(0f, 1f, 0f);
    private float _slopeLimit = 45f;
    private float _stepOffset = 0.3f;
    private float _skinWidth = 0.08f;
    private float _minMoveDistance;
    private Vector3 _velocity;
    private Collider? _supportCollider;
    private Quaternion _supportRotation = Quaternion.Identity;
    private Vector3 _supportRelativePosition;
    private readonly HashSet<Collider> _activeTriggers = [];
    private readonly Queue<(Collider Trigger, TriggerPhase Phase)> _pendingTriggerEvents = [];
    private bool _dispatchingTriggerEvents;

    /// <summary>Total capsule height, including the rounded ends.</summary>
    public float Height
    {
        get => _height;
        set
        {
            _height = MathF.Max(2f * _radius, value);
            _stepOffset = MathF.Min(_stepOffset, _height);
        }
    }
    /// <summary>Capsule radius.</summary>
    public float Radius { get => _radius; set { _radius = MathF.Max(0.01f, value); _height = MathF.Max(_height, 2f * _radius); } }
    /// <summary>Capsule centre in local space. The default places the capsule's feet at the object origin.</summary>
    public Vector3 Center { get => _center; set => _center = value; }
    /// <summary>Maximum walkable slope in degrees, from 0 through 90.</summary>
    public float SlopeLimit { get => _slopeLimit; set => _slopeLimit = Math.Clamp(value, 0f, 90f); }
    /// <summary>Maximum vertical ledge height the controller can step onto.</summary>
    public float StepOffset { get => _stepOffset; set => _stepOffset = Math.Clamp(value, 0f, Height); }
    /// <summary>Collision skin that prevents the capsule from being pressed exactly against surfaces.</summary>
    public float SkinWidth { get => _skinWidth; set => _skinWidth = MathF.Max(0.001f, value); }
    /// <summary>Displacements shorter than this are ignored.</summary>
    public float MinMoveDistance { get => _minMoveDistance; set => _minMoveDistance = MathF.Max(0f, value); }
    /// <summary>Whether movement is constrained by colliders.</summary>
    public bool DetectCollisions { get; set; } = true;
    /// <summary>Whether small initial overlaps are resolved before the controller moves.</summary>
    public bool EnableOverlapRecovery { get; set; } = true;
    /// <summary>True when the last movement touched a walkable surface below.</summary>
    public bool IsGrounded { get; private set; }
    /// <summary>World-space velocity estimated from the most recent explicit move.</summary>
    public Vector3 Velocity => _velocity;
    /// <summary>Flags produced by the most recent movement.</summary>
    public CollisionFlags CollisionFlags { get; private set; }

    /// <summary>Moves by a world-space displacement; gravity is not applied.</summary>
    public CollisionFlags Move(Vector3 motion)
    {
        Vector3 frameStart = Transform.WorldPosition;
        Vector3 requestedMotion = motion;
        Vector3 supportMotion = GetSupportMotion(frameStart);
        motion += supportMotion;
        if (motion.Length() <= MinMoveDistance && supportMotion.Length() <= MinMoveDistance)
        {
            motion = Vector3.Zero;
        }

        List<ControllerColliderHit> contacts = [];
        List<MotionSegment> movementPath = [];
        Vector3 triggerStartPosition = frameStart;
        _supportCollider = null;

        if (!DetectCollisions)
        {
            Transform.WorldPosition = frameStart + motion;
            AddMotionSegment(movementPath, frameStart, Transform.WorldPosition);
            CollisionFlags = ControllerCollisionFlags.None;
        }
        else
        {
            if (EnableOverlapRecovery) RecoverOverlaps(movementPath);
            Vector3 movementStart = Transform.WorldPosition;
            int recoveryPathCount = movementPath.Count;

            bool probeGround = motion.Y <= 0f || (supportMotion.Y > 0f && requestedMotion.Y <= 0f);
            CollisionFlags flags = MoveAndSlide(motion, contacts, movementPath, probeGround);
            if (StepOffset > 0f && motion.Y <= 0f &&
                (flags & ControllerCollisionFlags.Sides) != 0 && (flags & ControllerCollisionFlags.Below) != 0)
            {
                Vector3 normalMoveEnd = Transform.WorldPosition;
                Collider? originalSupport = _supportCollider;
                if (TryStep(movementStart, motion, contacts, out CollisionFlags steppedFlags, out List<MotionSegment> steppedPath))
                {
                    flags = steppedFlags;
                    movementPath.RemoveRange(recoveryPathCount, movementPath.Count - recoveryPathCount);
                    movementPath.AddRange(steppedPath);
                }
                else
                {
                    Transform.WorldPosition = normalMoveEnd;
                    _supportCollider = originalSupport;
                }
            }

            CollisionFlags = flags;
        }

        IsGrounded = (CollisionFlags & ControllerCollisionFlags.Below) != 0;
        UpdateSupportPose();
        DispatchTriggerChanges(movementPath, triggerStartPosition);
        float dt = Time.DeltaTime;
        _velocity = dt > 1e-6f ? (Transform.WorldPosition - frameStart) / dt : Vector3.Zero;
        foreach (ControllerColliderHit contact in contacts.DistinctBy(hit => hit.Collider))
        {
            foreach (Script script in GameObject!.GetComponents<Script>()
                .Where(script => script.IsActive && !script.IsSuspended).ToArray())
            {
                try { script.OnControllerColliderHit(contact); }
                catch (Exception exception) { Logger.Warning($"Controller collision callback on '{GameObject.Name}' failed: {exception.Message}"); }
            }
        }

        return CollisionFlags;
    }

    /// <summary>Moves by a world-space speed, applies gravity, and returns whether the controller is grounded.</summary>
    public bool SimpleMove(Vector3 speed)
    {
        float deltaTime = MathF.Max(Time.DeltaTime, 0f);
        _velocity.Y += Physics.Gravity.Y * deltaTime;
        Vector3 motion = new(speed.X * deltaTime, _velocity.Y * deltaTime, speed.Z * deltaTime);
        CollisionFlags result = Move(motion);
        if ((result & ControllerCollisionFlags.Below) != 0)
        {
            _velocity.Y = 0f;
        }
        return IsGrounded;
    }

    private CollisionFlags MoveAndSlide(
        Vector3 motion,
        List<ControllerColliderHit> contacts,
        List<MotionSegment> path,
        bool probeGround = true)
    {
        CollisionFlags flags = ControllerCollisionFlags.None;
        Vector3 remaining = motion;
        for (int iteration = 0; iteration < 5 && remaining.LengthSquared() > 1e-8f; iteration++)
        {
            if (!Sweep(remaining, out RaycastHit hit))
            {
                Vector3 segmentStart = Transform.WorldPosition;
                Transform.WorldPosition += remaining;
                AddMotionSegment(path, segmentStart, Transform.WorldPosition);
                break;
            }

            contacts.Add(new ControllerColliderHit
            {
                Collider = hit.Collider,
                Point = hit.Point,
                Normal = hit.Normal,
                MoveDirection = Vector3.Normalize(remaining),
                MoveLength = remaining.Length()
            });

            float travel = Math.Clamp(hit.Distance - SkinWidth, 0f, remaining.Length());
            Vector3 direction = Vector3.Normalize(remaining);
            Vector3 segmentStart = Transform.WorldPosition;
            Transform.WorldPosition += direction * travel;
            AddMotionSegment(path, segmentStart, Transform.WorldPosition);
            remaining -= direction * travel;

            float verticalNormal = Vector3.Dot(hit.Normal, Vector3.UnitY);
            if (verticalNormal >= WalkableDot && remaining.Y <= 0f)
            {
                _supportCollider = hit.Collider;
                flags |= ControllerCollisionFlags.Below;
                if (remaining.Y < 0f) remaining.Y = 0f;
            }
            else if (verticalNormal < -1e-4f && remaining.Y > 0f)
            {
                flags |= ControllerCollisionFlags.Above;
                remaining.Y = 0f;
            }
            else
            {
                flags |= ControllerCollisionFlags.Sides;
            }

            // Slide along the contact plane instead of stopping dead at a wall. A steep uphill slope is not
            // walkable: remove its inward horizontal component rather than letting the plane projection climb it.
            if (Vector3.Dot(remaining, hit.Normal) < 0f)
            {
                Vector3 slide = remaining - (hit.Normal * Vector3.Dot(remaining, hit.Normal));
                if (verticalNormal > 0f && verticalNormal < WalkableDot && slide.Y > 1e-5f)
                {
                    slide.Y = 0f;
                    Vector3 horizontalNormal = new(hit.Normal.X, 0f, hit.Normal.Z);
                    if (horizontalNormal.LengthSquared() > 1e-8f)
                    {
                        horizontalNormal = Vector3.Normalize(horizontalNormal);
                        float intoSlope = Vector3.Dot(slide, horizontalNormal);
                        if (intoSlope < 0f) slide -= horizontalNormal * intoSlope;
                    }
                }
                remaining = slide;
            }
        }

        // A short downward probe provides stable grounding when the caller's requested motion is horizontal.
        if (probeGround && Sweep(-Vector3.UnitY * (SkinWidth + 0.03f), out RaycastHit ground) &&
            Vector3.Dot(ground.Normal, Vector3.UnitY) >= WalkableDot)
        {
            _supportCollider = ground.Collider;
            flags |= ControllerCollisionFlags.Below;
        }
        return flags;
    }

    private bool TryStep(
        Vector3 start,
        Vector3 motion,
        List<ControllerColliderHit> contacts,
        out CollisionFlags flags,
        out List<MotionSegment> acceptedPath)
    {
        flags = ControllerCollisionFlags.None;
        acceptedPath = [];
        Vector3 horizontal = new(motion.X, 0f, motion.Z);
        if (horizontal.LengthSquared() < 1e-8f) return false;

        Transform.WorldPosition = start;
        if (Sweep(Vector3.UnitY * StepOffset, out _)) return false;
        List<MotionSegment> stepPath = [];
        Vector3 stepStart = Transform.WorldPosition;
        Transform.WorldPosition += Vector3.UnitY * StepOffset;
        AddMotionSegment(stepPath, stepStart, Transform.WorldPosition);
        List<ControllerColliderHit> stepContacts = [];
        CollisionFlags stepFlags = MoveAndSlide(horizontal, stepContacts, stepPath);
        if ((stepFlags & ControllerCollisionFlags.Sides) != 0)
        {
            Transform.WorldPosition = start;
            return false;
        }

        float downDistance = StepOffset + MathF.Max(0f, -motion.Y) + SkinWidth + 0.03f;
        if (!Sweep(-Vector3.UnitY * downDistance, out RaycastHit landing) ||
            Vector3.Dot(landing.Normal, Vector3.UnitY) < WalkableDot)
        {
            Transform.WorldPosition = start;
            return false;
        }

        Vector3 landingStart = Transform.WorldPosition;
        Transform.WorldPosition += -Vector3.UnitY * MathF.Max(0f, landing.Distance - SkinWidth);
        AddMotionSegment(stepPath, landingStart, Transform.WorldPosition);
        _supportCollider = landing.Collider;
        contacts.AddRange(stepContacts);
        flags = (stepFlags & ~ControllerCollisionFlags.Below) | ControllerCollisionFlags.Below;
        acceptedPath = stepPath;
        return true;
    }

    private static void AddMotionSegment(List<MotionSegment> path, Vector3 start, Vector3 end)
    {
        if (Vector3.DistanceSquared(start, end) > 1e-12f) path.Add(new MotionSegment(start, end));
    }

    private bool Sweep(Vector3 motion, out RaycastHit hit)
    {
        GetCapsule(out Vector3 lower, out Vector3 upper, out float radius);
        return Physics.CapsuleCastForController(lower, upper, radius, motion, GameObject!, out hit);
    }

    private void GetCapsule(out Vector3 lower, out Vector3 upper, out float radius)
    {
        Vector3 scale = Vector3.Abs(Transform.LossyScale);
        Vector3 center = Transform.WorldPosition + Vector3.Transform(Center * scale, Transform.WorldRotation);
        radius = MathF.Max(0.01f, Radius * MathF.Max(scale.X, scale.Z));
        float height = MathF.Max(Height * scale.Y, radius * 2f);
        Vector3 axis = Vector3.UnitY * MathF.Max(0f, height * 0.5f - radius);
        lower = center - axis;
        upper = center + axis;
    }

    public override void OnDisable()
    {
        ClearTriggerContacts();
        _supportCollider = null;
        base.OnDisable();
    }

    public override void OnDestroy()
    {
        ClearTriggerContacts();
        _supportCollider = null;
        base.OnDestroy();
    }

    private void ClearTriggerContacts()
    {
        Collider[] active = [.. _activeTriggers];
        _activeTriggers.Clear();
        foreach (Collider trigger in active) DispatchTriggerEvent(trigger, TriggerPhase.Exit);
    }

    private void DispatchTriggerChanges(List<MotionSegment> path, Vector3 startPosition)
    {
        if (!DetectCollisions)
        {
            ClearTriggerContacts();
            return;
        }

        GetCapsule(out Vector3 lower, out Vector3 upper, out float radius);
        Vector3 finalPosition = Transform.WorldPosition;
        HashSet<Collider> current = Physics.OverlapTriggersForController(lower, upper, radius, GameObject!).ToHashSet();
        Vector3 startOffset = startPosition - finalPosition;
        HashSet<Collider> initial = Physics.OverlapTriggersForController(
            lower + startOffset, upper + startOffset, radius, GameObject!).ToHashSet();
        HashSet<Collider> candidates = Physics.ControllerTriggerColliders(GameObject!).ToHashSet();
        candidates.UnionWith(_activeTriggers);

        List<(Collider Trigger, TriggerPhase Phase)> transitions = [];
        foreach (Collider trigger in candidates)
        {
            bool wasOverlapping = _activeTriggers.Contains(trigger);
            bool startedOverlapping = initial.Contains(trigger);
            bool isOverlapping = current.Contains(trigger);
            bool crossedDuringMove = false;
            if (!startedOverlapping && !isOverlapping)
            {
                foreach (MotionSegment segment in path)
                {
                    Vector3 displacement = segment.End - segment.Start;
                    Vector3 offset = segment.Start - finalPosition;
                    if (!Physics.CapsuleCastTriggerForController(
                        trigger, lower + offset, upper + offset, radius, displacement, GameObject!)) continue;
                    crossedDuringMove = true;
                    break;
                }
            }

            if (startedOverlapping)
            {
                if (!wasOverlapping) transitions.Add((trigger, TriggerPhase.Enter));
                else if (isOverlapping) transitions.Add((trigger, TriggerPhase.Stay));
                if (!isOverlapping) transitions.Add((trigger, TriggerPhase.Exit));
            }
            else if (crossedDuringMove)
            {
                transitions.Add((trigger, TriggerPhase.Enter));
                if (!isOverlapping) transitions.Add((trigger, TriggerPhase.Exit));
            }
            else if (isOverlapping)
            {
                transitions.Add((trigger, TriggerPhase.Enter));
            }
            else if (wasOverlapping)
            {
                transitions.Add((trigger, TriggerPhase.Exit));
            }
        }

        _activeTriggers.Clear();
        _activeTriggers.UnionWith(current);
        foreach ((Collider trigger, TriggerPhase phase) in transitions) DispatchTriggerEvent(trigger, phase);
    }

    private void DispatchTriggerEvent(Collider trigger, TriggerPhase phase)
    {
        _pendingTriggerEvents.Enqueue((trigger, phase));
        if (_dispatchingTriggerEvents) return;

        _dispatchingTriggerEvents = true;
        try
        {
            while (_pendingTriggerEvents.TryDequeue(out var next))
            {
                NotifyTrigger(next.Trigger, next.Phase);
            }
        }
        finally
        {
            _dispatchingTriggerEvents = false;
        }
    }

    private void NotifyTrigger(Collider trigger, TriggerPhase phase)
    {
        foreach (Script script in GameObject!.GetComponents<Script>()
            .Where(script => script.IsActive && !script.IsSuspended).ToArray())
        {
            try
            {
                switch (phase)
                {
                    case TriggerPhase.Enter:
                        script.OnTriggerEnter(trigger);
                        break;
                    case TriggerPhase.Stay:
                        script.OnTriggerStay(trigger);
                        break;
                    default:
                        script.OnTriggerExit(trigger);
                        break;
                }
            }
            catch (Exception exception)
            {
                Logger.Warning($"Controller trigger callback on '{GameObject.Name}' failed: {exception.Message}");
            }
        }

        if (trigger.GameObject is not { } triggerObject) return;
        foreach (Script script in triggerObject.GetComponents<Script>()
            .Where(script => script.IsActive && !script.IsSuspended).ToArray())
        {
            try
            {
                switch (phase)
                {
                    case TriggerPhase.Enter:
                        script.OnTriggerEnter(this);
                        break;
                    case TriggerPhase.Stay:
                        script.OnTriggerStay(this);
                        break;
                    default:
                        script.OnTriggerExit(this);
                        break;
                }
            }
            catch (Exception exception)
            {
                Logger.Warning($"Trigger callback on '{triggerObject.Name}' for controller '{GameObject.Name}' failed: {exception.Message}");
            }
        }
    }

    private Vector3 GetSupportMotion(Vector3 controllerPosition)
    {
        Collider? support = _supportCollider;
        if (support is null || !support.IsActive || support.IsDestroyed || support.GameObject is not { ActiveInHierarchy: true })
        {
            _supportCollider = null;
            return Vector3.Zero;
        }

        Vector3 currentPlatformPosition = support.Transform.WorldPosition;
        Quaternion currentPlatformRotation = support.Transform.WorldRotation;
        Quaternion rotationDelta = Quaternion.Normalize(currentPlatformRotation * Quaternion.Inverse(_supportRotation));
        Vector3 carriedPosition = currentPlatformPosition + Vector3.Transform(_supportRelativePosition, rotationDelta);
        return carriedPosition - controllerPosition;
    }

    private void UpdateSupportPose()
    {
        Collider? support = _supportCollider;
        if (!IsGrounded || support is null || !support.IsActive || support.IsDestroyed ||
            support.GameObject is not { ActiveInHierarchy: true })
        {
            _supportCollider = null;
            return;
        }

        _supportRotation = support.Transform.WorldRotation;
        _supportRelativePosition = Transform.WorldPosition - support.Transform.WorldPosition;
    }

    private void RecoverOverlaps(List<MotionSegment> path)
    {
        // Resolve the deepest penetration a little at a time. The cap prevents a misplaced controller from being
        // teleported an arbitrary distance out of level geometry.
        float recoveredDistance = 0f;
        for (int iteration = 0; iteration < 8 && recoveredDistance < 0.5f; iteration++)
        {
            GetCapsule(out Vector3 lower, out Vector3 upper, out float radius);
            if (!Physics.TryGetCapsulePenetration(lower, upper, radius, GameObject!,
                out Vector3 correction, out _)) return;

            float correctionLength = correction.Length();
            if (correctionLength < 1e-5f) return;
            float allowed = MathF.Min(correctionLength + 0.001f, 0.5f - recoveredDistance);
            Vector3 correctionStart = Transform.WorldPosition;
            Transform.WorldPosition += correction * (allowed / correctionLength);
            AddMotionSegment(path, correctionStart, Transform.WorldPosition);
            recoveredDistance += allowed;
        }
    }

    private float WalkableDot => MathF.Cos(SlopeLimit * (MathF.PI / 180f));
}
