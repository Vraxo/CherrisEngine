using Overcore.Attributes;
using Overcore.Core;
using Overcore.Navigation;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>Local obstacle avoidance fidelity for a <see cref="NavMeshAgent"/>.</summary>
public enum ObstacleAvoidanceType
{
    NoObstacleAvoidance,
    LowQualityObstacleAvoidance,
    MedQualityObstacleAvoidance,
    GoodQualityObstacleAvoidance,
    HighQualityObstacleAvoidance
}

/// <summary>
/// Autonomous pathfinding and crowd-steering component — Unity's <c>NavMeshAgent</c>.
/// Queries <see cref="NavMesh"/> via A* + Funnel string-pulling, smoothly accelerates/decelerates along corners,
/// avoids neighboring agents and non-carving <see cref="NavMeshObstacle"/>s with priority-weighted RVO/separation,
/// and projects its position onto the underlying walkable surface.
/// </summary>
public class NavMeshAgent : Component
{
    private NavMeshPath _path = new();
    private int _cornerIndex;
    private Vector3 _destination;
    private bool _hasDestination;
    private int _pathBuildVersion = -1;
    private readonly NavMeshQueryFilter _filter = new();

    // ---------------------------------------------------------------- Serialized Steering & Shape Settings

    [Range(0f, 100f, 0.1f)]
    public float Speed { get; set; } = 3.5f;

    [Range(0f, 3600f, 5f)]
    public float AngularSpeed { get; set; } = 360f;

    [Range(0.01f, 500f, 0.25f)]
    public float Acceleration { get; set; } = 8.0f;

    [Range(0f, 50f, 0.05f)]
    public float StoppingDistance { get; set; } = 0.1f;

    public bool AutoBraking { get; set; } = true;

    public bool AutoRepath { get; set; } = true;

    [Range(0.05f, 10f, 0.05f)]
    public float Radius { get; set; } = 0.5f;

    [Range(0.1f, 20f, 0.1f)]
    public float Height { get; set; } = 2.0f;

    public float BaseOffset { get; set; } = 0.0f;

    public ObstacleAvoidanceType ObstacleAvoidanceType { get; set; } = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

    /// <summary>Lower value means higher priority (<c>0</c> = immovable VIP, <c>99</c> = yields to everyone).</summary>
    [Range(0f, 99f, 1f)]
    public int AvoidancePriority { get; set; } = 50;

    public int AreaMask
    {
        get => _filter.AreaMask;
        set => _filter.AreaMask = value;
    }

    public bool UpdatePosition { get; set; } = true;

    public bool UpdateRotation { get; set; } = true;

    // ---------------------------------------------------------------- Runtime Read/Write State

    [HideInInspector]
    public bool IsStopped { get; set; }

    [HideInInspector]
    public Vector3 Velocity { get; set; }

    [HideInInspector]
    public Vector3 DesiredVelocity { get; private set; }

    [HideInInspector]
    public Vector3 NextPosition { get; set; }

    [HideInInspector]
    public bool IsOnNavMesh { get; private set; }

    [HideInInspector]
    public bool PathPending => false;

    [HideInInspector]
    public bool HasPath => _path.CornerList.Count >= 2 && _path.Status != NavMeshPathStatus.PathInvalid;

    [HideInInspector]
    public NavMeshPathStatus PathStatus => _path.Status;

    [HideInInspector]
    public NavMeshPath Path
    {
        get => _path;
        set => SetPath(value);
    }

    [HideInInspector]
    public Vector3 SteeringTarget
    {
        get
        {
            IReadOnlyList<Vector3> corners = _path.CornerList;
            if (corners.Count == 0)
            {
                return GroundPosition;
            }

            int idx = Math.Clamp(_cornerIndex, 0, corners.Count - 1);
            return corners[idx];
        }
    }

    [HideInInspector]
    public Vector3 Destination
    {
        get => _hasDestination ? _destination : GroundPosition;
        set => SetDestination(value);
    }

    [HideInInspector]
    public float RemainingDistance
    {
        get
        {
            IReadOnlyList<Vector3> corners = _path.CornerList;
            if (corners.Count == 0 || _path.Status == NavMeshPathStatus.PathInvalid)
            {
                return _hasDestination ? float.PositiveInfinity : 0f;
            }

            Vector3 cur = GroundPosition;
            int idx = Math.Clamp(_cornerIndex, 0, corners.Count - 1);
            float dist = HorizontalDistance(cur, corners[idx]);

            for (int i = idx; i < corners.Count - 1; i++)
            {
                dist += HorizontalDistance(corners[i], corners[i + 1]);
            }

            return dist;
        }
    }

