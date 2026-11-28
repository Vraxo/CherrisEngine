using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMesh
{
    // ---------------------------------------------------------------- Raycast & FindClosestEdge

    /// <summary>
    /// Traces a straight segment from <paramref name="sourcePosition"/> to <paramref name="targetPosition"/> across
    /// connected NavMesh polygons. Returns <c>true</c> if obstructed by an impassable boundary edge, populating
    /// <paramref name="hit"/>; returns <c>false</c> when the line of sight across the NavMesh is clear.
    /// </summary>
    public static bool Raycast(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        out NavMeshHit hit,
        int areaMask = AllAreas)
    {
        NavMeshQueryFilter filter = areaMask == AllAreas ? _defaultFilter : new NavMeshQueryFilter { AreaMask = areaMask };
        return Raycast(sourcePosition, targetPosition, out hit, filter);
    }

    /// <summary>Traces a straight line across the NavMesh using <paramref name="filter"/>.</summary>
    public static bool Raycast(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        out NavMeshHit hit,
        NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        hit = default;
        if (_entries.Count == 0)
        {
            hit = new NavMeshHit
            {
                Position = sourcePosition,
                Normal = Vector3.Zero,
                Distance = 0f,
                Hit = true,
                PolygonIndex = -1
            };
            return true;
        }

        for (int e = _entries.Count - 1; e >= 0; e--)
        {
            NavMeshData data = _entries[e].Data;
            if (data.IsEmpty)
            {
                continue;
            }

            float searchExtent = MathF.Max(1.0f, data.BuildSettings.AgentHeight * 1.5f);
            if (!TryFindContainingOrNearestPolygon(data, sourcePosition, searchExtent, filter, out int curPoly, out Vector3 startOnMesh))
            {
                continue;
            }

            // If sourcePosition itself was horizontally outside the polygon beyond tolerance, immediately report hit at boundary.
            if (Vector2.DistanceSquared(new Vector2(sourcePosition.X, sourcePosition.Z), new Vector2(startOnMesh.X, startOnMesh.Z)) > 0.05f)
            {
                Vector3 toStart = Vector3.Normalize(new Vector3(sourcePosition.X - startOnMesh.X, 0f, sourcePosition.Z - startOnMesh.Z));
                hit = new NavMeshHit
                {
                    Position = startOnMesh,
                    Normal = -toStart,
                    Distance = 0f,
                    Mask = 1 << data.Polygons[curPoly].Area,
                    Hit = true,
                    PolygonIndex = curPoly
                };
                return true;
            }

            Vector2 rayA = new(startOnMesh.X, startOnMesh.Z);
            Vector2 rayB = new(targetPosition.X, targetPosition.Z);
            Vector2 rayDir = rayB - rayA;
            float totalDistXZ = rayDir.Length();

            if (totalDistXZ <= 1e-5f)
            {
                hit = new NavMeshHit
                {
                    Position = targetPosition,
                    Normal = Vector3.Zero,
                    Distance = 0f,
                    Mask = 1 << data.Polygons[curPoly].Area,
                    Hit = false,
                    PolygonIndex = curPoly
                };
                return false;
            }

            int prevPoly = -1;
            float tCurrent = 0f;
            int safety = data.Polygons.Count + 8;

            while (safety-- > 0)
            {
                NavMeshPolygon poly = data.Polygons[curPoly];
                int n = poly.VertexIndices.Length;

                int exitEdge = -1;
                float bestExitT = float.PositiveInfinity;
                Vector3 exitHitPos = targetPosition;
                Vector3 exitEdgeNormal = Vector3.Zero;

                for (int edge = 0; edge < n; edge++)
                {
                    Vector3 v0 = data.Vertices[poly.VertexIndices[edge]];
                    Vector3 v1 = data.Vertices[poly.VertexIndices[(edge + 1) % n]];

                    Vector2 e0 = new(v0.X, v0.Z);
                    Vector2 e1 = new(v1.X, v1.Z);
                    Vector2 edgeVec = e1 - e0;

                    // Outward normal in XZ: point rayB must be on the exterior side of the edge, or rayDir pointing outward.
                    Vector2 toCenter = new Vector2(poly.Center.X, poly.Center.Z) - e0;
                    Vector2 perp = new(-edgeVec.Y, edgeVec.X);
                    if (Vector2.Dot(perp, toCenter) > 0f)
                    {
                        perp = -perp; // Make perp point outward away from polygon center.
                    }

                    if (Vector2.Dot(rayDir, perp) <= 1e-6f)
                    {
                        // Ray is parallel or entering through this edge, not exiting.
                        continue;
                    }

                    if (TryIntersectSegmentsXZ(startOnMesh, targetPosition, v0, v1, out float tRay, out float tEdge))
                    {
                        bool differentNeighbor = prevPoly < 0 || poly.Neighbors[edge] != prevPoly;
                        if (tRay >= tCurrent - 1e-4f && tRay < bestExitT && differentNeighbor)
                        {
                            bestExitT = tRay;
                            exitEdge = edge;
                            exitHitPos = new Vector3(
                                float.Lerp(startOnMesh.X, targetPosition.X, tRay),
                                float.Lerp(v0.Y, v1.Y, tEdge),
                                float.Lerp(startOnMesh.Z, targetPosition.Z, tRay));

                            float pLen = perp.Length();
                            Vector2 inward = pLen > 1e-6f ? -perp / pLen : Vector2.Zero;
                            exitEdgeNormal = new Vector3(inward.X, 0f, inward.Y);
                        }
                    }
                }

                if (exitEdge == -1 || bestExitT > 1f)
                {
                    // Reached target inside curPoly without crossing an impassable wall!
                    Vector3 finalPos = ClosestPointOnConvexPolygon(poly, data.Vertices, targetPosition);
                    hit = new NavMeshHit
                    {
                        Position = finalPos,
                        Normal = Vector3.Zero,
                        Distance = Vector3.Distance(startOnMesh, finalPos),
                        Mask = 1 << poly.Area,
                        Hit = false,
                        PolygonIndex = curPoly
                    };
                    return false;
                }

                int nextPoly = poly.Neighbors[exitEdge];
                bool blocked = nextPoly < 0
                    || nextPoly >= data.Polygons.Count
                    || (filter.AreaMask & (1 << data.Polygons[nextPoly].Area)) == 0
                    || !float.IsFinite(filter.GetAreaCost(data.Polygons[nextPoly].Area));

                if (blocked)
                {
                    hit = new NavMeshHit
                    {
                        Position = exitHitPos,
                        Normal = exitEdgeNormal,
                        Distance = Vector3.Distance(startOnMesh, exitHitPos),
                        Mask = 1 << poly.Area,
                        Hit = true,
                        PolygonIndex = curPoly
                    };
                    return true;
                }

                if (bestExitT >= 1f - 1e-5f)
                {
                    NavMeshPolygon destPoly = data.Polygons[nextPoly];
                    Vector3 finalPos = ClosestPointOnConvexPolygon(destPoly, data.Vertices, targetPosition);
                    hit = new NavMeshHit
                    {
                        Position = finalPos,
                        Normal = Vector3.Zero,
                        Distance = Vector3.Distance(startOnMesh, finalPos),
                        Mask = 1 << destPoly.Area,
                        Hit = false,
                        PolygonIndex = nextPoly
                    };
                    return false;
                }

                prevPoly = curPoly;
                curPoly = nextPoly;
                tCurrent = bestExitT;
            }
        }

        // Source was not on any NavMesh.
        hit = new NavMeshHit
        {
            Position = sourcePosition,
            Normal = Vector3.Zero,
            Distance = 0f,
            Hit = true,
            PolygonIndex = -1
        };
        return true;
    }
}
