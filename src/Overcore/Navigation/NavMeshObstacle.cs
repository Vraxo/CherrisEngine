using Overcore.Attributes;
using Overcore.Core;
using Overcore.Navigation;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>Shape of a <see cref="NavMeshObstacle"/>.</summary>
public enum NavMeshObstacleShape
{
    Box,
    Capsule
}

/// <summary>
/// Dynamic or stationary navigation blocker — Unity's <c>NavMeshObstacle</c>.
/// When <see cref="Carving"/> is enabled, stationary movement beyond <see cref="CarvingMoveThreshold"/> triggers
/// active <see cref="NavMeshSurface"/>s in the scene to re-carve impassable holes around the obstacle; when
/// <see cref="Carving"/> is false, <see cref="NavMeshAgent"/>s steer around it locally via avoidance.
/// </summary>
public class NavMeshObstacle : Component
{
    public NavMeshObstacleShape Shape { get; set; } = NavMeshObstacleShape.Box;

    /// <summary>Local-space centre offset relative to the <see cref="Transform"/>.</summary>
    public Vector3 Center { get; set; } = Vector3.Zero;

    /// <summary>Box full extents in local space (used when <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Box"/>).</summary>
    public Vector3 Size { get; set; } = Vector3.One;

    /// <summary>Capsule radius in local space (used when <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Capsule"/>).</summary>
    [Range(0.05f, 100f, 0.05f)]
    public float Radius { get; set; } = 0.5f;

    /// <summary>Capsule height in local space (used when <see cref="Shape"/> is <see cref="NavMeshObstacleShape.Capsule"/>).</summary>
    [Range(0.1f, 200f, 0.05f)]
    public float Height { get; set; } = 2f;

    /// <summary>When true, cuts an impassable hole directly into baked <see cref="NavMeshSurface"/>s.</summary>
    public bool Carving { get; set; } = true;

    /// <summary>Minimum world movement in metres before a carving obstacle triggers a NavMesh re-carve.</summary>
    [Range(0.01f, 10f, 0.05f)]
    public float CarvingMoveThreshold { get; set; } = 0.1f;

    /// <summary>Seconds a moving carving obstacle waits after settling before re-carving (<c>0</c> = immediate).</summary>
    [Range(0f, 5f, 0.05f)]
    public float TimeToStationary { get; set; } = 0f;

    private Vector3 _lastCarvedCenter;
    private Quaternion _lastCarvedRotation = Quaternion.Identity;
    private Vector3 _lastCarvedScale = Vector3.One;
    private Vector3 _lastObservedCenter;
    private float _stationaryTimer;
    private bool _hasCarvedState;
    private bool _dirtyPending;

    /// <summary>Computes the obstacle's current world-space <see cref="NavMeshCarveVolume"/>.</summary>
    public NavMeshCarveVolume GetWorldCarveVolume()
    {
        if (GameObject is null)
        {
            return new NavMeshCarveVolume(
                Shape == NavMeshObstacleShape.Capsule,
                Center,
                Quaternion.Identity,
                Vector3.Abs(Size) * 0.5f,
                Radius,
                Height);
        }

        Transform.GetWorldTransform(out Vector3 pos, out Quaternion rot, out Vector3 rawScale);
        Vector3 scale = Vector3.Abs(rawScale);
        Vector3 worldCenter = pos + Vector3.Transform(Center * scale, rot);

        if (Shape == NavMeshObstacleShape.Capsule)
        {
            float r = MathF.Max(0.01f, Radius * MathF.Max(scale.X, scale.Z));
            float h = MathF.Max(r * 2f, Height * scale.Y);
            return new NavMeshCarveVolume(true, worldCenter, rot, new Vector3(r, h * 0.5f, r), r, h);
        }

        Vector3 halfExtents = Vector3.Max(Vector3.Abs(Size) * scale * 0.5f, new Vector3(0.01f));
        float approxRadius = MathF.Sqrt((halfExtents.X * halfExtents.X) + (halfExtents.Z * halfExtents.Z));
        return new NavMeshCarveVolume(false, worldCenter, rot, halfExtents, approxRadius, halfExtents.Y * 2f);
    }

    /// <summary>Effective horizontal avoidance radius in world space.</summary>
    [HideInInspector]
    public float WorldAvoidanceRadius
    {
        get
        {
            NavMeshCarveVolume v = GetWorldCarveVolume();
            return v.IsCapsule ? v.Radius : MathF.Max(v.HalfExtents.X, v.HalfExtents.Z);
        }
    }

    /// <summary>Checks whether a carving obstacle moved past <see cref="CarvingMoveThreshold"/> and needs a surface rebuild.</summary>
    internal bool PollCarveDirty(float deltaTime)
    {
        if (!Enabled || !Carving || GameObject is null || !GameObject.ActiveInHierarchy)
        {
            if (_hasCarvedState)
            {
                _hasCarvedState = false;
                return true;
            }

            return false;
        }

        Transform.GetWorldTransform(out Vector3 pos, out Quaternion rot, out Vector3 scale);
        Vector3 worldCenter = pos + Vector3.Transform(Center * Vector3.Abs(scale), rot);

        if (!_hasCarvedState)
        {
            _lastCarvedCenter = worldCenter;
            _lastObservedCenter = worldCenter;
            _lastCarvedRotation = rot;
            _lastCarvedScale = scale;
            _hasCarvedState = true;
            return true;
        }

        float moveDist = Vector3.Distance(worldCenter, _lastCarvedCenter);
        float rotDot = MathF.Abs(Quaternion.Dot(rot, _lastCarvedRotation));
        bool scaleChanged = Vector3.DistanceSquared(scale, _lastCarvedScale) > 1e-4f;

        if (moveDist >= CarvingMoveThreshold || rotDot < 0.995f || scaleChanged)
        {
            _dirtyPending = true;
        }

        if (!_dirtyPending)
        {
            _lastObservedCenter = worldCenter;
            return false;
        }

        float frameMove = Vector3.Distance(worldCenter, _lastObservedCenter);
        _lastObservedCenter = worldCenter;

        if (TimeToStationary > 0f && frameMove > 0.005f)
        {
            _stationaryTimer = 0f;
            return false;
        }

        _stationaryTimer += deltaTime;
        if (_stationaryTimer < TimeToStationary)
        {
            return false;
        }

        _dirtyPending = false;
        _stationaryTimer = 0f;
        _lastCarvedCenter = worldCenter;
        _lastCarvedRotation = rot;
        _lastCarvedScale = scale;
        return true;
    }

    internal void MarkCarvedSnapshot()
    {
        if (GameObject is null)
        {
            return;
        }

        Transform.GetWorldTransform(out Vector3 pos, out Quaternion rot, out Vector3 scale);
        _lastCarvedCenter = pos + Vector3.Transform(Center * Vector3.Abs(scale), rot);
        _lastObservedCenter = _lastCarvedCenter;
        _lastCarvedRotation = rot;
        _lastCarvedScale = scale;
        _hasCarvedState = true;
        _dirtyPending = false;
        _stationaryTimer = 0f;
    }

    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        _hasCarvedState = false;
        _dirtyPending = false;
        _stationaryTimer = 0f;
    }
}
