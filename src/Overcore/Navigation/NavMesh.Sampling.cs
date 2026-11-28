using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMesh
{
    // ---------------------------------------------------------------- SamplePosition

    /// <summary>Finds the closest point on the NavMesh within <paramref name="maxDistance"/> of <paramref name="sourcePosition"/>.</summary>
    public static bool SamplePosition(
        Vector3 sourcePosition,
        out NavMeshHit hit,
        float maxDistance,
        int areaMask = AllAreas)
    {
        NavMeshQueryFilter filter = new() { AreaMask = areaMask };
        return SamplePosition(sourcePosition, out hit, maxDistance, filter);
    }

    /// <summary>Finds the closest point on the NavMesh within <paramref name="maxDistance"/> matching <paramref name="filter"/>.</summary>
    public static bool SamplePosition(
        Vector3 sourcePosition,
        out NavMeshHit hit,
        float maxDistance,
        NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        hit = default;
        if (maxDistance < 0f || _entries.Count == 0)
        {
            return false;
        }

        float bestDistSq = maxDistance * maxDistance;
        bool found = false;

        foreach (RegisteredEntry entry in _entries)
        {
            if (TrySampleData(entry.Data, sourcePosition, maxDistance, filter, ref bestDistSq, ref hit))
            {
                found = true;
            }
        }

        return found;
    }

    internal static bool TryFindContainingOrNearestPolygon(
        NavMeshData data,
        Vector3 position,
        float maxDistance,
        NavMeshQueryFilter filter,
        out int polygonIndex,
        out Vector3 clampedPoint)
    {
        polygonIndex = -1;
        clampedPoint = position;

        float bestDistSq = maxDistance * maxDistance;
        NavMeshHit hit = default;

        if (!TrySampleData(data, position, maxDistance, filter, ref bestDistSq, ref hit))
        {
            return false;
        }

        polygonIndex = hit.PolygonIndex;
        clampedPoint = hit.Position;
        return true;
    }

    private static bool TrySampleData(
        NavMeshData data,
        Vector3 sourcePosition,
        float maxDistance,
        NavMeshQueryFilter filter,
        ref float bestDistSq,
        ref NavMeshHit bestHit)
    {
        if (data.IsEmpty)
        {
            return false;
        }

        bool found = false;
        Vector3 queryMin = sourcePosition - new Vector3(maxDistance);
        Vector3 queryMax = sourcePosition + new Vector3(maxDistance);

        if (!BoxesOverlap(data.Bounds, queryMin, queryMax))
        {
            return false;
        }

        Vector3[] verts = data.Vertices;
        for (int p = 0; p < data.Polygons.Count; p++)
        {
            NavMeshPolygon poly = data.Polygons[p];
            int areaBit = 1 << poly.Area;
            if ((filter.AreaMask & areaBit) == 0)
            {
                continue;
            }

            if (!BoxesOverlap(poly.Bounds, queryMin, queryMax))
            {
                continue;
            }

            Vector3 closest = ClosestPointOnConvexPolygon(poly, verts, sourcePosition);
            float distSq = Vector3.DistanceSquared(sourcePosition, closest);

            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                bestHit = new NavMeshHit
                {
                    Position = closest,
                    Normal = poly.Normal,
                    Distance = MathF.Sqrt(distSq),
                    Mask = areaBit,
                    Hit = true,
                    PolygonIndex = p
                };
                found = true;
            }
        }

        return found;
    }
}
