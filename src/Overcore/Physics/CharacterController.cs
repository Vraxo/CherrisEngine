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
        Vector3 supportMotion = GetSupportMotion(frameStart);
        motion += supportMotion;
        if (motion.Length() <= MinMoveDistance && supportMotion.Length() <= MinMoveDistance)
        {
            _velocity = Vector3.Zero;
            CollisionFlags = ControllerCollisionFlags.None;
            IsGrounded = false;
            return CollisionFlags;
        }

        List<ControllerColliderHit> contacts = [];
        _supportCollider = null;

        if (!DetectCollisions)
        {
            Transform.WorldPosition = frameStart + motion;
            CollisionFlags = ControllerCollisionFlags.None;
        }
        else
        {
            if (EnableOverlapRecovery) RecoverOverlaps();
            Vector3 movementStart = Transform.WorldPosition;

            CollisionFlags flags = MoveAndSlide(motion, contacts);
            if (StepOffset > 0f && (flags & ControllerCollisionFlags.Sides) != 0)
            {
                Collider? originalSupport = _supportCollider;
                if (TryStep(movementStart, motion, contacts, out CollisionFlags steppedFlags))
                {
                    flags = steppedFlags;
                }
                else
                {
                    _supportCollider = originalSupport;
                }
            }

            CollisionFlags = flags;
        }

        IsGrounded = (CollisionFlags & ControllerCollisionFlags.Below) != 0;
        UpdateSupportPose();
        float dt = Time.DeltaTime;
        _velocity = dt > 1e-6f ? (Transform.WorldPosition - frameStart) / dt : Vector3.Zero;
        foreach (ControllerColliderHit contact in contacts.DistinctBy(hit => hit.Collider))
        {
            foreach (Script script in GameObject!.GetComponents<Script>().Where(script => script.IsActive && !script.IsSuspended))
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

    private CollisionFlags MoveAndSlide(Vector3 motion, List<ControllerColliderHit> contacts)
    {
        CollisionFlags flags = ControllerCollisionFlags.None;
        Vector3 remaining = motion;
        for (int iteration = 0; iteration < 5 && remaining.LengthSquared() > 1e-8f; iteration++)
        {
            if (!Sweep(remaining, out RaycastHit hit))
            {
                Transform.WorldPosition += remaining;
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
            Transform.WorldPosition += direction * travel;
            remaining -= direction * travel;

            if (Vector3.Dot(hit.Normal, Vector3.UnitY) >= WalkableDot)
            {
                _supportCollider = hit.Collider;
                flags |= ControllerCollisionFlags.Below;
                if (remaining.Y < 0f) remaining.Y = 0f;
            }
            else if (Vector3.Dot(hit.Normal, Vector3.UnitY) <= -0.5f)
            {
                flags |= ControllerCollisionFlags.Above;
                if (remaining.Y > 0f) remaining.Y = 0f;
            }
            else
            {
                flags |= ControllerCollisionFlags.Sides;
            }

            // Slide along the contact plane instead of stopping dead at a wall.
            remaining = Vector3.Dot(remaining, hit.Normal) < 0f
                ? remaining - (hit.Normal * Vector3.Dot(remaining, hit.Normal))
                : remaining;
        }

        // A short downward probe provides stable grounding when the caller's requested motion is horizontal.
        if (Sweep(-Vector3.UnitY * (SkinWidth + 0.03f), out RaycastHit ground) &&
            Vector3.Dot(ground.Normal, Vector3.UnitY) >= WalkableDot)
        {
            _supportCollider = ground.Collider;
            flags |= ControllerCollisionFlags.Below;
        }
        return flags;
    }

    private bool TryStep(Vector3 start, Vector3 motion, List<ControllerColliderHit> contacts, out CollisionFlags flags)
    {
        flags = ControllerCollisionFlags.None;
        Vector3 horizontal = new(motion.X, 0f, motion.Z);
        if (horizontal.LengthSquared() < 1e-8f) return false;

        Transform.WorldPosition = start;
        if (Sweep(Vector3.UnitY * StepOffset, out _)) return false;
        Transform.WorldPosition += Vector3.UnitY * StepOffset;
        List<ControllerColliderHit> stepContacts = [];
        CollisionFlags stepFlags = MoveAndSlide(horizontal, stepContacts);
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

        Transform.WorldPosition += -Vector3.UnitY * MathF.Max(0f, landing.Distance - SkinWidth);
        _supportCollider = landing.Collider;
        contacts.AddRange(stepContacts);
        flags = (stepFlags & ~ControllerCollisionFlags.Below) | ControllerCollisionFlags.Below;
        return true;
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
        _supportRelativePosition = Transform.WorldPosition - _supportPosition;
    }

    private void RecoverOverlaps()
    {
        // Resolve the deepest penetration a little at a time. The cap prevents a misplaced controller from being
        // teleported an arbitrary distance out of level geometry.
        float recoveredDistance = 0f;
        for (int iteration = 0; iteration < 8 && recoveredDistance < 0.5f; iteration++)
        {
            GetCapsule(out Vector3 lower, out Vector3 upper, out float radius);
            if (!Physics.TryGetCapsulePenetration(lower, upper, radius, GameObject!,
                out Vector3 correction, out float depth)) return;

            float correctionLength = correction.Length();
            if (correctionLength < 1e-5f) return;
            float allowed = MathF.Min(correctionLength + 0.001f, 0.5f - recoveredDistance);
            Transform.WorldPosition += correction * (allowed / correctionLength);
            recoveredDistance += allowed;
        }
    }

    private float WalkableDot => MathF.Cos(SlopeLimit * (MathF.PI / 180f));
}
