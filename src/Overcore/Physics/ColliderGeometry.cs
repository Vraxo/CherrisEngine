using Overcore.Components;
using System.Numerics;

namespace Overcore.Core.Physics;

/// <summary>Which of the three shapes a collider is.</summary>
internal enum ShapeKind
{
    Box,
    Sphere,
    Capsule
}

/// <summary>
/// A collider's shape, posed in the world and ready to be tested: the one description every query and every
/// overlap test works from, whatever component it came from.
/// </summary>
internal readonly struct ShapeInstance
{
    public ShapeKind Kind { get; init; }

    /// <summary>Centre of the shape in world space; for a capsule, the middle of its line.</summary>
    public Vector3 Center { get; init; }

    public Quaternion Rotation { get; init; }

    /// <summary>Half the box's size along its own axes.</summary>
    public Vector3 HalfExtents { get; init; }

    /// <summary>The sphere's radius, or the capsule's.</summary>
    public float Radius { get; init; }

    /// <summary>One end of the capsule's line.</summary>
    public Vector3 SegmentA { get; init; }

    /// <summary>The other end of the capsule's line.</summary>
    public Vector3 SegmentB { get; init; }

    /// <summary>The box's own X axis in world space.</summary>
    public Vector3 AxisX => Vector3.Transform(Vector3.UnitX, Rotation);

    /// <summary>The box's own Y axis in world space.</summary>
    public Vector3 AxisY => Vector3.Transform(Vector3.UnitY, Rotation);

    /// <summary>The box's own Z axis in world space.</summary>
    public Vector3 AxisZ => Vector3.Transform(Vector3.UnitZ, Rotation);
}

/// <summary>
/// The shape arithmetic behind the physics queries: rays against boxes, spheres and capsules; spheres and boxes
/// against the same; and the shape-against-shape tests trigger colliders use.
/// </summary>
/// <remarks>
/// Every test works from a <see cref="ShapeInstance"/>, so a query does not care whether the shape came from a
/// collider on a moving body or from one that never moves. Rays and casts are exact for boxes and spheres; a
/// capsule is treated as its line plus its radius, so a ray grazing the round cap is reported a little early.
/// </remarks>
internal static class ColliderGeometry
{
    private const float Epsilon = 1.0e-6f;

    /// <summary>Poses a collider's shape in the world.</summary>
    /// <param name="collider">The collider to pose.</param>
    public static ShapeInstance From(Collider collider)
    {
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation, out Vector3 scale);
        Vector3 absoluteScale = Vector3.Abs(scale);

