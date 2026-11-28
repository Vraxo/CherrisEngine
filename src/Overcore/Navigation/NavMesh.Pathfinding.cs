using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMesh
{
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
}
