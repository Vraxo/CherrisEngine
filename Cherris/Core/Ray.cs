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
    public bool Intersects(BoundingBox box, out float distance)
    {
        distance = 0.0f;
        float tmin = 0.0f;
        float tmax = float.MaxValue;

        if (Math.Abs(Direction.X) < 1e-6)
        {
            if (Origin.X < box.Min.X || Origin.X > box.Max.X) return false;
        }
        else
        {
            float ood = 1.0f / Direction.X;
            float t1 = (box.Min.X - Origin.X) * ood;
            float t2 = (box.Max.X - Origin.X) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(Direction.Y) < 1e-6)
        {
            if (Origin.Y < box.Min.Y || Origin.Y > box.Max.Y) return false;
        }
        else
        {
            float ood = 1.0f / Direction.Y;
            float t1 = (box.Min.Y - Origin.Y) * ood;
            float t2 = (box.Max.Y - Origin.Y) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(Direction.Z) < 1e-6)
        {
            if (Origin.Z < box.Min.Z || Origin.Z > box.Max.Z) return false;
        }
        else
        {
            float ood = 1.0f / Direction.Z;
            float t1 = (box.Min.Z - Origin.Z) * ood;
            float t2 = (box.Max.Z - Origin.Z) * ood;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        distance = tmin;
        return true;
    }
}
