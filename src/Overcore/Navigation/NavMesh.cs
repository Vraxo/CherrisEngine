using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

/// <summary>Handle returned by <see cref="NavMesh.AddNavMeshData"/> so a registered slice can be removed individually.</summary>
public readonly record struct NavMeshDataInstance(int Id)
{
    public bool Valid => Id > 0;
    public void Remove() => NavMesh.RemoveNavMeshData(this);
}

/// <summary>
/// Runtime navigation mesh registry and query engine — Unity's <c>NavMesh</c>:
/// A* polygon search with area masks/costs, Simple Stupid Funnel string-pulling, nearest-point projection
/// (<see cref="SamplePosition"/>), NavMesh boundary raycasting (<see cref="Raycast"/>), closest boundary edge
/// (<see cref="FindClosestEdge"/>), and triangulation export (<see cref="CalculateTriangulation"/>).
/// </summary>
public static class NavMesh
{
    public const int Walkable = 0;
    public const int NotWalkable = 1;
    public const int Jump = 2;
    public const int AllAreas = ~0;

    private sealed record RegisteredEntry(int Id, NavMeshData Data);

    private static readonly List<RegisteredEntry> _entries = [];
    private static int _nextInstanceId = 1;
    private static readonly NavMeshQueryFilter _defaultFilter = new();

    /// <summary>Version counter incremented whenever NavMesh data is added, updated, or cleared so agents know to replan.</summary>
    public static int Version { get; private set; }

    /// <summary>Currently active primary <see cref="NavMeshData"/>, or null when none has been baked/added.</summary>
    public static NavMeshData? ActiveData => _entries.Count > 0 ? _entries[^1].Data : null;

    /// <summary>Registers baked <see cref="NavMeshData"/> and returns a handle that can remove it later.</summary>
    public static NavMeshDataInstance AddNavMeshData(NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int id = _nextInstanceId++;
        _entries.Add(new RegisteredEntry(id, data));
        Version++;
        return new NavMeshDataInstance(id);
    }

