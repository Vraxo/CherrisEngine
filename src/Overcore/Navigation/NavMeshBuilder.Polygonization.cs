using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMeshBuilder
{
    private static NavMeshData BuildMergedPolygonMesh(
        Vector3 gridOrigin,
        float cellSize,
        float cellHeight,
        int width,
        int depth,
        int maxStepVoxels,
        int minRegionCells,
        NavMeshBuildSettings settings,
        int[] cellSpanStart,
        int[] cellSpanCount,
        List<OpenSpan> openSpans)
    {
        List<RectRegion> regions = [];

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int col = (z * width) + x;
                int count = cellSpanCount[col];
                for (int s = 0; s < count; s++)
                {
                    int seedIdx = cellSpanStart[col] + s;
                    OpenSpan seed = openSpans[seedIdx];
                    if (!seed.Walkable || seed.RegionId >= 0)
                    {
                        continue;
                    }

                    // Grow along +X first while maintaining flat/sloped continuity (within maxStepVoxels across cells
                    // and total elevation drift bounded so steep stairs stay piecewise-accurate).
                    List<int> rowIndices = [seedIdx];
                    int curIdx = seedIdx;
                    int endX = x;

                    while (endX + 1 < width)
                    {
                        OpenSpan cur = openSpans[curIdx];
                        int nextIdx = cur.Neighbors2; // +X
                        if (nextIdx < 0)
                        {
                            break;
                        }

                        OpenSpan next = openSpans[nextIdx];
                        if (!next.Walkable
                            || next.RegionId >= 0
                            || next.Area != seed.Area
                            || Math.Abs(next.FloorY - seed.FloorY) > Math.Max(1, maxStepVoxels))
                        {
                            break;
                        }

                        rowIndices.Add(nextIdx);
                        curIdx = nextIdx;
                        endX++;
                    }

                    int rectWidth = rowIndices.Count;
                    List<int> allSpanIndices = [.. rowIndices];
                    int[] prevRow = [.. rowIndices];
                    int endZ = z;

                    while (endZ + 1 < depth)
                    {
                        int[] nextRow = new int[rectWidth];
                        bool validRow = true;

                        for (int rx = 0; rx < rectWidth; rx++)
                        {
                            OpenSpan abovePrev = openSpans[prevRow[rx]];
                            int candidateIdx = abovePrev.Neighbors1; // +Z
                            if (candidateIdx < 0)
                            {
                                validRow = false;
                                break;
                            }

                            OpenSpan candidate = openSpans[candidateIdx];
                            if (!candidate.Walkable
                                || candidate.RegionId >= 0
                                || candidate.Area != seed.Area
                                || Math.Abs(candidate.FloorY - seed.FloorY) > Math.Max(1, maxStepVoxels))
                            {
                                validRow = false;
                                break;
                            }

                            // Verify horizontal connectivity inside the new row too.
                            if (rx > 0 && openSpans[nextRow[rx - 1]].Neighbors2 != candidateIdx)
                            {
                                validRow = false;
                                break;
                            }

                            nextRow[rx] = candidateIdx;
                        }

                        if (!validRow)
                        {
                            break;
                        }

                        allSpanIndices.AddRange(nextRow);
                        prevRow = nextRow;
                        endZ++;
                    }

                    if (allSpanIndices.Count < minRegionCells)
                    {
                        continue;
                    }

                    int regionId = regions.Count;
                    int sumFloorY = 0;
                    int minFloorY = int.MaxValue;
                    int maxFloorY = int.MinValue;

                    foreach (int idx in allSpanIndices)
                    {
                        OpenSpan marked = openSpans[idx];
                        marked.RegionId = regionId;
                        openSpans[idx] = marked;
                        sumFloorY += marked.FloorY;
                        minFloorY = Math.Min(minFloorY, marked.FloorY);
                        maxFloorY = Math.Max(maxFloorY, marked.FloorY);
                    }

                    regions.Add(new RectRegion(
                        regionId,
                        x,
                        z,
                        endX + 1,
                        endZ + 1,
                        seed.Area,
                        sumFloorY / (float)allSpanIndices.Count,
                        minFloorY,
                        maxFloorY,
                        allSpanIndices));
                }
            }
        }

        if (regions.Count == 0)
        {
            return new NavMeshData([], [], new BoundingBox { Min = gridOrigin, Max = gridOrigin }, settings);
        }

        // Split rectangle edges at T-junctions where adjacent rectangles begin/end along a shared edge!
        // Splitting edges at neighbor transition points guarantees every adjacent region pair shares an exact
        // vertex-to-vertex portal edge — essential for Simple Stupid Funnel string-pulling and multi-neighbor polygons.
        List<Vector3> vertices = [];
        Dictionary<QuantizedVertexKey, int> vertexLookup = [];

        int AddOrGetVertex(int gx, int gz, float worldY)
        {
            int qy = (int)MathF.Round(worldY / MathF.Max(0.01f, cellHeight * 2f));
            QuantizedVertexKey key = new(gx, qy, gz);
            if (vertexLookup.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int idx = vertices.Count;
            vertices.Add(new Vector3(
                gridOrigin.X + (gx * cellSize),
                worldY,
                gridOrigin.Z + (gz * cellSize)));
            vertexLookup[key] = idx;
            return idx;
        }

        // Discover portal intervals between regions from the voxel span neighbor graph.
        // Direction 0: -X (left edge x = X0, z in [Z0, Z1])
        // Direction 1: +Z (top edge z = Z1, x in [X0, X1])
        // Direction 2: +X (right edge x = X1, z in [Z0, Z1])
        // Direction 3: -Z (bottom edge z = Z0, x in [X0, X1])
        List<NavMeshPolygon> polygons = new(regions.Count);
        Vector3 meshMin = new(float.MaxValue);
        Vector3 meshMax = new(float.MinValue);

        for (int rIdx = 0; rIdx < regions.Count; rIdx++)
        {
            RectRegion reg = regions[rIdx];
            int rw = reg.X1 - reg.X0;
            int rd = reg.Z1 - reg.Z0;

            float baseWorldY = gridOrigin.Y + (reg.AvgFloorY * cellHeight);

            float SampleBorderY(int localX, int localZ, int neighborIdx)
            {
                int ownSpanIdx = reg.SpanIndices[(localZ * rw) + localX];
                float ownY = gridOrigin.Y + (openSpans[ownSpanIdx].FloorY * cellHeight);
                if (neighborIdx >= 0)
                {
                    float nY = gridOrigin.Y + (openSpans[neighborIdx].FloorY * cellHeight);
                    return (ownY + nY) * 0.5f;
                }

                return ownY;
            }

            List<int> polyVerts = [];
            List<int> polyNeighbors = [];

            void EmitEdge(int vStart, int vEnd, int neighborRegion)
            {
                if (vStart == vEnd)
                {
                    return;
                }

                if (polyVerts.Count == 0 || polyVerts[^1] != vStart)
                {
                    polyVerts.Add(vStart);
                }

                polyNeighbors.Add(neighborRegion);
            }

            // Walk perimeter clockwise when viewed from +Y:
            // Top (+Z: z = Z1, x from X0 -> X1), Right (+X: x = X1, z from Z1 -> Z0),
            // Bottom (-Z: z = Z0, x from X1 -> X0), Left (-X: x = X0, z from Z0 -> Z1).
            // Wait: in right-handed XZ with +X right and +Z forward/down:
            // (X0, Z0) -> (X0, Z1) -> (X1, Z1) -> (X1, Z0) has cross((0,0,1), (1,0,0)) = (0, +1, 0) (+Y normal)!
            // Let's verify: A=(0,0,0), B=(0,0,1), C=(1,0,1):
            // AB = (0,0,1), AC = (1,0,1), AB x AC = (0*1 - 1*0, 1*1 - 0*1, 0*0 - 0*1) = (0, +1, 0)!
            // Yes! Ordering:
            // Edge A (Left, -X): x = X0, z from Z0 -> Z1 (dir 0)
            // Edge B (Top, +Z):  z = Z1, x from X0 -> X1 (dir 1)
            // Edge C (Right, +X): x = X1, z from Z1 -> Z0 (dir 2)
            // Edge D (Bottom, -Z): z = Z0, x from X1 -> X0 (dir 3)

            // Edge A: Left (x = X0, z: Z0 -> Z1)
            {
                int runStart = 0;
                while (runStart < rd)
                {
                    int span0 = reg.SpanIndices[(runStart * rw) + 0];
                    int nSpan0 = openSpans[span0].Neighbors0;
                    int nReg = nSpan0 >= 0 ? openSpans[nSpan0].RegionId : -1;

                    int runEnd = runStart + 1;
                    while (runEnd < rd)
                    {
                        int sIdx = reg.SpanIndices[(runEnd * rw) + 0];
                        int ns = openSpans[sIdx].Neighbors0;
                        int nr = ns >= 0 ? openSpans[ns].RegionId : -1;
                        if (nr != nReg)
                        {
                            break;
                        }

                        runEnd++;
                    }

                    float y0 = SampleBorderY(0, runStart, nSpan0);
                    int spanLast = reg.SpanIndices[((runEnd - 1) * rw) + 0];
                    float y1 = SampleBorderY(0, runEnd - 1, openSpans[spanLast].Neighbors0);

                    int v0 = AddOrGetVertex(reg.X0, reg.Z0 + runStart, y0);
                    int v1 = AddOrGetVertex(reg.X0, reg.Z0 + runEnd, y1);
                    EmitEdge(v0, v1, nReg);
                    runStart = runEnd;
                }
            }

            // Edge B: Top (z = Z1, x: X0 -> X1)
            {
                int topLocalZ = rd - 1;
                int runStart = 0;
                while (runStart < rw)
                {
                    int span0 = reg.SpanIndices[(topLocalZ * rw) + runStart];
                    int nSpan0 = openSpans[span0].Neighbors1;
                    int nReg = nSpan0 >= 0 ? openSpans[nSpan0].RegionId : -1;

                    int runEnd = runStart + 1;
                    while (runEnd < rw)
                    {
                        int sIdx = reg.SpanIndices[(topLocalZ * rw) + runEnd];
                        int ns = openSpans[sIdx].Neighbors1;
                        int nr = ns >= 0 ? openSpans[ns].RegionId : -1;
                        if (nr != nReg)
                        {
                            break;
                        }

                        runEnd++;
                    }

                    float y0 = SampleBorderY(runStart, topLocalZ, nSpan0);
                    int spanLast = reg.SpanIndices[(topLocalZ * rw) + (runEnd - 1)];
                    float y1 = SampleBorderY(runEnd - 1, topLocalZ, openSpans[spanLast].Neighbors1);

                    int v0 = AddOrGetVertex(reg.X0 + runStart, reg.Z1, y0);
                    int v1 = AddOrGetVertex(reg.X0 + runEnd, reg.Z1, y1);
                    EmitEdge(v0, v1, nReg);
                    runStart = runEnd;
                }
            }

            // Edge C: Right (x = X1, z: Z1 -> Z0)
            {
                int rightLocalX = rw - 1;
                int runStart = rd - 1;
                while (runStart >= 0)
                {
                    int span0 = reg.SpanIndices[(runStart * rw) + rightLocalX];
                    int nSpan0 = openSpans[span0].Neighbors2;
                    int nReg = nSpan0 >= 0 ? openSpans[nSpan0].RegionId : -1;

                    int runEnd = runStart - 1;
                    while (runEnd >= 0)
                    {
                        int sIdx = reg.SpanIndices[(runEnd * rw) + rightLocalX];
                        int ns = openSpans[sIdx].Neighbors2;
                        int nr = ns >= 0 ? openSpans[ns].RegionId : -1;
                        if (nr != nReg)
                        {
                            break;
                        }

                        runEnd--;
                    }

                    float y0 = SampleBorderY(rightLocalX, runStart, nSpan0);
                    int spanLast = reg.SpanIndices[((runEnd + 1) * rw) + rightLocalX];
                    float y1 = SampleBorderY(rightLocalX, runEnd + 1, openSpans[spanLast].Neighbors2);

                    int v0 = AddOrGetVertex(reg.X1, reg.Z0 + runStart + 1, y0);
                    int v1 = AddOrGetVertex(reg.X1, reg.Z0 + runEnd + 1, y1);
                    EmitEdge(v0, v1, nReg);
                    runStart = runEnd;
                }
            }

            // Edge D: Bottom (z = Z0, x: X1 -> X0)
            {
                int runStart = rw - 1;
                while (runStart >= 0)
                {
                    int span0 = reg.SpanIndices[runStart];
                    int nSpan0 = openSpans[span0].Neighbors3;
                    int nReg = nSpan0 >= 0 ? openSpans[nSpan0].RegionId : -1;

                    int runEnd = runStart - 1;
                    while (runEnd >= 0)
                    {
                        int sIdx = reg.SpanIndices[runEnd];
                        int ns = openSpans[sIdx].Neighbors3;
                        int nr = ns >= 0 ? openSpans[ns].RegionId : -1;
                        if (nr != nReg)
                        {
                            break;
                        }

                        runEnd--;
                    }

                    float y0 = SampleBorderY(runStart, 0, nSpan0);
                    int spanLast = reg.SpanIndices[runEnd + 1];
                    float y1 = SampleBorderY(runEnd + 1, 0, openSpans[spanLast].Neighbors3);

                    int v0 = AddOrGetVertex(reg.X0 + runStart + 1, reg.Z0, y0);
                    int v1 = AddOrGetVertex(reg.X0 + runEnd + 1, reg.Z0, y1);
                    EmitEdge(v0, v1, nReg);
                    runStart = runEnd;
                }
            }

            Vector3 pMin = new(float.MaxValue);
            Vector3 pMax = new(float.MinValue);
            Vector3 sum = Vector3.Zero;

            foreach (int vi in polyVerts)
            {
                Vector3 v = vertices[vi];
                pMin = Vector3.Min(pMin, v);
                pMax = Vector3.Max(pMax, v);
                sum += v;
            }

            Vector3 center = polyVerts.Count > 0
                ? new Vector3(sum.X / polyVerts.Count, baseWorldY, sum.Z / polyVerts.Count)
                : Vector3.Zero;

            meshMin = Vector3.Min(meshMin, pMin);
            meshMax = Vector3.Max(meshMax, pMax);

            polygons.Add(new NavMeshPolygon
            {
                VertexIndices = [.. polyVerts],
                Neighbors = [.. polyNeighbors],
                Area = reg.Area,
                Center = center,
                Normal = Vector3.UnitY,
                Bounds = new BoundingBox { Min = pMin, Max = pMax }
            });
        }

        // Ensure neighbor symmetry across shared edges even when corner height quantization caused asymmetric links.
        EnsureSymmetricPortals(polygons, vertices, cellSize * 0.5f, MathF.Max(cellHeight * (maxStepVoxels + 2), 0.5f));

        return new NavMeshData(
            [.. vertices],
            polygons,
            new BoundingBox { Min = meshMin, Max = meshMax },
            settings);
    }

    private static void EnsureSymmetricPortals(
        List<NavMeshPolygon> polygons,
        List<Vector3> vertices,
        float xzTolerance,
        float yTolerance)
    {
        for (int aIdx = 0; aIdx < polygons.Count; aIdx++)
        {
            NavMeshPolygon polyA = polygons[aIdx];
            int countA = polyA.VertexIndices.Length;

            for (int ea = 0; ea < countA; ea++)
            {
                int bIdx = polyA.Neighbors[ea];
                if (bIdx < 0 || bIdx >= polygons.Count || bIdx == aIdx)
                {
                    polyA.Neighbors[ea] = -1;
                    continue;
                }

                NavMeshPolygon polyB = polygons[bIdx];
                Vector3 a0 = vertices[polyA.VertexIndices[ea]];
                Vector3 a1 = vertices[polyA.VertexIndices[(ea + 1) % countA]];

                bool foundReverse = false;
                int countB = polyB.VertexIndices.Length;
                for (int eb = 0; eb < countB; eb++)
                {
                    if (polyB.Neighbors[eb] == aIdx)
                    {
                        foundReverse = true;
                        break;
                    }
                }

                if (!foundReverse)
                {
                    // Try matching opposite edge geometry within tolerance.
                    for (int eb = 0; eb < countB; eb++)
                    {
                        if (polyB.Neighbors[eb] >= 0)
                        {
                            continue;
                        }

                        Vector3 b0 = vertices[polyB.VertexIndices[eb]];
                        Vector3 b1 = vertices[polyB.VertexIndices[(eb + 1) % countB]];

                        if (SamePointApprox(a0, b1, xzTolerance, yTolerance)
                            && SamePointApprox(a1, b0, xzTolerance, yTolerance))
                        {
                            polyB.Neighbors[eb] = aIdx;
                            foundReverse = true;
                            break;
                        }
                    }
                }

                if (!foundReverse)
                {
                    polyA.Neighbors[ea] = -1;
                }
            }
        }
    }

    private static bool SamePointApprox(Vector3 p, Vector3 q, float xzTol, float yTol)
    {
        float dx = p.X - q.X;
        float dz = p.Z - q.Z;
        return ((dx * dx) + (dz * dz)) <= (xzTol * xzTol) && MathF.Abs(p.Y - q.Y) <= yTol;
    }

    private readonly record struct QuantizedVertexKey(int X, int Y, int Z);

    private sealed record RectRegion(
        int Id,
        int X0,
        int Z0,
        int X1,
        int Z1,
        int Area,
        float AvgFloorY,
        int MinFloorY,
        int MaxFloorY,
        List<int> SpanIndices);
}
