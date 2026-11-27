using Overcore.Components;
using System.Numerics;

namespace Overcore.Core.Physics;

/// <summary>
/// The physics queries every script reaches for, Unity's static <c>Physics</c>: fire a ray, cast a shape, ask what
/// is inside a volume — and the few switches that decide what the answers mean.
/// </summary>
/// <remarks>
/// <para>
/// A query looks at the colliders of the running world (<see cref="PhysicsSystem.Active"/>) as they are posed at
/// the moment it is asked: right after the physics step for a moving body, and wherever the object is otherwise.
/// With no world running — a test, an editor that has not pressed play — every query answers "nothing", which is
/// safer than throwing at the caller.
/// </para>
/// <para>
/// Casts are exact for boxes and spheres. A capsule is a line with a radius, so a ray that grazes its round end is
/// reported a fraction early; a sphere cast at a box is measured against the box grown by the cast's radius, so its
/// corners read a little far.
/// </para>
/// </remarks>
public static class Physics
{
    private static readonly bool[,] IgnoredLayers = new bool[Layers.Count, Layers.Count];

    private static Vector3 _gravity = PhysicsSystem.DefaultGravity;
    private static bool _queriesHitTriggers = true;

    /// <summary>
    /// Gravity, in metres per second squared, applied to every dynamic body. Setting it reaches the running world
    /// on its next step.
    /// </summary>
    public static Vector3 Gravity
    {
        get => PhysicsSystem.Active?.Gravity ?? _gravity;
        set
        {
            _gravity = value;

            if (PhysicsSystem.Active is { } world)
            {
                world.Gravity = value;
            }
        }
    }

    /// <summary>True when queries report colliders marked <see cref="Collider.IsTrigger"/>; true by default, as in Unity.</summary>
    public static bool QueriesHitTriggers
    {
        get => _queriesHitTriggers;
        set => _queriesHitTriggers = value;
    }

    /// <summary>The layers a query looks at unless it is told otherwise: everything but <see cref="Layers.IgnoreRaycast"/>.</summary>
    public static int DefaultRaycastLayers => ~(1 << Layers.IgnoreRaycast);

    /// <summary>Every layer.</summary>
    public static int AllLayers => ~0;

    /// <summary>Where the physics world keeps its fixed step, in seconds. Shorthand for <see cref="PhysicsSystem.FixedTimeStep"/>.</summary>
    public static float FixedDeltaTime
    {
        get => Time.FixedDeltaTime;
        set => Time.FixedDeltaTime = value;
    }

    // ---- Rays -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Casts a ray and reports the closest collider it meets, Unity's <c>Physics.Raycast</c>.
    /// </summary>
    /// <param name="origin">Where the ray starts, in world space.</param>
    /// <param name="direction">Which way it goes; it is normalized, so its length does not matter.</param>
    /// <param name="hit">Receives what was hit, when something was.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    /// <returns>True when the ray met something.</returns>
    public static bool Raycast(
        Vector3 origin,
        Vector3 direction,
        out RaycastHit hit,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        hit = default;

        if (direction.LengthSquared() < 1.0e-12f || !TryGetWorld(out PhysicsSystem? world))
        {
            return false;
        }

        Vector3 unit = Vector3.Normalize(direction);
        float reach = Limit(maxDistance);
        float closest = reach;
        bool found = false;

        foreach (Collider collider in world.Colliders)
        {
            if (!IsCandidate(collider, layerMask, queryTriggerInteraction))
            {
                continue;
            }

            if (!ColliderGeometry.Raycast(
                ColliderGeometry.From(collider), origin, unit, closest, out float distance, out Vector3 point, out Vector3 normal))
            {
                continue;
            }

            if (distance > closest)
            {
                continue;
            }

            closest = distance;
            found = true;
            hit = new RaycastHit
            {
                Collider = collider,
                Point = point,
                Normal = normal,
                Distance = distance,
                Origin = origin
            };
        }

        return found;
    }

