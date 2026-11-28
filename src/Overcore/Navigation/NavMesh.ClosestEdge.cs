using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMesh
{
    /// <summary>Finds the closest boundary (non-shared) edge of the NavMesh to <paramref name="sourcePosition"/>.</summary>
    public static bool FindClosestEdge(
        Vector3 sourcePosition,
        out NavMeshHit hit,
        int areaMask = AllAreas)
    {
        NavMeshQueryFilter filter = areaMask == AllAreas ? _defaultFilter : new NavMeshQueryFilter { AreaMask = areaMask };
        return FindClosestEdge(sourcePosition, out hit, filter);
    }

    /// <summary>Finds the closest boundary edge matching <paramref name="filter"/>.</summary>
    public static bool FindClosestEdge(
        Vector3 sourcePosition,
        out NavMeshHit hit,
        NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        hit = default;
        if (_entries.Count == 0)
        {
            return false;
        }

        float bestDistSq = float.PositiveInfinity;
        bool found = false;

        foreach (RegisteredEntry entry in _entries)
        {
            NavMeshData data = entry.Data;
            if (data.IsEmpty)
            {
                continue;
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

                int n = poly.VertexIndices.Length;
                for (int edge = 0; edge < n; edge++)
                {
                    int neighbor = poly.Neighbors[edge];
                    bool isBoundary = neighbor < 0
                        || (filter.AreaMask & (1 << data.Polygons[neighbor].Area)) == 0;

                    if (!isBoundary)
                    {
                        continue;
                    }

                    Vector3 a = verts[poly.VertexIndices[edge]];
                    Vector3 b = verts[poly.VertexIndices[(edge + 1) % n]];
                    Vector3 closest = ClosestPointOnSegment(a, b, sourcePosition);
                    float distSq = Vector3.DistanceSquared(sourcePosition, closest);

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;

                        Vector2 edgeVec = new(b.X - a.X, b.Z - a.Z);
                        Vector2 inward = new(-edgeVec.Y, edgeVec.X);
                        if (Vector2.Dot(inward, new Vector2(poly.Center.X - a.X, poly.Center.Z - a.Z)) < 0f)
                        {
                            inward = -inward;
                        }

                        float len = inward.Length();
                        Vector3 normal = len > 1e-6f ? new Vector3(inward.X / len, 0f, inward.Y / len) : Vector3.UnitY;

                        hit = new NavMeshHit
                        {
                            Position = closest,
                            Normal = normal,
                            Distance = MathF.Sqrt(distSq),
                            Mask = areaBit,
                            Hit = true,
                            PolygonIndex = p
                        };
                        found = true;
                    }
                }
            }
        }

        return found;
    }
}