    private Vector3 GroundPosition =>
        GameObject is not null
            ? Transform.WorldPosition - new Vector3(0f, BaseOffset, 0f)
            : NextPosition - new Vector3(0f, BaseOffset, 0f);

    // ---------------------------------------------------------------- Public API

    public override void Awake()
    {
        SnapToNavMeshIfPossible();
    }

    public override void OnEnable()
    {
        SnapToNavMeshIfPossible();
    }

    public float GetAreaCost(int areaIndex) => _filter.GetAreaCost(areaIndex);

    public void SetAreaCost(int areaIndex, float areaCost) => _filter.SetAreaCost(areaIndex, areaCost);

    /// <summary>Computes a path to <paramref name="target"/> and starts steering the agent toward it.</summary>
    public bool SetDestination(Vector3 target)
    {
        _destination = target;
        _hasDestination = true;
        IsStopped = false;

        Vector3 start = GroundPosition;
        NavMeshPath newPath = new();
        bool ok = CalculatePath(target, newPath);

        _path = newPath;
        _cornerIndex = _path.CornerList.Count > 1 ? 1 : 0;
        _pathBuildVersion = NavMesh.Version;

        if (NavMesh.SamplePosition(start, out _, MathF.Max(1.0f, Height * 2f), _filter))
        {
            IsOnNavMesh = true;
        }

        return ok;
    }

    /// <summary>Computes a path from the agent's current position to <paramref name="targetPosition"/> without assigning it.</summary>
    public bool CalculatePath(Vector3 targetPosition, NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return NavMesh.CalculatePath(GroundPosition, targetPosition, _filter, path);
    }

    /// <summary>Assigns a precomputed <see cref="NavMeshPath"/> to the agent.</summary>
    public bool SetPath(NavMeshPath? path)
    {
        if (path is null || path.Status == NavMeshPathStatus.PathInvalid || path.CornerList.Count == 0)
        {
            ResetPath();
            return false;
        }

        _path = path;
        _destination = path.CornerList[^1];
        _hasDestination = true;
        _cornerIndex = path.CornerList.Count > 1 ? 1 : 0;
        _pathBuildVersion = NavMesh.Version;
        IsStopped = false;
        return true;
    }