        switch (collider)
        {
            case BoxCollider box:
            {
                Vector3 size = Vector3.Abs(box.Size) * absoluteScale;
                return new ShapeInstance
                {
                    Kind = ShapeKind.Box,
                    Center = position,
                    Rotation = rotation,
                    HalfExtents = size * 0.5f
                };
            }

            case SphereCollider sphere:
            {
                float uniform = MaxComponent(absoluteScale);
                return new ShapeInstance
                {
                    Kind = ShapeKind.Sphere,
                    Center = position,
                    Rotation = rotation,
                    Radius = MathF.Max(sphere.Radius * uniform, Epsilon)
                };
            }

            case CapsuleCollider capsule:
            {
                float radius = MathF.Max(capsule.Radius * MathF.Max(absoluteScale.X, absoluteScale.Z), Epsilon);
                float height = MathF.Max(capsule.Height * absoluteScale.Y, radius * 2.0f);
                float halfLine = MathF.Max((height * 0.5f) - radius, 0.0f);
                Vector3 axis = Vector3.Transform(Vector3.UnitY, rotation) * halfLine;

                return new ShapeInstance
                {
                    Kind = ShapeKind.Capsule,
                    Center = position,
                    Rotation = rotation,
                    Radius = radius,
                    SegmentA = position - axis,
                    SegmentB = position + axis
                };
            }

            default:
                return new ShapeInstance
                {
                    Kind = ShapeKind.Sphere,
                    Center = position,
                    Rotation = rotation,
                    Radius = Epsilon
                };
        }
    }

    /// <summary>The smallest box, in world space, that holds the shape.</summary>
    /// <param name="shape">The shape to bound.</param>
    public static BoundingBox Bounds(in ShapeInstance shape)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Box:
            {
                Vector3 x = shape.AxisX * shape.HalfExtents.X;
                Vector3 y = shape.AxisY * shape.HalfExtents.Y;
                Vector3 z = shape.AxisZ * shape.HalfExtents.Z;
                Vector3 extent = Vector3.Abs(x) + Vector3.Abs(y) + Vector3.Abs(z);

                return new BoundingBox { Min = shape.Center - extent, Max = shape.Center + extent };
            }

            case ShapeKind.Capsule:
            {
                Vector3 radius = new(shape.Radius);
                return new BoundingBox
                {
                    Min = Vector3.Min(shape.SegmentA, shape.SegmentB) - radius,
                    Max = Vector3.Max(shape.SegmentA, shape.SegmentB) + radius
                };
            }

            default:
            {
                Vector3 radius = new(shape.Radius);
                return new BoundingBox { Min = shape.Center - radius, Max = shape.Center + radius };
            }
        }
    }

    /// <summary>True when a point is inside the shape.</summary>
    /// <param name="shape">The shape to test.</param>
    /// <param name="point">The point, in world space.</param>
    public static bool Contains(in ShapeInstance shape, Vector3 point)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Box:
            {
                Vector3 local = ToLocal(shape, point);
                return MathF.Abs(local.X) <= shape.HalfExtents.X + Epsilon
                    && MathF.Abs(local.Y) <= shape.HalfExtents.Y + Epsilon
                    && MathF.Abs(local.Z) <= shape.HalfExtents.Z + Epsilon;
            }

            case ShapeKind.Capsule:
                return DistanceToSegment(shape.SegmentA, shape.SegmentB, point) <= shape.Radius + Epsilon;

            default:
                return Vector3.Distance(point, shape.Center) <= shape.Radius + Epsilon;
        }
    }

    /// <summary>
    /// Casts a ray at the shape.
    /// </summary>
    /// <param name="shape">The shape to cast at.</param>
    /// <param name="origin">Where the ray starts, in world space.</param>
    /// <param name="direction">Which way it goes; must be a unit vector.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    /// <param name="distance">Receives how far along the ray the hit is.</param>
    /// <param name="point">Receives where the ray met the shape.</param>
    /// <param name="normal">Receives which way the surface faces at the hit.</param>
    /// <returns>True when the ray met the shape inside <paramref name="maxDistance"/>.</returns>
    public static bool Raycast(
        in ShapeInstance shape,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out float distance,
        out Vector3 point,
        out Vector3 normal)
    {
        return shape.Kind switch
        {
            ShapeKind.Box => RaycastBox(shape, origin, direction, maxDistance, out distance, out point, out normal),
            ShapeKind.Capsule => RaycastCapsule(shape, origin, direction, maxDistance, out distance, out point, out normal),
            _ => RaycastSphere(shape.Center, shape.Radius, origin, direction, maxDistance, out distance, out point, out normal)
        };
    }

    /// <summary>True when a sphere overlaps the shape.</summary>
    /// <param name="shape">The shape to test.</param>
    /// <param name="center">The sphere's centre, in world space.</param>
    /// <param name="radius">The sphere's radius.</param>
    public static bool OverlapsSphere(in ShapeInstance shape, Vector3 center, float radius)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Box:
            {
                Vector3 closest = ClosestPoint(shape, center);
                return Vector3.DistanceSquared(closest, center) <= radius * radius;
            }

            case ShapeKind.Capsule:
            {
                float sum = radius + shape.Radius;
                return Vector3.DistanceSquared(
                    ClosestPointOnSegment(shape.SegmentA, shape.SegmentB, center),
                    center) <= sum * sum;
            }

            default:
            {
                float sum = radius + shape.Radius;
                return Vector3.DistanceSquared(shape.Center, center) <= sum * sum;
            }
        }
    }

    /// <summary>True when a box overlaps the shape.</summary>
    /// <param name="shape">The shape to test.</param>
    /// <param name="center">The box's centre, in world space.</param>
    /// <param name="rotation">How the box is turned.</param>
    /// <param name="halfExtents">Half the box's size along its own axes.</param>
    public static bool OverlapsBox(in ShapeInstance shape, Vector3 center, Quaternion rotation, Vector3 halfExtents)
    {
        ShapeInstance box = new()
        {
            Kind = ShapeKind.Box,
            Center = center,
            Rotation = rotation,
            HalfExtents = Vector3.Abs(halfExtents)
        };

        switch (shape.Kind)
        {
            case ShapeKind.Box:
                return OverlapsBoxShape(shape, box);

            case ShapeKind.Capsule:
                return DistanceSegmentToBox(shape.SegmentA, shape.SegmentB, box) <= shape.Radius;

            default:
                return OverlapsSphere(box, shape.Center, shape.Radius);
        }
    }

    /// <summary>True when two shapes overlap, which is how a trigger collider finds what is inside it.</summary>
    /// <param name="shape">One shape.</param>
    /// <param name="other">The other shape.</param>
    public static bool Overlaps(in ShapeInstance shape, in ShapeInstance other)
    {
        return (shape.Kind, other.Kind) switch
        {
            (ShapeKind.Box, ShapeKind.Box) => OverlapsBoxShape(shape, other),
            (ShapeKind.Box, ShapeKind.Sphere) => OverlapsSphere(shape, other.Center, other.Radius),
            (ShapeKind.Box, ShapeKind.Capsule) =>
                DistanceSegmentToBox(other.SegmentA, other.SegmentB, shape) <= other.Radius,
            (ShapeKind.Sphere, ShapeKind.Box) => OverlapsSphere(other, shape.Center, shape.Radius),
            (ShapeKind.Sphere, ShapeKind.Sphere) =>
                Vector3.DistanceSquared(shape.Center, other.Center) <= MathF.Pow(shape.Radius + other.Radius, 2),
            (ShapeKind.Sphere, ShapeKind.Capsule) =>
                Vector3.DistanceSquared(
                    ClosestPointOnSegment(other.SegmentA, other.SegmentB, shape.Center),
                    shape.Center) <= MathF.Pow(shape.Radius + other.Radius, 2),
            (ShapeKind.Capsule, ShapeKind.Box) =>
                DistanceSegmentToBox(shape.SegmentA, shape.SegmentB, other) <= shape.Radius,
            (ShapeKind.Capsule, ShapeKind.Sphere) =>
                Vector3.DistanceSquared(
                    ClosestPointOnSegment(shape.SegmentA, shape.SegmentB, other.Center),
                    other.Center) <= MathF.Pow(shape.Radius + other.Radius, 2),
            _ => DistanceSegmentToSegment(shape.SegmentA, shape.SegmentB, other.SegmentA, other.SegmentB)
                <= shape.Radius + other.Radius
        };
    }

    /// <summary>The point of the shape closest to a place in the world.</summary>
    /// <param name="shape">The shape to measure to.</param>
    /// <param name="point">The place, in world space.</param>
    public static Vector3 ClosestPoint(in ShapeInstance shape, Vector3 point)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Box:
            {
                Vector3 local = ToLocal(shape, point);
                Vector3 clamped = Vector3.Clamp(local, -shape.HalfExtents, shape.HalfExtents);
                return shape.Center + Vector3.Transform(clamped, shape.Rotation);
            }

            case ShapeKind.Capsule:
            {
                Vector3 onLine = ClosestPointOnSegment(shape.SegmentA, shape.SegmentB, point);
                Vector3 offset = point - onLine;
                float length = offset.Length();
                return length <= shape.Radius || length < Epsilon
                    ? point
                    : onLine + (offset / length * shape.Radius);
            }

            default:
            {
                Vector3 offset = point - shape.Center;
                float length = offset.Length();
                return length <= shape.Radius || length < Epsilon
                    ? point
                    : shape.Center + (offset / length * shape.Radius);
            }
        }
    }

    /// <summary>The offset from the shape's centre to a place in the shape's own axes.</summary>
    private static Vector3 ToLocal(in ShapeInstance shape, Vector3 point) =>
        Vector3.Transform(point - shape.Center, Quaternion.Inverse(shape.Rotation));

    private static bool RaycastSphere(
        Vector3 center,
        float radius,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out float distance,
        out Vector3 point,
        out Vector3 normal)
    {
        distance = 0.0f;
        point = origin;
        normal = -direction;

        Vector3 toCenter = center - origin;
        float along = Vector3.Dot(toCenter, direction);
        float closestSquared = toCenter.LengthSquared() - (along * along);

        if (closestSquared > radius * radius)
        {
            return false;
        }

        float half = MathF.Sqrt(MathF.Max(radius * radius - closestSquared, 0.0f));
        float entry = along - half;
        float exit = along + half;

        if (exit < 0.0f || entry > maxDistance)
        {
            return false;
        }

        if (entry < 0.0f)
        {
            // The ray starts inside the sphere: the hit is where it starts, and the surface faces back at it.
            distance = 0.0f;
            point = origin;
            normal = Vector3.Normalize(origin - center);
            return true;
        }

        distance = entry;
        point = origin + (direction * entry);
        normal = Vector3.Normalize(point - center);
        return true;
    }

    private static bool RaycastBox(
        in ShapeInstance shape,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out float distance,
        out Vector3 point,
        out Vector3 normal)
    {
        distance = 0.0f;
        point = origin;
        normal = -direction;

        Quaternion inverse = Quaternion.Inverse(shape.Rotation);
        Vector3 localOrigin = Vector3.Transform(origin - shape.Center, inverse);
        Vector3 localDirection = Vector3.Transform(direction, inverse);
        Vector3 half = shape.HalfExtents;

        float entry = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        int entryAxis = -1;
        float entrySign = 0.0f;

        for (int axis = 0; axis < 3; axis++)
        {
            float directionOnAxis = Component(localDirection, axis);
            float originOnAxis = Component(localOrigin, axis);
            float halfOnAxis = Component(half, axis);

            if (MathF.Abs(directionOnAxis) < Epsilon)
            {
                if (MathF.Abs(originOnAxis) > halfOnAxis)
                {
                    return false;
                }

                continue;
            }

            float inverseDirection = 1.0f / directionOnAxis;
            float near = (-halfOnAxis - originOnAxis) * inverseDirection;
            float far = (halfOnAxis - originOnAxis) * inverseDirection;

            if (near > far)
            {
                (near, far) = (far, near);
            }

            if (near > entry)
            {
                entry = near;
                entryAxis = axis;
                // A ray going down +X enters through the −X face, whose normal is −X.
                entrySign = directionOnAxis > 0.0f ? -1.0f : 1.0f;
            }

            exit = MathF.Min(exit, far);

            if (entry > exit)
            {
                return false;
            }
        }

        if (exit < 0.0f || entry > maxDistance)
        {
            return false;
        }

        if (entry < 0.0f || entryAxis < 0)
        {
            // Starting inside the box, or moving parallel to every face it is between.
            distance = 0.0f;
            point = origin;
            normal = -direction;
            return true;
        }

        distance = entry;
        point = origin + (direction * entry);

        Vector3 localNormal = Vector3.Zero;
        SetComponent(ref localNormal, entryAxis, entrySign);
        normal = Vector3.Normalize(Vector3.Transform(localNormal, shape.Rotation));
        return true;
    }

    private static bool RaycastCapsule(
        in ShapeInstance shape,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out float distance,
        out Vector3 point,
        out Vector3 normal)
    {
        distance = 0.0f;
        point = origin;
        normal = -direction;

        ClosestPointsRayToSegment(origin, direction, shape.SegmentA, shape.SegmentB, out float alongRay, out float alongSegment);
        Vector3 onRay = origin + (direction * alongRay);
        Vector3 onSegment = shape.SegmentA + ((shape.SegmentB - shape.SegmentA) * alongSegment);
        float gap = Vector3.Distance(onRay, onSegment);

        if (gap > shape.Radius)
        {
            return false;
        }

        float half = MathF.Sqrt(MathF.Max(shape.Radius * shape.Radius - gap * gap, 0.0f));
        float entry = alongRay - half;

        if (entry < 0.0f)
        {
            entry = 0.0f;
        }

        if (entry > maxDistance)
        {
            return false;
        }

        distance = entry;
        point = origin + (direction * entry);

        Vector3 towardsLine = ClosestPointOnSegment(shape.SegmentA, shape.SegmentB, point) - point;
        normal = towardsLine.LengthSquared() < Epsilon ? -direction : Vector3.Normalize(towardsLine);
        return true;
    }

    private static bool OverlapsBoxShape(in ShapeInstance a, in ShapeInstance b)
    {
        // Separating axis test over the three face axes of each box and the nine edge-edge axes.
        Vector3[] axesA = [a.AxisX, a.AxisY, a.AxisZ];
        Vector3[] axesB = [b.AxisX, b.AxisY, b.AxisZ];
        float[] halfA = [a.HalfExtents.X, a.HalfExtents.Y, a.HalfExtents.Z];
        float[] halfB = [b.HalfExtents.X, b.HalfExtents.Y, b.HalfExtents.Z];

        Vector3 between = b.Center - a.Center;

        for (int i = 0; i < 3; i++)
        {
            if (!Separates(axesA[i], between, axesA, halfA, axesB, halfB))
            {
                return false;
            }
        }

        for (int j = 0; j < 3; j++)
        {
            if (!Separates(axesB[j], between, axesA, halfA, axesB, halfB))
            {
                return false;
            }
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 axis = Vector3.Cross(axesA[i], axesB[j]);

                if (axis.LengthSquared() < 1.0e-8f)
                {
                    continue;
                }

                if (!Separates(Vector3.Normalize(axis), between, axesA, halfA, axesB, halfB))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Separates(
        Vector3 axis,
        Vector3 between,
        Vector3[] axesA,
        float[] halfA,
        Vector3[] axesB,
        float[] halfB)
    {
        float projectionA = 0.0f;
        float projectionB = 0.0f;

        for (int i = 0; i < 3; i++)
        {
            projectionA += halfA[i] * MathF.Abs(Vector3.Dot(axis, axesA[i]));
            projectionB += halfB[i] * MathF.Abs(Vector3.Dot(axis, axesB[i]));
        }

        return MathF.Abs(Vector3.Dot(between, axis)) <= projectionA + projectionB + Epsilon;
    }

    /// <summary>
    /// How far a segment passes from a box, which is how a capsule meets a box: measured by walking the segment
    /// and closing in on the nearest point, which is exact to well under a millimetre.
    /// </summary>
    private static float DistanceSegmentToBox(Vector3 start, Vector3 end, in ShapeInstance box)
    {
        // The local function below cannot capture an `in` parameter, so the box is copied once.
        ShapeInstance shape = box;
        float low = 0.0f;
        float high = 1.0f;

        for (int iteration = 0; iteration < 40; iteration++)
        {
            float left = low + ((high - low) / 3.0f);
            float right = high - ((high - low) / 3.0f);

            if (DistanceAt(left) < DistanceAt(right))
            {
                high = right;
            }
            else
            {
                low = left;
            }
        }

        return DistanceAt((low + high) * 0.5f);

        float DistanceAt(float t)
        {
            Vector3 point = Vector3.Lerp(start, end, t);
            return Vector3.Distance(point, ClosestPoint(shape, point));
        }
    }

    private static Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.LengthSquared();

        if (lengthSquared < Epsilon)
        {
            return start;
        }

        float t = Math.Clamp(Vector3.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
        return start + (segment * t);
    }

    private static float DistanceToSegment(Vector3 start, Vector3 end, Vector3 point) =>
        Vector3.Distance(ClosestPointOnSegment(start, end, point), point);

    private static float DistanceSegmentToSegment(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        Vector3 u = a1 - a0;
        Vector3 v = b1 - b0;
        Vector3 w = a0 - b0;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float denominator = (a * c) - (b * b);

        float s;
        float t;

        if (denominator < 1.0e-8f)
        {
            s = 0.0f;
            t = c > Epsilon ? e / c : 0.0f;
        }
        else
        {
            s = ((b * e) - (c * d)) / denominator;
            t = ((a * e) - (b * d)) / denominator;
        }

        s = Math.Clamp(s, 0.0f, 1.0f);
        t = Math.Clamp(t, 0.0f, 1.0f);

        Vector3 pointA = a0 + (u * s);
        Vector3 pointB = b0 + (v * t);
        return Vector3.Distance(pointA, pointB);
    }

    /// <summary>Where a ray comes closest to a segment, and where on the segment that is.</summary>
    private static void ClosestPointsRayToSegment(
        Vector3 origin,
        Vector3 direction,
        Vector3 start,
        Vector3 end,
        out float alongRay,
        out float alongSegment)
    {
        Vector3 segment = end - start;
        Vector3 toStart = origin - start;

        float a = Vector3.Dot(direction, direction);
        float b = Vector3.Dot(direction, segment);
        float c = Vector3.Dot(segment, segment);
        float d = Vector3.Dot(direction, toStart);
        float e = Vector3.Dot(segment, toStart);
        float denominator = (a * c) - (b * b);

        if (denominator < 1.0e-8f)
        {
            alongSegment = 0.0f;
            alongRay = -d / MathF.Max(a, Epsilon);
        }
        else
        {
            alongRay = ((b * e) - (c * d)) / denominator;
            alongSegment = ((a * e) - (b * d)) / denominator;
        }

        alongSegment = Math.Clamp(alongSegment, 0.0f, 1.0f);
        alongRay = MathF.Max(-Vector3.Dot(origin - (start + (segment * alongSegment)), direction), 0.0f);
    }

    private static float MaxComponent(Vector3 value) => MathF.Max(value.X, MathF.Max(value.Y, value.Z));

    private static float Component(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z
    };

    private static void SetComponent(ref Vector3 value, int axis, float component)
    {
        switch (axis)
        {
            case 0:
                value.X = component;
                break;
            case 1:
                value.Y = component;
                break;
            default:
                value.Z = component;
                break;
        }
    }

}