    /// <summary>Casts a ray and reports the closest collider it meets.</summary>
    /// <param name="origin">Where the ray starts, in world space.</param>
    /// <param name="direction">Which way it goes.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    /// <param name="layerMask">The layers to look at.</param>
    public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask = ~0) =>
        Raycast(origin, direction, out _, maxDistance, layerMask);

    /// <summary>Casts a prepared ray and reports the closest collider it meets.</summary>
    /// <param name="ray">The ray to cast.</param>
    /// <param name="hit">Receives what was hit, when something was.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static bool Raycast(
        Ray ray,
        out RaycastHit hit,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        Raycast(ray.Origin, ray.Direction, out hit, maxDistance, layerMask, queryTriggerInteraction);

    /// <summary>Every collider a ray meets, nearest first, Unity's <c>Physics.RaycastAll</c>.</summary>
    /// <param name="origin">Where the ray starts, in world space.</param>
    /// <param name="direction">Which way it goes.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static RaycastHit[] RaycastAll(
        Vector3 origin,
        Vector3 direction,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (direction.LengthSquared() < 1.0e-12f || !TryGetWorld(out PhysicsSystem? world))
        {
            return [];
        }

        Vector3 unit = Vector3.Normalize(direction);
        float reach = Limit(maxDistance);
        List<RaycastHit> hits = [];

        foreach (Collider collider in world.Colliders)
        {
            if (!IsCandidate(collider, layerMask, queryTriggerInteraction))
            {
                continue;
            }

            if (!ColliderGeometry.Raycast(
                ColliderGeometry.From(collider), origin, unit, reach, out float distance, out Vector3 point, out Vector3 normal))
            {
                continue;
            }

            hits.Add(new RaycastHit
            {
                Collider = collider,
                Point = point,
                Normal = normal,
                Distance = distance,
                Origin = origin
            });
        }

        hits.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        return [.. hits];
    }

    /// <summary>
    /// Checks the straight line between two places and reports the first collider on it, Unity's <c>Physics.Linecast</c>.
    /// </summary>
    /// <param name="start">Where the line starts, in world space.</param>
    /// <param name="end">Where it ends, in world space.</param>
    /// <param name="hit">Receives what was hit, when something was.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static bool Linecast(
        Vector3 start,
        Vector3 end,
        out RaycastHit hit,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        Vector3 direction = end - start;
        return Raycast(start, direction, out hit, direction.Length(), layerMask, queryTriggerInteraction);
    }

    /// <summary>Checks the straight line between two places, Unity's <c>Physics.Linecast</c>.</summary>
    /// <param name="start">Where the line starts, in world space.</param>
    /// <param name="end">Where it ends, in world space.</param>
    /// <param name="layerMask">The layers to look at.</param>
    public static bool Linecast(Vector3 start, Vector3 end, int layerMask = ~0) =>
        Linecast(start, end, out _, layerMask);

    // ---- Casts ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sweeps a sphere along a ray and reports the first collider it would touch, Unity's <c>Physics.SphereCast</c>.
    /// </summary>
    /// <param name="origin">Where the sphere starts, in world space.</param>
    /// <param name="radius">The sphere's radius.</param>
    /// <param name="direction">Which way it is swept.</param>
    /// <param name="hit">Receives what was hit, when something was.</param>
    /// <param name="maxDistance">How far the sphere is swept.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static bool SphereCast(
        Vector3 origin,
        float radius,
        Vector3 direction,
        out RaycastHit hit,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        hit = default;

        if (radius <= 0.0f || direction.LengthSquared() < 1.0e-12f || !TryGetWorld(out PhysicsSystem? world))
        {
            return false;
        }

        Vector3 unit = Vector3.Normalize(direction);
        float reach = Limit(maxDistance);
        float closest = reach;
        bool found = false;

        foreach (Collider collider in world.Colliders)
        {
            if (!IsCandidate(collider, layerMask, queryTriggerInteraction))
            {
                continue;
            }

            ShapeInstance shape = Inflate(ColliderGeometry.From(collider), radius);

            if (!ColliderGeometry.Raycast(shape, origin, unit, closest, out float distance, out Vector3 point, out Vector3 normal))
            {
                continue;
            }

            if (distance > closest)
            {
                continue;
            }

            closest = distance;
            found = true;
            hit = new RaycastHit
            {
                Collider = collider,
                Point = point - (normal * radius),
                Normal = normal,
                Distance = distance,
                Origin = origin
            };
        }

        return found;
    }

    /// <summary>Every collider a swept sphere would touch, nearest first, Unity's <c>Physics.SphereCastAll</c>.</summary>
    /// <param name="origin">Where the sphere starts, in world space.</param>
    /// <param name="radius">The sphere's radius.</param>
    /// <param name="direction">Which way it is swept.</param>
    /// <param name="maxDistance">How far the sphere is swept.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static RaycastHit[] SphereCastAll(
        Vector3 origin,
        float radius,
        Vector3 direction,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (radius <= 0.0f || direction.LengthSquared() < 1.0e-12f || !TryGetWorld(out PhysicsSystem? world))
        {
            return [];
        }

        Vector3 unit = Vector3.Normalize(direction);
        float reach = Limit(maxDistance);
        List<RaycastHit> hits = [];

        foreach (Collider collider in world.Colliders)
        {
            if (!IsCandidate(collider, layerMask, queryTriggerInteraction))
            {
                continue;
            }

            ShapeInstance shape = Inflate(ColliderGeometry.From(collider), radius);

            if (!ColliderGeometry.Raycast(shape, origin, unit, reach, out float distance, out Vector3 point, out Vector3 normal))
            {
                continue;
            }

            hits.Add(new RaycastHit
            {
                Collider = collider,
                Point = point - (normal * radius),
                Normal = normal,
                Distance = distance,
                Origin = origin
            });
        }

        hits.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        return [.. hits];
    }

    /// <summary>
    /// Sweeps an upright or arbitrarily oriented capsule through the active physics world using conservative
    /// advancement over capsule-to-collider separation. The controller component uses the internal overload to
    /// ignore its own object.
    /// </summary>
    public static bool CapsuleCast(
        Vector3 point1,
        Vector3 point2,
        float radius,
        Vector3 direction,
        out RaycastHit hit,
        float maxDistance = float.PositiveInfinity,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        CapsuleCastCore(point1, point2, radius, direction, out hit, maxDistance, layerMask, queryTriggerInteraction, null);

    internal static bool CapsuleCastForController(
        Vector3 point1, Vector3 point2, float radius, Vector3 displacement, GameObject owner, out RaycastHit hit) =>
        CapsuleCastCore(point1, point2, radius, displacement, out hit, displacement.Length(), ~0,
            QueryTriggerInteraction.Ignore, owner);

    internal static Collider[] ControllerTriggerColliders(GameObject owner)
    {
        if (!TryGetWorld(out PhysicsSystem? world)) return [];
        return [.. world.Colliders.Where(collider =>
            collider.IsTrigger && !collider.IsDestroyed && collider.IsActive &&
            collider.GameObject is { ActiveInHierarchy: true } gameObject &&
            !ReferenceEquals(gameObject, owner) && !IgnoredLayers[owner.Layer, gameObject.Layer])];
    }

    internal static bool CapsuleCastTriggerForController(
        Collider trigger,
        Vector3 point1,
        Vector3 point2,
        float radius,
        Vector3 displacement,
        GameObject owner)
    {
        float distance = displacement.Length();
        if (distance <= 1e-6f || trigger.IsDestroyed || !trigger.IsActive ||
            trigger.GameObject is not { ActiveInHierarchy: true } triggerObject ||
            ReferenceEquals(triggerObject, owner) || IgnoredLayers[owner.Layer, triggerObject.Layer]) return false;

        Vector3 radiusVector = new(radius);
        Vector3 sweepMin = Vector3.Min(Vector3.Min(point1, point2),
            Vector3.Min(point1 + displacement, point2 + displacement)) - radiusVector;
        Vector3 sweepMax = Vector3.Max(Vector3.Max(point1, point2),
            Vector3.Max(point1 + displacement, point2 + displacement)) + radiusVector;
        var triggerBounds = trigger.Bounds;
        if (!BoundsOverlap(sweepMin, sweepMax, triggerBounds.Min, triggerBounds.Max)) return false;

        return ColliderGeometry.CapsuleCast(
            ColliderGeometry.From(trigger), point1, point2, radius, displacement / distance, distance,
            out _, out _, out _);
    }

    internal static Collider[] OverlapTriggersForController(
        Vector3 point1, Vector3 point2, float radius, GameObject owner)
    {
        if (!TryGetWorld(out PhysicsSystem? world)) return [];

        ShapeInstance controllerShape = new()
        {
            Kind = ShapeKind.Capsule,
            Center = (point1 + point2) * 0.5f,
            Radius = radius,
            SegmentA = point1,
            SegmentB = point2
        };
        Vector3 radiusVector = new(radius);
        Vector3 controllerMin = Vector3.Min(point1, point2) - radiusVector;
        Vector3 controllerMax = Vector3.Max(point1, point2) + radiusVector;
        List<Collider> overlaps = [];
        foreach (Collider collider in world.Colliders)
        {
            GameObject? gameObject = collider.GameObject;
            if (!collider.IsTrigger || collider.IsDestroyed || !collider.IsActive || gameObject is null ||
                !gameObject.ActiveInHierarchy || ReferenceEquals(gameObject, owner) ||
                IgnoredLayers[owner.Layer, gameObject.Layer]) continue;

            var triggerBounds = collider.Bounds;
            if (!BoundsOverlap(controllerMin, controllerMax, triggerBounds.Min, triggerBounds.Max)) continue;
            if (ColliderGeometry.Overlaps(controllerShape, ColliderGeometry.From(collider))) overlaps.Add(collider);
        }
        return [.. overlaps];
    }

    private static bool BoundsOverlap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X &&
        minA.Y <= maxB.Y && maxA.Y >= minB.Y &&
        minA.Z <= maxB.Z && maxA.Z >= minB.Z;

    internal static bool TryGetCapsulePenetration(
        Vector3 point1, Vector3 point2, float radius, GameObject owner, out Vector3 correction, out float depth)
    {
        correction = Vector3.Zero;
        depth = 0f;
        if (!TryGetWorld(out PhysicsSystem? world)) return false;

        Vector3 deepestNormal = Vector3.Zero;
        foreach (Collider collider in world.Colliders)
        {
            GameObject? gameObject = collider.GameObject;
            if (collider.IsDestroyed || !collider.IsActive || gameObject is null || !gameObject.ActiveInHierarchy ||
                collider.IsTrigger || ReferenceEquals(gameObject, owner) ||
                IgnoredLayers[owner.Layer, gameObject.Layer]) continue;

            float separation = ColliderGeometry.CapsuleSeparation(
                ColliderGeometry.From(collider), point1, point2, radius, out Vector3 normal);
            if (separation >= -depth) continue;
            depth = -separation;
            deepestNormal = normal;
        }

        if (depth <= 0f) return false;
        correction = deepestNormal * depth;
        return true;
    }

    private static bool CapsuleCastCore(
        Vector3 point1,
        Vector3 point2,
        float radius,
        Vector3 direction,
        out RaycastHit hit,
        float maxDistance,
        int layerMask,
        QueryTriggerInteraction queryTriggerInteraction,
        GameObject? ignoredObject)
    {
        hit = default;
        if (radius <= 0f || direction.LengthSquared() < 1e-12f || float.IsNaN(maxDistance) || maxDistance <= 0f ||
            !TryGetWorld(out PhysicsSystem? world)) return false;

        float distanceLimit = maxDistance;
        bool hitTriggers = queryTriggerInteraction switch
        {
            QueryTriggerInteraction.Ignore => false,
            QueryTriggerInteraction.Collide => true,
            _ => QueriesHitTriggers
        };
        Vector3 unit = Vector3.Normalize(direction);
        bool hitAnything = false;
        float closest = distanceLimit;

        foreach (Collider collider in world.Colliders)
        {
            GameObject? gameObject = collider.GameObject;
            if (collider.IsDestroyed || !collider.IsActive || gameObject is null || !gameObject.ActiveInHierarchy ||
                (collider.IsTrigger && !hitTriggers) || ReferenceEquals(gameObject, ignoredObject) ||
                (layerMask & (1 << gameObject.Layer)) == 0 ||
                (ignoredObject is not null && IgnoredLayers[ignoredObject.Layer, gameObject.Layer]))
                continue;

            ShapeInstance shape = ColliderGeometry.From(collider);
            if (!ColliderGeometry.CapsuleCast(shape, point1, point2, radius, unit, closest,
                out float travelled, out Vector3 point, out Vector3 normal)) continue;
            if (travelled > closest) continue;

            closest = travelled;
            hitAnything = true;
            hit = new RaycastHit
            {
                Collider = collider,
                Distance = travelled,
                Point = point,
                Normal = normal,
                Origin = point1
            };
        }
        return hitAnything;
    }

    // ---- Volumes ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Every collider overlapping a sphere, Unity's <c>Physics.OverlapSphere</c>.
    /// </summary>
    /// <param name="position">The sphere's centre, in world space.</param>
    /// <param name="radius">The sphere's radius.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static Collider[] OverlapSphere(
        Vector3 position,
        float radius,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (radius <= 0.0f || !TryGetWorld(out PhysicsSystem? world))
        {
            return [];
        }

        List<Collider> found = [];

        foreach (Collider collider in world.Colliders)
        {
            if (IsCandidate(collider, layerMask, queryTriggerInteraction)
                && ColliderGeometry.OverlapsSphere(ColliderGeometry.From(collider), position, radius))
            {
                found.Add(collider);
            }
        }

        return [.. found];
    }

    /// <summary>True when anything overlaps a sphere, Unity's <c>Physics.CheckSphere</c>.</summary>
    /// <param name="position">The sphere's centre, in world space.</param>
    /// <param name="radius">The sphere's radius.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static bool CheckSphere(
        Vector3 position,
        float radius,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (radius <= 0.0f || !TryGetWorld(out PhysicsSystem? world))
        {
            return false;
        }

        foreach (Collider collider in world.Colliders)
        {
            if (IsCandidate(collider, layerMask, queryTriggerInteraction)
                && ColliderGeometry.OverlapsSphere(ColliderGeometry.From(collider), position, radius))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every collider overlapping a box, Unity's <c>Physics.OverlapBox</c>.</summary>
    /// <param name="center">The box's centre, in world space.</param>
    /// <param name="halfExtents">Half the box's size along each of its axes.</param>
    /// <param name="orientation">How the box is turned; an unrotated box when omitted.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static Collider[] OverlapBox(
        Vector3 center,
        Vector3 halfExtents,
        Quaternion orientation = default,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (!TryGetWorld(out PhysicsSystem? world))
        {
            return [];
        }

        Quaternion rotation = orientation == default ? Quaternion.Identity : orientation;
        List<Collider> found = [];

        foreach (Collider collider in world.Colliders)
        {
            if (IsCandidate(collider, layerMask, queryTriggerInteraction)
                && ColliderGeometry.OverlapsBox(ColliderGeometry.From(collider), center, rotation, halfExtents))
            {
                found.Add(collider);
            }
        }

        return [.. found];
    }

    /// <summary>True when anything overlaps a box, Unity's <c>Physics.CheckBox</c>.</summary>
    /// <param name="center">The box's centre, in world space.</param>
    /// <param name="halfExtents">Half the box's size along each of its axes.</param>
    /// <param name="orientation">How the box is turned; an unrotated box when omitted.</param>
    /// <param name="layerMask">The layers to look at.</param>
    /// <param name="queryTriggerInteraction">Whether triggers count as hits.</param>
    public static bool CheckBox(
        Vector3 center,
        Vector3 halfExtents,
        Quaternion orientation = default,
        int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        if (!TryGetWorld(out PhysicsSystem? world))
        {
            return false;
        }

        Quaternion rotation = orientation == default ? Quaternion.Identity : orientation;

        foreach (Collider collider in world.Colliders)
        {
            if (IsCandidate(collider, layerMask, queryTriggerInteraction)
                && ColliderGeometry.OverlapsBox(ColliderGeometry.From(collider), center, rotation, halfExtents))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Layers -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Stops two layers from colliding, Unity's <c>Physics.IgnoreLayerCollision</c>: contacts between them are
    /// never solved, and trigger overlaps are never reported.
    /// </summary>
    /// <param name="layer1">One layer, 0 to 31.</param>
    /// <param name="layer2">The other layer.</param>
    /// <param name="ignore">True to let them pass through each other, false to let them collide again.</param>
    public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore = true)
    {
        if (layer1 is < 0 or >= Layers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(layer1), layer1, "A layer is a number from 0 to 31.");
        }

        if (layer2 is < 0 or >= Layers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(layer2), layer2, "A layer is a number from 0 to 31.");
        }

        IgnoredLayers[layer1, layer2] = ignore;
        IgnoredLayers[layer2, layer1] = ignore;
    }

    /// <summary>True when two layers have been told to pass through each other — Unity's <c>GetIgnoreLayerCollision</c>.</summary>
    /// <param name="layer1">One layer, 0 to 31.</param>
    /// <param name="layer2">The other layer.</param>
    public static bool GetIgnoreLayerCollision(int layer1, int layer2) =>
        IgnoredLayers[layer1, layer2];

    /// <summary>True when two colliders may touch at all, as far as their layers are concerned.</summary>
    /// <param name="a">One collider.</param>
    /// <param name="b">The other collider.</param>
    internal static bool LayersInteract(Collider a, Collider b) =>
        !IgnoredLayers[LayerOf(a), LayerOf(b)];

    /// <summary>Puts the layer switches back to how a new world starts: everything collides with everything.</summary>
    public static void ResetLayers()
    {
        Array.Clear(IgnoredLayers);
    }

    private static int LayerOf(Collider collider) => collider.GameObject?.Layer ?? Layers.Default;

    private static bool TryGetWorld(out PhysicsSystem world)
    {
        if (PhysicsSystem.Active is { } active)
        {
            world = active;
            return true;
        }

        world = null!;
        return false;
    }

    private static bool IsCandidate(Collider collider, int layerMask, QueryTriggerInteraction queryTriggerInteraction)
    {
        if (collider.IsDestroyed || !collider.IsActive || collider.GameObject is not { } gameObject)
        {
            return false;
        }

        if (!gameObject.ActiveInHierarchy)
        {
            return false;
        }

        if ((layerMask & (1 << gameObject.Layer)) == 0)
        {
            return false;
        }

        bool hitTriggers = queryTriggerInteraction switch
        {
            QueryTriggerInteraction.Ignore => false,
            QueryTriggerInteraction.Collide => true,
            _ => QueriesHitTriggers
        };

        return !collider.IsTrigger || hitTriggers;
    }

    private static float Limit(float maxDistance) =>
        maxDistance <= 0.0f || float.IsInfinity(maxDistance) ? float.PositiveInfinity : maxDistance;

    /// <summary>Grows a shape by a radius, which is what turning a ray into a sphere cast means.</summary>
    private static ShapeInstance Inflate(in ShapeInstance shape, float radius)
    {
        return shape.Kind switch
        {
            ShapeKind.Box => new ShapeInstance
            {
                Kind = ShapeKind.Box,
                Center = shape.Center,
                Rotation = shape.Rotation,
                HalfExtents = shape.HalfExtents + new Vector3(radius)
            },
            ShapeKind.Capsule => new ShapeInstance
            {
                Kind = ShapeKind.Capsule,
                Center = shape.Center,
                Rotation = shape.Rotation,
                Radius = shape.Radius + radius,
                SegmentA = shape.SegmentA,
                SegmentB = shape.SegmentB
            },
            _ => new ShapeInstance
            {
                Kind = ShapeKind.Sphere,
                Center = shape.Center,
                Rotation = shape.Rotation,
                Radius = shape.Radius + radius
            }
        };
    }
}