    /// <summary>Clears the current path and halts goal-seeking steering.</summary>
    public void ResetPath()
    {
        _path.ClearCorners();
        _cornerIndex = 0;
        _hasDestination = false;
        DesiredVelocity = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    /// <summary>Teleports the agent to <paramref name="newPosition"/>, snapping it to the nearest NavMesh surface.</summary>
    public bool Warp(Vector3 newPosition)
    {
        ResetPath();

        if (NavMesh.SamplePosition(newPosition, out NavMeshHit hit, MathF.Max(2.0f, Height * 2f), _filter))
        {
            Vector3 finalWorld = hit.Position + new Vector3(0f, BaseOffset, 0f);
            NextPosition = finalWorld;
            if (GameObject is not null && UpdatePosition)
            {
                Transform.WorldPosition = finalWorld;
            }

            IsOnNavMesh = true;
            return true;
        }

        NextPosition = newPosition;
        if (GameObject is not null && UpdatePosition)
        {
            Transform.WorldPosition = newPosition;
        }

        IsOnNavMesh = false;
        return false;
    }

    /// <summary>Applies a manual world-space displacement constrained to the walkable NavMesh.</summary>
    public void Move(Vector3 offset)
    {
        Vector3 currentGround = GroundPosition;
        Vector3 targetGround = currentGround + offset;

        if (NavMesh.Raycast(currentGround, targetGround, out NavMeshHit hit, _filter))
        {
            targetGround = hit.Position;
        }
        else if (NavMesh.SamplePosition(targetGround, out NavMeshHit snapped, MathF.Max(1.0f, Height), _filter))
        {
            targetGround = snapped.Position;
            IsOnNavMesh = true;
        }

        Vector3 finalWorld = targetGround + new Vector3(0f, BaseOffset, 0f);
        NextPosition = finalWorld;
        if (GameObject is not null && UpdatePosition)
        {
            Transform.WorldPosition = finalWorld;
        }
    }

    public bool Raycast(Vector3 targetPosition, out NavMeshHit hit) =>
        NavMesh.Raycast(GroundPosition, targetPosition, out hit, _filter);

    public bool FindClosestEdge(out NavMeshHit hit) =>
        NavMesh.FindClosestEdge(GroundPosition, out hit, _filter);

    // ---------------------------------------------------------------- Per-Frame Simulation Step

    internal void Step(
        float deltaTime,
        IReadOnlyList<NavMeshAgent> allAgents,
        IReadOnlyList<NavMeshObstacle> allObstacles)
    {
        if (!Enabled || GameObject is null || !GameObject.ActiveInHierarchy || deltaTime <= 0f)
        {
            return;
        }

        // Automatically replan if the NavMesh was re-baked or carved underneath an active destination.
        if (AutoRepath && _hasDestination && _pathBuildVersion != NavMesh.Version)
        {
            SetDestination(_destination);
        }

        Vector3 groundPos = GroundPosition;
        if (NavMesh.SamplePosition(groundPos, out NavMeshHit groundHit, MathF.Max(1.5f, Height * 1.5f), _filter))
        {
            IsOnNavMesh = true;
            groundPos = new Vector3(groundPos.X, groundHit.Position.Y, groundPos.Z);
        }
        else
        {
            IsOnNavMesh = false;
        }

        // Advance corner index as we pass intermediate waypoints.
        IReadOnlyList<Vector3> corners = _path.CornerList;
        float cornerReachRadius = MathF.Max(0.05f, Radius * 0.35f);
        while (_cornerIndex < corners.Count - 1
               && HorizontalDistance(groundPos, corners[_cornerIndex]) <= cornerReachRadius)
        {
            _cornerIndex++;
        }

        // Compute desired velocity toward current corner.
        Vector3 desiredVel = Vector3.Zero;
        if (!IsStopped && corners.Count > 0 && _path.Status != NavMeshPathStatus.PathInvalid)
        {
            float remDist = RemainingDistance;
            if (remDist <= StoppingDistance)
            {
                if (_cornerIndex >= corners.Count - 1)
                {
                    DesiredVelocity = Vector3.Zero;
                    Velocity = MoveTowardsVector(Velocity, Vector3.Zero, Acceleration * 2f * deltaTime);
                    ApplyMovementAndRotation(groundPos, deltaTime);
                    return;
                }
            }
            else
            {
                Vector3 targetCorner = corners[Math.Clamp(_cornerIndex, 0, corners.Count - 1)];
                Vector3 toCorner = new(targetCorner.X - groundPos.X, 0f, targetCorner.Z - groundPos.Z);
                float toCornerLen = toCorner.Length();

                if (toCornerLen > 1e-5f)
                {
                    Vector3 dir = toCorner / toCornerLen;
                    float targetSpeed = Speed;

                    if (AutoBraking && Acceleration > 0.01f)
                    {
                        float stopMargin = MathF.Max(0f, remDist - StoppingDistance);
                        float maxSafeSpeed = MathF.Sqrt(2f * Acceleration * stopMargin);
                        targetSpeed = MathF.Min(targetSpeed, maxSafeSpeed);
                    }

                    desiredVel = dir * targetSpeed;
                }
            }
        }

        DesiredVelocity = desiredVel;

        // Crowd & non-carving obstacle avoidance (RVO / separation steering).
        Vector3 steeredVel = ObstacleAvoidanceType != ObstacleAvoidanceType.NoObstacleAvoidance
            ? ComputeAvoidanceVelocity(groundPos, desiredVel, allAgents, allObstacles)
            : desiredVel;

        // Smooth acceleration toward steered velocity.
        Velocity = MoveTowardsVector(Velocity, steeredVel, MathF.Max(0.1f, Acceleration) * deltaTime);

        ApplyMovementAndRotation(groundPos, deltaTime);
    }

    private void ApplyMovementAndRotation(Vector3 currentGroundPos, float deltaTime)
    {
        Vector3 displacement = Velocity * deltaTime;
        Vector3 candidateGround = currentGroundPos + displacement;

        // Keep movement clamped on the walkable NavMesh when we are on one.
        if (IsOnNavMesh && displacement.LengthSquared() > 1e-10f)
        {
            if (NavMesh.Raycast(currentGroundPos, candidateGround, out NavMeshHit wallHit, _filter))
            {
                // Slide along boundary edge tangent instead of getting stuck dead on corners.
                Vector3 wallNormal = new(wallHit.Normal.X, 0f, wallHit.Normal.Z);
                if (wallNormal.LengthSquared() > 1e-6f)
                {
                    wallNormal = Vector3.Normalize(wallNormal);
                    Vector3 intoWall = wallNormal * MathF.Min(0f, Vector3.Dot(displacement, wallNormal));
                    Vector3 slide = displacement - intoWall;
                    Vector3 slideTarget = wallHit.Position + (slide * 0.9f);
                    if (!NavMesh.Raycast(wallHit.Position, slideTarget, out NavMeshHit secondHit, _filter))
                    {
                        candidateGround = slideTarget;
                    }
                    else
                    {
                        candidateGround = secondHit.Position;
                    }
                }
                else
                {
                    candidateGround = wallHit.Position;
                }
            }

            if (NavMesh.SamplePosition(candidateGround, out NavMeshHit heightSnap, MathF.Max(1.5f, Height * 1.5f), _filter))
            {
                candidateGround = heightSnap.Position;
            }
        }

        Vector3 nextWorld = candidateGround + new Vector3(0f, BaseOffset, 0f);
        NextPosition = nextWorld;

        if (GameObject is null)
        {
            return;
        }

        if (UpdatePosition)
        {
            Transform.WorldPosition = nextWorld;
        }

        if (UpdateRotation)
        {
            Vector3 flatVel = new(Velocity.X, 0f, Velocity.Z);
            if (flatVel.LengthSquared() > 0.0025f)
            {
                Vector3 forward = Vector3.Normalize(flatVel);
                float targetYaw = MathF.Atan2(forward.X, forward.Z);
                Quaternion desiredRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, targetYaw);
                float maxTurnRad = AngularSpeed * (MathF.PI / 180f) * deltaTime;
                Transform.WorldRotation = RotateTowardsQuaternion(Transform.WorldRotation, desiredRot, maxTurnRad);
            }
        }
    }

