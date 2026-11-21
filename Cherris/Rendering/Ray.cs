using Cherris.Components;
using System.Numerics;

namespace Cherris;

// A simple ray for picking.
public struct Ray
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    // Slab method for ray-AABB intersection.
    public readonly bool Intersects(BoundingBox box, out float distance)
    {
        Vector3 invDir = Vector3.One / Direction;
        Vector3 t1 = (box.Min - Origin) * invDir;
        Vector3 t2 = (box.Max - Origin) * invDir;

        Vector3 tMinVec = Vector3.Min(t1, t2);
        Vector3 tMaxVec = Vector3.Max(t1, t2);

        float tmin = Math.Max(tMinVec.X, Math.Max(tMinVec.Y, tMinVec.Z));
        float tmax = Math.Min(tMaxVec.X, Math.Min(tMaxVec.Y, tMaxVec.Z));

        distance = tmin;

        // if tmax < 0, ray is intersecting AABB, but the whole AABB is behind us
        // if tmin > tmax, ray doesn't intersect AABB
        return tmax >= Math.Max(tmin, 0.0f);
    }
}