using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMesh
{
    // ---------------------------------------------------------------- Geometry Helpers

    private static Vector3 GetEdgeMidpoint(Vector3[] verts, NavMeshPolygon poly, int edge)
    {
        int i0 = poly.VertexIndices[edge];
        int i1 = poly.VertexIndices[(edge + 1) % poly.VertexIndices.Length];
        return (verts[i0] + verts[i1]) * 0.5f;
    }

    internal static Vector3 ClosestPointOnConvexPolygon(NavMeshPolygon poly, Vector3[] verts, Vector3 point)
    {
        int n = poly.VertexIndices.Length;
        if (n == 0)
        {
            return poly.Center;
        }

        if (n < 3)
        {
            return verts[poly.VertexIndices[0]];
        }

        // Fan-triangulate from vertex 0 (exact because every NavMeshPolygon is convex) and pick closest point.
        Vector3 v0 = verts[poly.VertexIndices[0]];
        Vector3 best = v0;
        float bestDistSq = float.PositiveInfinity;

        for (int i = 1; i < n - 1; i++)
        {
            Vector3 v1 = verts[poly.VertexIndices[i]];
            Vector3 v2 = verts[poly.VertexIndices[i + 1]];

            Vector3 candidate = ClosestPointOnTriangle(v0, v1, v2, point);
            float dSq = Vector3.DistanceSquared(point, candidate);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Real-Time Collision Detection (Christer Ericson) closest point on 3D triangle.</summary>
    private static Vector3 ClosestPointOnTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + (v * ab);
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + (w * ac);
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + (w * (c - b));
        }

        float denom = 1f / (va + vb + vc);
        float vCoord = vb * denom;
        float wCoord = vc * denom;
        return a + (ab * vCoord) + (ac * wCoord);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq <= 1e-8f)
        {
            return a;
        }

        float t = Math.Clamp(Vector3.Dot(p - a, ab) / lenSq, 0f, 1f);
        return a + (ab * t);
    }

    private static bool TryIntersectSegmentsXZ(
        Vector3 a0,
        Vector3 a1,
        Vector3 b0,
        Vector3 b1,
        out float tA,
        out float tB)
    {
        tA = 0f;
        tB = 0f;

        float rx = a1.X - a0.X;
        float rz = a1.Z - a0.Z;
        float sx = b1.X - b0.X;
        float sz = b1.Z - b0.Z;

        float denom = (rx * sz) - (rz * sx);
        if (MathF.Abs(denom) < 1e-7f)
        {
            return false;
        }

        float qpx = b0.X - a0.X;
        float qpz = b0.Z - a0.Z;

        tA = ((qpx * sz) - (qpz * sx)) / denom;
        tB = ((qpx * rz) - (qpz * rx)) / denom;

        return tA is >= -1e-4f and <= 1.0001f
            && tB is >= -1e-4f and <= 1.0001f;
    }

    private static bool BoxesOverlap(BoundingBox box, Vector3 min, Vector3 max) =>
        box.Max.X >= min.X && box.Min.X <= max.X
        && box.Max.Y >= min.Y && box.Min.Y <= max.Y
        && box.Max.Z >= min.Z && box.Min.Z <= max.Z;
}