    private Vector3 ComputeAvoidanceVelocity(
        Vector3 myPos,
        Vector3 desiredVel,
        IReadOnlyList<NavMeshAgent> allAgents,
        IReadOnlyList<NavMeshObstacle> allObstacles)
    {
        Vector3 avoidance = Vector3.Zero;
        float horizon = ObstacleAvoidanceType switch
        {
            ObstacleAvoidanceType.LowQualityObstacleAvoidance => 0.8f,
            ObstacleAvoidanceType.MedQualityObstacleAvoidance => 1.2f,
            ObstacleAvoidanceType.GoodQualityObstacleAvoidance => 1.6f,
            _ => 2.0f
        };

        // 1. Neighboring NavMeshAgents (reciprocal velocity & penetration avoidance weighted by AvoidancePriority).
        foreach (NavMeshAgent other in allAgents)
        {
            if (ReferenceEquals(other, this) || !other.Enabled || other.GameObject is null || !other.GameObject.ActiveInHierarchy)
            {
                continue;
            }

            Vector3 otherPos = other.GroundPosition;
            if (MathF.Abs(otherPos.Y - myPos.Y) > MathF.Max(Height, other.Height))
            {
                continue;
            }

            Vector3 relPos = new(otherPos.X - myPos.X, 0f, otherPos.Z - myPos.Z);
            float combinedRadius = Radius + other.Radius;
            float dist = relPos.Length();

            // Responsibility share based on priority: lower AvoidancePriority number = yields less.
            float weight = PriorityShare(AvoidancePriority, other.AvoidancePriority);

            if (dist < combinedRadius)
            {
                // Immediate overlap separation.
                Vector3 pushDir = dist > 1e-4f
                    ? -relPos / dist
                    : DeterministicTieBreakDirection(GameObject.Id, other.GameObject.Id);
                float overlap = (combinedRadius - dist) / MathF.Max(0.05f, combinedRadius);
                avoidance += pushDir * (Speed * (1.5f + overlap * 2f) * weight);
                continue;
            }

            if (dist > combinedRadius + (Speed * horizon))
            {
                continue;
            }

            // Predictive time-to-closest-approach in XZ.
            Vector3 relVel = new(desiredVel.X - other.Velocity.X, 0f, desiredVel.Z - other.Velocity.Z);
            float relSpeedSq = relVel.LengthSquared();
            if (relSpeedSq < 1e-5f)
            {
                continue;
            }

            float tca = Vector3.Dot(relPos, relVel) / relSpeedSq;
            if (tca <= 0f || tca > horizon)
            {
                continue;
            }

            Vector3 closestSep = relPos - (relVel * tca);
            float closestDist = closestSep.Length();
            if (closestDist >= combinedRadius * 1.15f)
            {
                continue;
            }

            // Steer laterally (consistent right-hand rule bias so head-on agents pass smoothly without deadlock).
            Vector3 forwardDir = relPos / dist;
            Vector3 rightSide = new(forwardDir.Z, 0f, -forwardDir.X);
            Vector3 lateral = closestDist > 1e-4f ? -closestSep / closestDist : rightSide;
            if (Vector3.Dot(lateral, rightSide) < -0.1f && closestDist < combinedRadius * 0.25f)
            {
                lateral = rightSide;
            }

            float urgency = (horizon - tca) / horizon;
            avoidance += lateral * (Speed * urgency * 1.6f * weight);
        }

        // 2. Non-carving NavMeshObstacles (dynamic blockers that don't rebuild the NavMesh).
        foreach (NavMeshObstacle obs in allObstacles)
        {
            if (!obs.Enabled || obs.Carving || obs.GameObject is null || !obs.GameObject.ActiveInHierarchy)
            {
                continue;
            }

            NavMeshCarveVolume vol = obs.GetWorldCarveVolume();
            if (MathF.Abs(vol.Center.Y - myPos.Y) > Height + (vol.Height * 0.5f))
            {
                continue;
            }

            Vector3 rel = new(myPos.X - vol.Center.X, 0f, myPos.Z - vol.Center.Z);
            float obsRadius = obs.WorldAvoidanceRadius + Radius;
            float dist = rel.Length();

            if (dist < obsRadius * 1.35f)
            {
                Vector3 away = dist > 1e-4f ? rel / dist : Vector3.UnitX;
                Vector3 tangent = new(-away.Z, 0f, away.X);
                if (Vector3.Dot(tangent, desiredVel) < 0f)
                {
                    tangent = -tangent;
                }

                float factor = Math.Clamp(1.35f - (dist / MathF.Max(0.05f, obsRadius)), 0f, 1.35f);
                avoidance += ((away * 0.7f) + (tangent * 0.7f)) * (Speed * factor * 1.5f);
            }
        }

        if (avoidance == Vector3.Zero)
        {
            return desiredVel;
        }

        Vector3 combined = desiredVel + avoidance;
        float maxSpeed = MathF.Max(Speed, desiredVel.Length());
        float combinedLen = combined.Length();
        return combinedLen > maxSpeed && maxSpeed > 1e-5f
            ? combined * (maxSpeed / combinedLen)
            : combined;
    }

