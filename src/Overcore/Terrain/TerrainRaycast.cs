using System.Numerics;

namespace Overcore.Terrain;

/// <summary>
/// A ray against a heightfield, in the terrain's own metres: the picking arithmetic behind both the
/// physics raycast and the editor's sculpting brush.
/// </summary>
/// <remarks>
/// The walk is a 2D DDA over the height grid along the ray's ground track, testing the two triangles of
/// each crossed cell exactly (Möller–Trumbore), so a hit is the drawn surface rather than a sampling of
/// it. Everything here is local: the caller owns the terrain's place in the world and converts the ray
/// in and the hit out, which is what lets an editor in edit mode — no physics world, no colliders —
/// pick the ground with the same arithmetic a <c>Physics.Raycast</c> uses in play mode.
/// </remarks>
public static class TerrainRaycast
{
    private const float Epsilon = 1.0e-6f;

    /// <summary>
    /// True when a ray in terrain-local metres meets the ground, with where it met it.
    /// </summary>
    /// <param name="data">The ground to test.</param>
    /// <param name="localOrigin">Where the ray starts, in the terrain's own metres (origin at its corner).</param>
    /// <param name="localDirection">Which way it goes, in the same metres; it need not be unit length, and its parameter is what <paramref name="parameter"/> returns.</param>
    /// <param name="localPoint">Receives where the ray met the surface, in terrain-local metres.</param>
    /// <param name="parameter">Receives the ray parameter of the meeting, in units of <paramref name="localDirection"/>.</param>
    public static bool TryHitLocal(
        TerrainData data,
        Vector3 localOrigin,
        Vector3 localDirection,
        out Vector3 localPoint,
        out float parameter)
    {
        ArgumentNullException.ThrowIfNull(data);
        localPoint = localOrigin;
        parameter = 0f;

        int resolution = data.HeightmapResolution;
        float cellX = data.Size.X / (resolution - 1);
        float cellZ = data.Size.Z / (resolution - 1);

        // The cells the ray's ground track crosses, from where it enters the footprint. A ray that never
        // crosses the footprint, or points away from it, cannot meet the ground.
        float enter = 0f;
        float exit = float.PositiveInfinity;
        if (!Slab(localOrigin.X, localDirection.X, 0f, data.Size.X, ref enter, ref exit)
            || !Slab(localOrigin.Z, localDirection.Z, 0f, data.Size.Z, ref enter, ref exit))
        {
            return false;
        }

        Vector3 start = localOrigin + (localDirection * MathF.Max(enter, 0f));
        int stepX = localDirection.X > 0f ? 1 : -1;
        int stepZ = localDirection.Z > 0f ? 1 : -1;
        float nextX = Boundary(start.X, cellX, stepX);
        float nextZ = Boundary(start.Z, cellZ, stepZ);
        float tMaxX = MathF.Abs(localDirection.X) < Epsilon ? float.PositiveInfinity
            : MathF.Abs((nextX - start.X) / localDirection.X);
        float tMaxZ = MathF.Abs(localDirection.Z) < Epsilon ? float.PositiveInfinity
            : MathF.Abs((nextZ - start.Z) / localDirection.Z);
        float tDeltaX = MathF.Abs(localDirection.X) < Epsilon ? float.PositiveInfinity
            : MathF.Abs(cellX / localDirection.X);
        float tDeltaZ = MathF.Abs(localDirection.Z) < Epsilon ? float.PositiveInfinity
            : MathF.Abs(cellZ / localDirection.Z);

        float bestT = float.PositiveInfinity;
        int cellCount = (resolution - 1) * 2 + 2;
        int cx = Math.Clamp((int)MathF.Floor(start.X / cellX), 0, resolution - 2);
        int cz = Math.Clamp((int)MathF.Floor(start.Z / cellZ), 0, resolution - 2);
        for (int visited = 0; visited < cellCount; visited++)
        {
            if (cx >= 0 && cx < resolution - 1 && cz >= 0 && cz < resolution - 1)
            {
                TestCell(data, cx, cz, localOrigin, localDirection, ref bestT);
            }

            // Past the cell the ray leaves the footprint through, there is no more ground to meet.
            if (tMaxX < tMaxZ)
            {
                if (tMaxX > exit - MathF.Max(enter, 0f))
                {
                    break;
                }

                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                if (tMaxZ > exit - MathF.Max(enter, 0f))
                {
                    break;
                }

                cz += stepZ;
                tMaxZ += tDeltaZ;
            }
        }

        if (float.IsPositiveInfinity(bestT))
        {
            return false;
        }

        parameter = bestT;
        localPoint = localOrigin + (localDirection * bestT);
        return true;
    }

    /// <summary>Tests both triangles of one heightfield cell against the local ray, keeping the nearest hit.</summary>
    private static void TestCell(
        TerrainData data, int cellX, int cellZ, Vector3 origin, Vector3 direction, ref float bestT)
    {
        float spacingX = data.Size.X / (data.HeightmapResolution - 1);
        float spacingZ = data.Size.Z / (data.HeightmapResolution - 1);

        Vector3 Corner(int x, int z) => new(x * spacingX, data.GetHeight(x, z), z * spacingZ);

        Vector3 a = Corner(cellX, cellZ);
        Vector3 b = Corner(cellX + 1, cellZ);
        Vector3 c = Corner(cellX + 1, cellZ + 1);
        Vector3 d = Corner(cellX, cellZ + 1);

        // The mesher's triangles (far corner first), so the ray meets the surface that is drawn.
        if (RaycastTriangle(d, c, b, origin, direction, out float first) && first < bestT)
        {
            bestT = first;
        }

        if (RaycastTriangle(d, a, c, origin, direction, out float second) && second < bestT)
        {
            bestT = second;
        }
    }

    /// <summary>Möller–Trumbore: true when the ray meets the triangle, with the ray parameter of the meeting.</summary>
    private static bool RaycastTriangle(
        Vector3 a, Vector3 b, Vector3 c, Vector3 origin, Vector3 direction, out float t)
    {
        t = 0f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 p = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < Epsilon)
        {
            return false;
        }

        float inverse = 1f / determinant;
        Vector3 fromA = origin - a;
        float u = Vector3.Dot(fromA, p) * inverse;
        if (u < -Epsilon || u > 1f + Epsilon)
        {
            return false;
        }

        Vector3 q = Vector3.Cross(fromA, edge1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < -Epsilon || u + v > 1f + Epsilon)
        {
            return false;
        }

        t = Vector3.Dot(edge2, q) * inverse;
        return t >= 0f;
    }

    /// <summary>Clips a ray against one axis of a slab, narrowing the entry/exit parameters.</summary>
    private static bool Slab(float origin, float direction, float low, float high, ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) < Epsilon)
        {
            return origin >= low && origin <= high;
        }

        float near = (low - origin) / direction;
        float far = (high - origin) / direction;
        if (near > far)
        {
            (near, far) = (far, near);
        }

        enter = MathF.Max(enter, near);
        exit = MathF.Min(exit, far);
        return enter <= exit;
    }

    /// <summary>The next grid line past a coordinate, in the direction of travel.</summary>
    private static float Boundary(float value, float cell, int step) =>
        step > 0 ? (MathF.Floor(value / cell) + 1f) * cell : MathF.Floor(value / cell) * cell;
}