    /// <summary>Replaces an existing <see cref="NavMeshDataInstance"/> in place, or registers a new one if invalid.</summary>
    internal static NavMeshDataInstance ReplaceNavMeshData(NavMeshDataInstance existing, NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id == existing.Id)
            {
                _entries[i] = new RegisteredEntry(existing.Id, data);
                Version++;
                return existing;
            }
        }

        return AddNavMeshData(data);
    }

    /// <summary>Removes a previously registered <see cref="NavMeshDataInstance"/>.</summary>
    public static void RemoveNavMeshData(NavMeshDataInstance handle)
    {
        if (!handle.Valid)
        {
            return;
        }

        int removed = _entries.RemoveAll(e => e.Id == handle.Id);
        if (removed > 0)
        {
            Version++;
        }
    }

    /// <summary>Removes all registered <see cref="NavMeshData"/> instances.</summary>
    public static void RemoveAllNavMeshData()
    {
        if (_entries.Count > 0)
        {
            _entries.Clear();
            Version++;
        }
    }

    /// <summary>Exports the combined triangulation of all registered <see cref="NavMeshData"/> slices.</summary>
    public static NavMeshTriangulation CalculateTriangulation()
    {
        if (_entries.Count == 0)
        {
            return new NavMeshTriangulation([], [], []);
        }

        if (_entries.Count == 1)
        {
            return _entries[0].Data.ToTriangulation();
        }

        List<Vector3> vertices = [];
        List<int> indices = [];
        List<int> areas = [];

        foreach (RegisteredEntry entry in _entries)
        {
            NavMeshTriangulation part = entry.Data.ToTriangulation();
            int baseVert = vertices.Count;
            vertices.AddRange(part.Vertices);
            foreach (int idx in part.Indices)
            {
                indices.Add(baseVert + idx);
            }

            areas.AddRange(part.Areas);
        }

        return new NavMeshTriangulation([.. vertices], [.. indices], [.. areas]);
    }

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

    // ---------------------------------------------------------------- CalculatePath (A* + Funnel)

    /// <summary>Computes a smooth corner path between <paramref name="sourcePosition"/> and <paramref name="targetPosition"/>.</summary>
    public static bool CalculatePath(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        int areaMask,
        NavMeshPath path)
    {
        NavMeshQueryFilter filter = areaMask == AllAreas ? _defaultFilter : new NavMeshQueryFilter { AreaMask = areaMask };
        return CalculatePath(sourcePosition, targetPosition, filter, path);
    }

    /// <summary>Computes a smooth corner path using <paramref name="filter"/>'s area mask and traversal costs.</summary>
    public static bool CalculatePath(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        NavMeshQueryFilter filter,
        NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(path);

        path.ClearCorners();

        if (_entries.Count == 0)
        {
            return false;
        }

        // Choose the NavMeshData whose sampled start & target are valid (searching latest registered first).
        for (int e = _entries.Count - 1; e >= 0; e--)
        {
            NavMeshData data = _entries[e].Data;
            if (data.IsEmpty)
            {
                continue;
            }

            float searchExtent = MathF.Max(2.0f, data.BuildSettings.AgentHeight * 2.5f);
            if (!TryFindContainingOrNearestPolygon(data, sourcePosition, searchExtent, filter, out int startPoly, out Vector3 startClamped))
            {
                continue;
            }

            if (!TryFindContainingOrNearestPolygon(data, targetPosition, searchExtent, filter, out int endPoly, out Vector3 endClamped))
            {
                // Target is completely off-mesh beyond searchExtent; try clamping to closest polygon within larger fallback
                // to produce a PathPartial toward the closest reachable spot if possible.
                if (!TryFindContainingOrNearestPolygon(data, targetPosition, searchExtent * 8f, filter, out endPoly, out endClamped))
                {
                    continue;
                }

                BuildCorridorAndFunnel(data, startPoly, startClamped, endPoly, endClamped, filter, forcePartial: true, path);
                return path.Status != NavMeshPathStatus.PathInvalid;
            }

            BuildCorridorAndFunnel(data, startPoly, startClamped, endPoly, endClamped, filter, forcePartial: false, path);
            return path.Status != NavMeshPathStatus.PathInvalid;
        }

        return false;
    }

    private static void BuildCorridorAndFunnel(
        NavMeshData data,
        int startPoly,
        Vector3 startPos,
        int endPoly,
        Vector3 endPos,
        NavMeshQueryFilter filter,
        bool forcePartial,
        NavMeshPath outPath)
    {
        if (startPoly == endPoly)
        {
            if (Vector3.DistanceSquared(startPos, endPos) < 1e-6f)
            {
                outPath.SetCorners([startPos], forcePartial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
            }
            else
            {
                outPath.SetCorners([startPos, endPos], forcePartial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
            }

            return;
        }

        int polyCount = data.Polygons.Count;
        float[] gScore = new float[polyCount];
        int[] cameFrom = new int[polyCount];
        bool[] closed = new bool[polyCount];

        Array.Fill(gScore, float.PositiveInfinity);
        Array.Fill(cameFrom, -1);

        PriorityQueue<int, float> open = new();
        gScore[startPoly] = 0f;
        open.Enqueue(startPoly, Vector3.Distance(startPos, endPos));

        int closestPoly = startPoly;
        float closestHeuristic = Vector3.Distance(data.Polygons[startPoly].Center, endPos);
        bool reachedGoal = false;

        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (closed[current])
            {
                continue;
            }

            closed[current] = true;

            if (current == endPoly)
            {
                reachedGoal = true;
                break;
            }

            NavMeshPolygon curPoly = data.Polygons[current];
            float hCurrent = Vector3.Distance(curPoly.Center, endPos);
            if (hCurrent < closestHeuristic)
            {
                closestHeuristic = hCurrent;
                closestPoly = current;
            }

            Vector3 curRefPoint = current == startPoly ? startPos : curPoly.Center;

            for (int edge = 0; edge < curPoly.Neighbors.Length; edge++)
            {
                int neighborIdx = curPoly.Neighbors[edge];
                if (neighborIdx < 0 || neighborIdx >= polyCount || closed[neighborIdx])
                {
                    continue;
                }

                NavMeshPolygon nextPoly = data.Polygons[neighborIdx];
                int areaBit = 1 << nextPoly.Area;
                if ((filter.AreaMask & areaBit) == 0)
                {
                    continue;
                }

                float areaCost = filter.GetAreaCost(nextPoly.Area);
                if (!float.IsFinite(areaCost))
                {
                    continue;
                }

                Vector3 edgeMid = GetEdgeMidpoint(data.Vertices, curPoly, edge);
                Vector3 nextRef = neighborIdx == endPoly ? endPos : nextPoly.Center;
                float stepDist = Vector3.Distance(curRefPoint, edgeMid) + Vector3.Distance(edgeMid, nextRef);
                float tentativeG = gScore[current] + (stepDist * areaCost);

                if (tentativeG < gScore[neighborIdx])
                {
                    cameFrom[neighborIdx] = current;
                    gScore[neighborIdx] = tentativeG;
                    float f = tentativeG + Vector3.Distance(nextRef, endPos);
                    open.Enqueue(neighborIdx, f);
                }
            }
        }

        int targetPolyForCorridor = reachedGoal ? endPoly : closestPoly;
        Vector3 finalGoalPos = reachedGoal
            ? endPos
            : ClosestPointOnConvexPolygon(data.Polygons[closestPoly], data.Vertices, endPos);

        List<int> corridor = [];
        int trace = targetPolyForCorridor;
        while (trace != -1)
        {
            corridor.Add(trace);
            trace = cameFrom[trace];
        }

        corridor.Reverse();

        NavMeshPathStatus status = reachedGoal && !forcePartial
            ? NavMeshPathStatus.PathComplete
            : NavMeshPathStatus.PathPartial;

        List<Vector3> corners = StringPullFunnel(data, corridor, startPos, finalGoalPos);
        outPath.SetCorners(corners, status);
    }

    /// <summary>
    /// Simple Stupid Funnel Algorithm (Mikko Mononen / Recast Detour) in the XZ plane with height interpolated
    /// across portal edges so paths hug ramps and stairs smoothly while taking shortest taut corners around obstacles.
    /// </summary>
    private static List<Vector3> StringPullFunnel(
        NavMeshData data,
        List<int> corridor,
        Vector3 startPos,
        Vector3 endPos)
    {
        if (corridor.Count <= 1)
        {
            return Vector3.DistanceSquared(startPos, endPos) < 1e-6f
                ? [startPos]
                : [startPos, endPos];
        }

        // Collect ordered portals (left, right) when looking from corridor[i] into corridor[i + 1].
        List<(Vector3 Left, Vector3 Right)> portals = new(corridor.Count + 1)
        {
            (startPos, startPos)
        };

        for (int i = 0; i < corridor.Count - 1; i++)
        {
            NavMeshPolygon cur = data.Polygons[corridor[i]];
            int nextIdx = corridor[i + 1];

            if (TryGetPortalEdge(data.Vertices, cur, nextIdx, out Vector3 left, out Vector3 right))
            {
                portals.Add((left, right));
            }
        }

        portals.Add((endPos, endPos));

        List<Vector3> pts = [startPos];

        Vector3 portalApex = startPos;
        Vector3 portalLeft = startPos;
        Vector3 portalRight = startPos;
        int apexIndex = 0;
        int leftIndex = 0;
        int rightIndex = 0;

        for (int i = 1; i < portals.Count; i++)
        {
            Vector3 left = portals[i].Left;
            Vector3 right = portals[i].Right;

            // Update right vertex.
            if (TriArea2XZ(portalApex, portalRight, right) <= 0f)
            {
                if (SameXZ(portalApex, portalRight) || TriArea2XZ(portalApex, portalLeft, right) > 0f)
                {
                    // Tighten the funnel.
                    portalRight = right;
                    rightIndex = i;
                }
                else
                {
                    // Right crossed over left: insert left as a corner and restart scan from leftIndex.
                    AppendCornerIfDistinct(pts, portalLeft);
                    portalApex = portalLeft;
                    apexIndex = leftIndex;

                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            // Update left vertex.
            if (TriArea2XZ(portalApex, portalLeft, left) >= 0f)
            {
                if (SameXZ(portalApex, portalLeft) || TriArea2XZ(portalApex, portalRight, left) < 0f)
                {
                    // Tighten the funnel.
                    portalLeft = left;
                    leftIndex = i;
                }
                else
                {
                    // Left crossed over right: insert right as a corner and restart scan from rightIndex.
                    AppendCornerIfDistinct(pts, portalRight);
                    portalApex = portalRight;
                    apexIndex = rightIndex;

                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        AppendCornerIfDistinct(pts, endPos);

        // Project intermediate segments across portal heights when elevation changes (e.g. stairs/ramps between corners).
        ProjectHeightChangesAlongPortals(data, corridor, pts);

        return pts;
    }

    private static void ProjectHeightChangesAlongPortals(
        NavMeshData data,
        List<int> corridor,
        List<Vector3> corners)
    {
        if (corners.Count < 2 || corridor.Count < 2)
        {
            return;
        }

        // Insert elevation inflection points where a straight XZ segment crosses a portal with a noticeably different Y
        // than linear interpolation between the two corners.
        List<Vector3> refined = new(corners.Count + 4) { corners[0] };

        for (int c = 0; c < corners.Count - 1; c++)
        {
            Vector3 segA = corners[c];
            Vector3 segB = corners[c + 1];
            float segLenXZ = Vector2.Distance(new Vector2(segA.X, segA.Z), new Vector2(segB.X, segB.Z));

            if (segLenXZ > 1e-3f)
            {
                List<(float T, Vector3 Point)> crossings = [];

                for (int i = 0; i < corridor.Count - 1; i++)
                {
                    NavMeshPolygon poly = data.Polygons[corridor[i]];
                    if (!TryGetPortalEdge(data.Vertices, poly, corridor[i + 1], out Vector3 pL, out Vector3 pR))
                    {
                        continue;
                    }

                    if (TryIntersectSegmentsXZ(segA, segB, pL, pR, out float tSeg, out float tPortal)
                        && tSeg > 0.02f && tSeg < 0.98f)
                    {
                        float portalY = float.Lerp(pL.Y, pR.Y, tPortal);
                        float lineY = float.Lerp(segA.Y, segB.Y, tSeg);
                        if (MathF.Abs(portalY - lineY) > 0.05f)
                        {
                            Vector3 crossPt = new(
                                float.Lerp(segA.X, segB.X, tSeg),
                                portalY,
                                float.Lerp(segA.Z, segB.Z, tSeg));
                            crossings.Add((tSeg, crossPt));
                        }
                    }
                }

                crossings.Sort((x, y) => x.T.CompareTo(y.T));
                foreach ((_, Vector3 pt) in crossings)
                {
                    AppendCornerIfDistinct(refined, pt);
                }
            }

            AppendCornerIfDistinct(refined, segB);
        }

        corners.Clear();
        corners.AddRange(refined);
    }

    private static bool TryGetPortalEdge(
        Vector3[] vertices,
        NavMeshPolygon poly,
        int neighborIndex,
        out Vector3 left,
        out Vector3 right)
    {
        int n = poly.VertexIndices.Length;
        for (int e = 0; e < n; e++)
        {
            if (poly.Neighbors[e] == neighborIndex)
            {
                // Polygon vertices are wound clockwise when viewed from +Y:
                // walking along edge v0 -> v1 has the polygon's interior on the RIGHT and the neighbor across the edge on the LEFT.
                // Wait: let's verify!
                // Earlier polygon winding:
                // Edge A (Left, -X): (X0, Z0) -> (X0, Z1). Interior is at +X!
                // Looking from interior (+X) toward neighbor (-X, dir = (-1, 0)):
                // Forward = (-1, 0). In XZ (+X right, +Z down-screen / forward):
                // Wait: in 3D right-handed coordinates (+X right, +Y up, +Z toward viewer):
                // Looking along (-1, 0, 0) with Up = (0, 1, 0):
                // Right-hand side = Forward x Up = (-1, 0, 0) x (0, 1, 0) = (0, 0, -1) -> Z0!
                // Left-hand side  = Up x Forward = (0, 1, 0) x (-1, 0, 0) = (0, 0, +1) -> Z1!
                // So v0 = (X0, Z0) is RIGHT, and v1 = (X0, Z1) is LEFT!
                // Let's check TriArea2XZ consistency!
                Vector3 v0 = vertices[poly.VertexIndices[e]];
                Vector3 v1 = vertices[poly.VertexIndices[(e + 1) % n]];

                // Ensure TriArea2XZ(poly.Center, left, right) < 0 (consistent with funnel tightening rules:
                // right update checks TriArea2XZ(apex, portalRight, right) <= 0 to move inward/leftward, and
                // TriArea2XZ(apex, portalLeft, right) > 0 meaning right is on the right side of left!).
                // Let's check: we need TriArea2XZ(center, left, right) > 0?
                // Wait: in StringPullFunnel:
                // "if (TriArea2XZ(portalApex, portalLeft, right) > 0f) -> Tighten!"
                // That means for a non-degenerate open funnel (apex behind portal), `TriArea2XZ(apex, left, right)` MUST BE > 0!
                if (TriArea2XZ(poly.Center, v0, v1) >= 0f)
                {
                    left = v0;
                    right = v1;
                }
                else
                {
                    left = v1;
                    right = v0;
                }

                return true;
            }
        }

        left = poly.Center;
        right = poly.Center;
        return false;
    }

    /// <summary>Signed 2x area of triangle (a, b, c) in XZ: <c>(bx - ax)*(cz - az) - (cx - ax)*(bz - az)</c>.</summary>
    private static float TriArea2XZ(Vector3 a, Vector3 b, Vector3 c)
    {
        float ax = b.X - a.X;
        float az = b.Z - a.Z;
        float bx = c.X - a.X;
        float bz = c.Z - a.Z;
        return (bx * az) - (ax * bz);
    }

    private static bool SameXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return ((dx * dx) + (dz * dz)) < 1e-6f;
    }

    private static void AppendCornerIfDistinct(List<Vector3> list, Vector3 point)
    {
        if (list.Count == 0 || Vector3.DistanceSquared(list[^1], point) > 1e-5f)
        {
            list.Add(point);
        }
    }

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