    private void SnapToNavMeshIfPossible()
    {
        if (GameObject is null)
        {
            return;
        }

        Vector3 ground = GroundPosition;
        if (NavMesh.SamplePosition(ground, out NavMeshHit hit, MathF.Max(1.5f, Height * 1.5f), _filter))
        {
            IsOnNavMesh = true;
            NextPosition = hit.Position + new Vector3(0f, BaseOffset, 0f);
            if (UpdatePosition)
            {
                Transform.WorldPosition = NextPosition;
            }
        }
        else
        {
            NextPosition = Transform.WorldPosition;
        }
    }

    private static float PriorityShare(int myPriority, int otherPriority)
    {
        if (myPriority == otherPriority)
        {
            return 0.5f;
        }

        // Lower integer = higher importance (Unity convention: 0 = highest priority, 99 = lowest).
        return myPriority < otherPriority ? 0.15f : 0.85f;
    }

    private static Vector3 DeterministicTieBreakDirection(Guid a, Guid b)
    {
        int cmp = a.CompareTo(b);
        return cmp >= 0 ? Vector3.UnitX : -Vector3.UnitX;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private static Vector3 MoveTowardsVector(Vector3 current, Vector3 target, float maxDelta)
    {
        Vector3 diff = target - current;
        float dist = diff.Length();
        if (dist <= maxDelta || dist <= 1e-7f)
        {
            return target;
        }

        return current + ((diff / dist) * maxDelta);
    }

    private static Quaternion RotateTowardsQuaternion(Quaternion from, Quaternion to, float maxRadiansDelta)
    {
        float dot = Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), 0f, 1f);
        float angle = 2f * MathF.Acos(dot);
        if (angle <= 1e-5f || maxRadiansDelta >= angle)
        {
            return Quaternion.Normalize(to);
        }

        float t = Math.Clamp(maxRadiansDelta / angle, 0f, 1f);
        return Quaternion.Normalize(Quaternion.Slerp(from, to, t));
    }

    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        _path = new NavMeshPath();
        _cornerIndex = 0;
        _hasDestination = false;
        _pathBuildVersion = -1;
        Velocity = Vector3.Zero;
        DesiredVelocity = Vector3.Zero;
    }
}
