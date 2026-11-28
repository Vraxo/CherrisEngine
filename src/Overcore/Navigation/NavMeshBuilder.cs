using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

/// <summary>
/// Recast-inspired 3D navigation mesh generator:
/// 1. Extracts world-space triangles from <see cref="MeshRenderer"/> and/or <see cref="Collider"/> components.
/// 2. Rasterizes triangles into a 3D solid heightfield of vertical voxel spans, marking walkable top surfaces by slope.
/// 3. Filters walkable open spans by <see cref="NavMeshBuildSettings.AgentHeight"/> headroom, obstacle carving, and
///    <see cref="NavMeshBuildSettings.AgentRadius"/> boundary/wall erosion.
/// 4. Greedily merges co-planar, step-connected walkable spans into maximal axis-aligned convex rectangular regions.
/// 5. Emits shared vertices, convex polygons, and exact shared-portal neighbor links for A* + Funnel pathfinding.
/// </summary>
public static class NavMeshBuilder
{
    private const int MaxGridCellsPerAxis = 512;

    private struct SolidSpan
    {
        public int MinY;
        public int MaxY;
        public int Area; // -1 = unwalkable, >= 0 = walkable area index
        public int Next; // -1 = none
    }

    private struct OpenSpan
    {
        public int FloorY;
        public int CeilY;
        public int Area;
        public bool Walkable;
        public int Neighbors0; // -X
        public int Neighbors1; // +Z
        public int Neighbors2; // +X
        public int Neighbors3; // -Z
        public int RegionId;
    }

    /// <summary>Builds a <see cref="NavMeshData"/> from scene <see cref="GameObject"/>s and optional carve volumes.</summary>
    public static NavMeshData BuildFromGameObjects(
        IEnumerable<GameObject> objects,
        NavMeshBuildSettings settings,
        NavMeshCollectGeometry collectGeometry = NavMeshCollectGeometry.Both,
        LayerMask? includedLayers = null,
        int defaultArea = NavMesh.Walkable,
        BoundingBox? volumeBounds = null,
        IEnumerable<NavMeshCarveVolume>? carveVolumes = null)
    {
        ArgumentNullException.ThrowIfNull(objects);

        LayerMask mask = includedLayers ?? LayerMask.Everything;
        List<NavMeshBuildSourceTriangle> triangles = [];

        foreach (GameObject go in objects)
        {
            if (!go.ActiveInHierarchy || !mask.Contains(go.Layer))
            {
                continue;
            }

            // Skip objects that themselves carry a NavMeshObstacle or NavMeshAgent so dynamic agents/obstacles
            // aren't baked as walkable floor.
            if (go.GetComponent<NavMeshObstacle>() is not null || go.GetComponent<NavMeshAgent>() is not null)
            {
                continue;
            }

            // A terrain's forest and detail are scenery agents walk under and through, not ground they
            // walk over.
            if (Overcore.Components.Terrain.IsTreeObject(go)
                || Overcore.Components.Terrain.IsDetailObject(go))
            {
                continue;
            }

            if (collectGeometry is NavMeshCollectGeometry.RenderMeshes or NavMeshCollectGeometry.Both)
            {
                if (go.GetComponent<MeshRenderer>() is { Enabled: true, Mesh: { } mesh })
                {
                    AppendMeshTriangles(mesh, go.Transform.GetModelMatrix(), defaultArea, volumeBounds, triangles);
                }
            }

            if (collectGeometry is NavMeshCollectGeometry.PhysicsColliders or NavMeshCollectGeometry.Both)
            {
                // Avoid double-rasterizing identical geometry when Both is chosen and the object already contributed a MeshRenderer,
                // unless it has colliders without a MeshRenderer.
                bool alreadyAddedMesh = collectGeometry == NavMeshCollectGeometry.Both
                    && go.GetComponent<MeshRenderer>() is { Enabled: true, Mesh: not null };

                if (!alreadyAddedMesh)
                {
                    foreach (Collider collider in go.GetComponents<Collider>())
                    {
                        if (!collider.Enabled || collider.IsTrigger)
                        {
                            continue;
                        }

                        AppendColliderTriangles(collider, defaultArea, volumeBounds, triangles);
                    }
                }
            }
        }

        return Build(triangles, settings, volumeBounds, carveVolumes);
    }

    /// <summary>Builds a <see cref="NavMeshData"/> directly from world-space source triangles.</summary>
    public static NavMeshData Build(
        IReadOnlyList<NavMeshBuildSourceTriangle> triangles,
        NavMeshBuildSettings settings,
        BoundingBox? clipBounds = null,
        IEnumerable<NavMeshCarveVolume>? carveVolumes = null)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        if (triangles.Count == 0)
        {
            return new NavMeshData([], [], new BoundingBox { Min = Vector3.Zero, Max = Vector3.Zero }, settings);
        }

        // Compute world bounds of input triangles.
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (NavMeshBuildSourceTriangle tri in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(tri.A, Vector3.Min(tri.B, tri.C)));
            max = Vector3.Max(max, Vector3.Max(tri.A, Vector3.Max(tri.B, tri.C)));
        }

        if (clipBounds is { } clip)
        {
            min = Vector3.Max(min, clip.Min);
            max = Vector3.Min(max, clip.Max);
        }

        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
        {
            return new NavMeshData([], [], new BoundingBox { Min = Vector3.Zero, Max = Vector3.Zero }, settings);
        }

        float cellSize = MathF.Max(0.05f, settings.CellSize);
        float cellHeight = MathF.Max(0.02f, settings.CellHeight);

        // Pad Y slightly so flat floor triangles sitting on max.Y have open headroom above them.
        float agentHeightWorld = MathF.Max(cellHeight, settings.AgentHeight);
        max.Y += agentHeightWorld + (cellHeight * 2f);
        min.Y -= cellHeight;

        Vector3 extent = max - min;
        if (extent.X / cellSize > MaxGridCellsPerAxis)
        {
            cellSize = extent.X / MaxGridCellsPerAxis;
        }

        if (extent.Z / cellSize > MaxGridCellsPerAxis)
        {
            cellSize = MathF.Max(cellSize, extent.Z / MaxGridCellsPerAxis);
        }

        int width = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cellSize));
        int depth = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / cellSize));
        int maxGridY = Math.Max(4, (int)MathF.Ceiling((max.Y - min.Y) / cellHeight) + 2);

        int walkableHeightVoxels = Math.Max(1, (int)MathF.Ceiling(settings.AgentHeight / cellHeight));
        int walkableClimbVoxels = Math.Max(0, (int)MathF.Floor(settings.AgentClimb / cellHeight));
        int erosionRadiusVoxels = Math.Max(0, (int)MathF.Floor((settings.AgentRadius / cellSize) + 1e-4f));
        float minWalkableNormalY = MathF.Cos(Math.Clamp(settings.AgentSlope, 0f, 89.9f) * (MathF.PI / 180f));

        // 1. Solid heightfield linked-list per (x, z) column.
        int[] columnHeads = new int[width * depth];
        Array.Fill(columnHeads, -1);
        List<SolidSpan> solidPool = new(width * depth * 2);

        foreach (NavMeshBuildSourceTriangle tri in triangles)
        {
            Vector3 ab = tri.B - tri.A;
            Vector3 ac = tri.C - tri.A;
            Vector3 cross = Vector3.Cross(ab, ac);
            float crossLen = cross.Length();
            if (crossLen <= 1e-8f)
            {
                continue;
            }

            Vector3 normal = cross / crossLen;
            // Mesh primitives in Overcore can use either winding depending on CW front-face setup; treat upward-ish
            // triangles as walkable surfaces if |normal.Y| satisfies slope when double-sided, or normal.Y >= cos(slope).
            // Checking normal.Y directly for closed solids (cubes/capsules) prevents bottom faces from being marked walkable,
            // while for single-sheet planes normal is +Y.
            bool walkableTri = normal.Y >= minWalkableNormalY && tri.Area != NavMesh.NotWalkable;
            int area = walkableTri ? Math.Clamp(tri.Area, 0, 31) : -1;

            RasterizeTriangle(
                tri.A,
                tri.B,
                tri.C,
                area,
                min,
                cellSize,
                cellHeight,
                width,
                depth,
                maxGridY,
                columnHeads,
                solidPool);
        }

        // 2. Build Compact / Open Heightfield spans from solid tops.
        int[] cellSpanStart = new int[width * depth];
        int[] cellSpanCount = new int[width * depth];
        List<OpenSpan> openSpans = new(solidPool.Count);

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int col = (z * width) + x;
                cellSpanStart[col] = openSpans.Count;
                int count = 0;

                int curr = columnHeads[col];
                while (curr != -1)
                {
                    SolidSpan solid = solidPool[curr];
                    int next = solid.Next;
                    int floorY = solid.MaxY;
                    int ceilY = next != -1 ? solidPool[next].MinY : int.MaxValue / 2;

                    // Headroom clearance check: ceiling above this surface must fit the agent's height.
                    bool hasClearance = (ceilY - floorY) >= walkableHeightVoxels;
                    bool walkable = solid.Area >= 0 && hasClearance;

                    openSpans.Add(new OpenSpan
                    {
                        FloorY = floorY,
                        CeilY = ceilY,
                        Area = walkable ? solid.Area : NavMesh.NotWalkable,
                        Walkable = walkable,
                        Neighbors0 = -1,
                        Neighbors1 = -1,
                        Neighbors2 = -1,
                        Neighbors3 = -1,
                        RegionId = -1
                    });

                    count++;
                    curr = next;
                }

                cellSpanCount[col] = count;
            }
        }

        if (openSpans.Count == 0)
        {
            return new NavMeshData([], [], new BoundingBox { Min = min, Max = max }, settings);
        }

        // 3. Apply NavMeshObstacle carving (expanded horizontally by AgentRadius so agents steer clear of obstacle bounds).
        if (carveVolumes is not null)
        {
            List<NavMeshCarveVolume> carves = [.. carveVolumes];
            if (carves.Count > 0)
            {
                ApplyCarveVolumes(
                    carves,
                    MathF.Max(0f, settings.AgentRadius),
                    MathF.Max(0.1f, settings.AgentHeight),
                    min,
                    cellSize,
                    cellHeight,
                    width,
                    depth,
                    cellSpanStart,
                    cellSpanCount,
                    openSpans);
            }
        }

        // 4. Connect 4-directional neighbor open spans (step climb & shared headroom clearance).
        ReadOnlySpan<int> dx = [-1, 0, 1, 0];
        ReadOnlySpan<int> dz = [0, 1, 0, -1];

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int col = (z * width) + x;
                int start = cellSpanStart[col];
                int count = cellSpanCount[col];

                for (int s = 0; s < count; s++)
                {
                    int spanIdx = start + s;
                    OpenSpan span = openSpans[spanIdx];
                    if (!span.Walkable)
                    {
                        continue;
                    }

                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nx = x + dx[dir];
                        int nz = z + dz[dir];
                        if (nx < 0 || nx >= width || nz < 0 || nz >= depth)
                        {
                            continue;
                        }

                        int nCol = (nz * width) + nx;
                        int nStart = cellSpanStart[nCol];
                        int nCount = cellSpanCount[nCol];

                        int bestNeighbor = -1;
                        int bestDelta = int.MaxValue;

                        for (int ns = 0; ns < nCount; ns++)
                        {
                            int nIdx = nStart + ns;
                            OpenSpan candidate = openSpans[nIdx];
                            if (!candidate.Walkable)
                            {
                                continue;
                            }

                            int sharedFloor = Math.Max(span.FloorY, candidate.FloorY);
                            int sharedCeil = Math.Min(span.CeilY, candidate.CeilY);
                            int stepDelta = Math.Abs(candidate.FloorY - span.FloorY);

                            if ((sharedCeil - sharedFloor) >= walkableHeightVoxels
                                && stepDelta <= walkableClimbVoxels
                                && stepDelta < bestDelta)
                            {
                                bestDelta = stepDelta;
                                bestNeighbor = nIdx;
                            }
                        }

                        SetNeighbor(ref span, dir, bestNeighbor);
                    }

                    openSpans[spanIdx] = span;
                }
            }
        }

        // 5. Erode walkable area away from walls/ledges/boundaries by AgentRadius (in voxel cells)
        // when the grid has enough interior room; only erode against impassable walls/cliffs when a solid
        // wall actually blocks headroom, and erode outer ledges when grid is wide enough.
        if (erosionRadiusVoxels > 0 && width > erosionRadiusVoxels * 2 && depth > erosionRadiusVoxels * 2)
        {
            int[] distToBoundary = new int[openSpans.Count];
            const int inf = 1_000_000;

            for (int i = 0; i < openSpans.Count; i++)
            {
                OpenSpan span = openSpans[i];
                if (!span.Walkable)
                {
                    distToBoundary[i] = 0;
                    continue;
                }

                // Any missing 4-neighbor means a wall, cliff, carved obstacle, or mesh border is adjacent.
                bool touchesBorder = span.Neighbors0 < 0
                    || span.Neighbors1 < 0
                    || span.Neighbors2 < 0
                    || span.Neighbors3 < 0;

                distToBoundary[i] = touchesBorder ? 0 : inf;
            }

            // Two-pass Chamfer distance transform across the open-span graph (orthogonal = 2, diagonal = 3).
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int col = (z * width) + x;
                    int start = cellSpanStart[col];
                    int count = cellSpanCount[col];

                    for (int s = 0; s < count; s++)
                    {
                        int idx = start + s;
                        if (distToBoundary[idx] == 0)
                        {
                            continue;
                        }

                        OpenSpan span = openSpans[idx];
                        int d = distToBoundary[idx];

                        if (span.Neighbors0 >= 0)
                        {
                            d = Math.Min(d, distToBoundary[span.Neighbors0] + 2);
                            OpenSpan n0 = openSpans[span.Neighbors0];
                            if (n0.Neighbors3 >= 0)
                            {
                                d = Math.Min(d, distToBoundary[n0.Neighbors3] + 3);
                            }
                        }

                        if (span.Neighbors3 >= 0)
                        {
                            d = Math.Min(d, distToBoundary[span.Neighbors3] + 2);
                            OpenSpan n3 = openSpans[span.Neighbors3];
                            if (n3.Neighbors2 >= 0)
                            {
                                d = Math.Min(d, distToBoundary[n3.Neighbors2] + 3);
                            }
                        }

                        distToBoundary[idx] = d;
                    }
                }
            }

            for (int z = depth - 1; z >= 0; z--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int col = (z * width) + x;
                    int start = cellSpanStart[col];
                    int count = cellSpanCount[col];

                    for (int s = 0; s < count; s++)
                    {
                        int idx = start + s;
                        if (distToBoundary[idx] == 0)
                        {
                            continue;
                        }

                        OpenSpan span = openSpans[idx];
                        int d = distToBoundary[idx];

                        if (span.Neighbors2 >= 0)
                        {
                            d = Math.Min(d, distToBoundary[span.Neighbors2] + 2);
                            OpenSpan n2 = openSpans[span.Neighbors2];
                            if (n2.Neighbors1 >= 0)
                            {
                                d = Math.Min(d, distToBoundary[n2.Neighbors1] + 3);
                            }
                        }

                        if (span.Neighbors1 >= 0)
                        {
                            d = Math.Min(d, distToBoundary[span.Neighbors1] + 2);
                            OpenSpan n1 = openSpans[span.Neighbors1];
                            if (n1.Neighbors0 >= 0)
                            {
                                d = Math.Min(d, distToBoundary[n1.Neighbors0] + 3);
                            }
                        }

                        distToBoundary[idx] = d;
                    }
                }
            }

            int threshold = erosionRadiusVoxels * 2;
            for (int i = 0; i < openSpans.Count; i++)
            {
                if (openSpans[i].Walkable && distToBoundary[i] < threshold)
                {
                    OpenSpan eroded = openSpans[i];
                    eroded.Walkable = false;
                    eroded.Area = NavMesh.NotWalkable;
                    openSpans[i] = eroded;
                }
            }

            // Refresh neighbor links so surviving spans never point to eroded spans.
            for (int i = 0; i < openSpans.Count; i++)
            {
                OpenSpan span = openSpans[i];
                if (!span.Walkable)
                {
                    continue;
                }

                if (span.Neighbors0 >= 0 && !openSpans[span.Neighbors0].Walkable) span.Neighbors0 = -1;
                if (span.Neighbors1 >= 0 && !openSpans[span.Neighbors1].Walkable) span.Neighbors1 = -1;
                if (span.Neighbors2 >= 0 && !openSpans[span.Neighbors2].Walkable) span.Neighbors2 = -1;
                if (span.Neighbors3 >= 0 && !openSpans[span.Neighbors3].Walkable) span.Neighbors3 = -1;
                openSpans[i] = span;
            }
        }

        // 6. Greedily merge connected walkable spans into convex axis-aligned rectangular polygons,
        // supporting multi-layer (bridge over ground) heightfields via open-span indices.
        return BuildMergedPolygonMesh(
            min,
            cellSize,
            cellHeight,
            width,
            depth,
            walkableClimbVoxels,
            Math.Max(1, settings.MinRegionArea),
            settings,
            cellSpanStart,
            cellSpanCount,
            openSpans);
    }

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

    private static void RasterizeTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        int area,
        Vector3 gridOrigin,
        float cellSize,
        float cellHeight,
        int width,
        int depth,
        int maxGridY,
        int[] columnHeads,
        List<SolidSpan> solidPool)
    {
        float invCs = 1f / cellSize;
        float invCh = 1f / cellHeight;

        float triMinX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        float triMaxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float triMinZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
        float triMaxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));

        int x0 = Math.Clamp((int)MathF.Floor((triMinX - gridOrigin.X) * invCs), 0, width - 1);
        int x1 = Math.Clamp((int)MathF.Floor((triMaxX - gridOrigin.X) * invCs), 0, width - 1);
        int z0 = Math.Clamp((int)MathF.Floor((triMinZ - gridOrigin.Z) * invCs), 0, depth - 1);
        int z1 = Math.Clamp((int)MathF.Floor((triMaxZ - gridOrigin.Z) * invCs), 0, depth - 1);

        Span<Vector3> bufA = stackalloc Vector3[16];
        Span<Vector3> bufB = stackalloc Vector3[16];
        Span<Vector3> rowA = stackalloc Vector3[16];
        Span<Vector3> rowB = stackalloc Vector3[16];

        for (int z = z0; z <= z1; z++)
        {
            float cellMinZ = gridOrigin.Z + (z * cellSize);
            float cellMaxZ = cellMinZ + cellSize;

            bufA[0] = a;
            bufA[1] = b;
            bufA[2] = c;
            int n = 3;

            // Clip polygon to [cellMinZ, cellMaxZ].
            n = ClipPolygonAxis(bufA[..n], bufB, axis: 2, plane: cellMinZ, keepGreater: true);
            if (n < 3) continue;
            n = ClipPolygonAxis(bufB[..n], rowA, axis: 2, plane: cellMaxZ, keepGreater: false);
            if (n < 3) continue;

            float rowMinX = rowA[0].X;
            float rowMaxX = rowA[0].X;
            for (int i = 1; i < n; i++)
            {
                rowMinX = MathF.Min(rowMinX, rowA[i].X);
                rowMaxX = MathF.Max(rowMaxX, rowA[i].X);
            }

            int rx0 = Math.Clamp((int)MathF.Floor((rowMinX - gridOrigin.X) * invCs), x0, x1);
            int rx1 = Math.Clamp((int)MathF.Floor((rowMaxX - gridOrigin.X) * invCs), x0, x1);

            for (int x = rx0; x <= rx1; x++)
            {
                float cellMinX = gridOrigin.X + (x * cellSize);
                float cellMaxX = cellMinX + cellSize;

                int m = ClipPolygonAxis(rowA[..n], bufA, axis: 0, plane: cellMinX, keepGreater: true);
                if (m < 3) continue;
                m = ClipPolygonAxis(bufA[..m], rowB, axis: 0, plane: cellMaxX, keepGreater: false);
                if (m < 3) continue;

                float minY = rowB[0].Y;
                float maxY = rowB[0].Y;
                for (int i = 1; i < m; i++)
                {
                    minY = MathF.Min(minY, rowB[i].Y);
                    maxY = MathF.Max(maxY, rowB[i].Y);
                }

                int sMin = Math.Clamp((int)MathF.Floor((minY - gridOrigin.Y) * invCh), 0, maxGridY - 1);
                int sMax = Math.Clamp((int)MathF.Ceiling((maxY - gridOrigin.Y) * invCh), sMin + 1, maxGridY);

                InsertSolidSpan((z * width) + x, sMin, sMax, area, columnHeads, solidPool);
            }
        }
    }

    private static int ClipPolygonAxis(
        ReadOnlySpan<Vector3> input,
        Span<Vector3> output,
        int axis,
        float plane,
        bool keepGreater)
    {
        int count = input.Length;
        if (count == 0)
        {
            return 0;
        }

        int outCount = 0;
        Vector3 prev = input[count - 1];
        float prevDist = GetAxis(prev, axis) - plane;
        if (!keepGreater)
        {
            prevDist = -prevDist;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 curr = input[i];
            float currDist = GetAxis(curr, axis) - plane;
            if (!keepGreater)
            {
                currDist = -currDist;
            }

            bool prevIn = prevDist >= -1e-6f;
            bool currIn = currDist >= -1e-6f;

            if (prevIn != currIn)
            {
                float denom = prevDist - currDist;
                float t = MathF.Abs(denom) > 1e-8f ? Math.Clamp(prevDist / denom, 0f, 1f) : 0f;
                if (outCount < output.Length)
                {
                    output[outCount++] = Vector3.Lerp(prev, curr, t);
                }
            }

            if (currIn && outCount < output.Length)
            {
                output[outCount++] = curr;
            }

            prev = curr;
            prevDist = currDist;
        }

        return outCount;
    }

    private static float GetAxis(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z
    };

    private static void InsertSolidSpan(
        int columnIndex,
        int minY,
        int maxY,
        int area,
        int[] columnHeads,
        List<SolidSpan> pool)
    {
        int prev = -1;
        int curr = columnHeads[columnIndex];

        while (curr != -1)
        {
            SolidSpan existing = pool[curr];
            if (existing.MinY > maxY)
            {
                // Completely above the new span; insert before curr.
                break;
            }

            if (existing.MaxY < minY)
            {
                // Completely below; keep advancing.
                prev = curr;
                curr = existing.Next;
                continue;
            }

            // Overlapping or touching solid spans merge into one solid block.
            // If their top surfaces sit within 1 voxel, preserve a walkable area flag over unwalkable side walls
            // (Recast's rcAddSpan rule so vertical box side-faces don't erase the walkable top of a platform).
            if (Math.Abs(maxY - existing.MaxY) <= 1)
            {
                area = Math.Max(area, existing.Area);
            }
            else if (existing.MaxY > maxY)
            {
                area = existing.Area;
            }

            minY = Math.Min(minY, existing.MinY);
            maxY = Math.Max(maxY, existing.MaxY);

            // Unlink existing and continue merging with subsequent spans.
            int next = existing.Next;
            if (prev == -1)
            {
                columnHeads[columnIndex] = next;
            }
            else
            {
                SolidSpan prevSpan = pool[prev];
                prevSpan.Next = next;
                pool[prev] = prevSpan;
            }

            curr = next;
        }

        int newIndex = pool.Count;
        pool.Add(new SolidSpan
        {
            MinY = minY,
            MaxY = maxY,
            Area = area,
            Next = curr
        });

        if (prev == -1)
        {
            columnHeads[columnIndex] = newIndex;
        }
        else
        {
            SolidSpan prevSpan = pool[prev];
            prevSpan.Next = newIndex;
            pool[prev] = prevSpan;
        }
    }

    private static void ApplyCarveVolumes(
        List<NavMeshCarveVolume> carves,
        float agentRadius,
        float agentHeight,
        Vector3 gridOrigin,
        float cellSize,
        float cellHeight,
        int width,
        int depth,
        int[] cellSpanStart,
        int[] cellSpanCount,
        List<OpenSpan> openSpans)
    {
        foreach (NavMeshCarveVolume volume in carves)
        {
            Quaternion invRot = Quaternion.Inverse(Quaternion.Normalize(volume.Rotation));
            float maxHorizontalReach = volume.IsCapsule
                ? volume.Radius + agentRadius
                : volume.HalfExtents.Length() + agentRadius;

            float minY = volume.IsCapsule
                ? volume.Center.Y - (volume.Height * 0.5f) - agentHeight
                : volume.Center.Y - maxHorizontalReach - agentHeight;
            float maxY = volume.IsCapsule
                ? volume.Center.Y + (volume.Height * 0.5f)
                : volume.Center.Y + maxHorizontalReach;

            int x0 = Math.Clamp((int)MathF.Floor((volume.Center.X - maxHorizontalReach - gridOrigin.X) / cellSize), 0, width - 1);
            int x1 = Math.Clamp((int)MathF.Ceiling((volume.Center.X + maxHorizontalReach - gridOrigin.X) / cellSize), 0, width - 1);
            int z0 = Math.Clamp((int)MathF.Floor((volume.Center.Z - maxHorizontalReach - gridOrigin.Z) / cellSize), 0, depth - 1);
            int z1 = Math.Clamp((int)MathF.Ceiling((volume.Center.Z + maxHorizontalReach - gridOrigin.Z) / cellSize), 0, depth - 1);

            for (int z = z0; z <= z1; z++)
            {
                float worldZ = gridOrigin.Z + ((z + 0.5f) * cellSize);
                for (int x = x0; x <= x1; x++)
                {
                    float worldX = gridOrigin.X + ((x + 0.5f) * cellSize);
                    int col = (z * width) + x;
                    int start = cellSpanStart[col];
                    int count = cellSpanCount[col];

                    for (int s = 0; s < count; s++)
                    {
                        int idx = start + s;
                        OpenSpan span = openSpans[idx];
                        if (!span.Walkable)
                        {
                            continue;
                        }

                        float floorWorldY = gridOrigin.Y + (span.FloorY * cellHeight);
                        if (floorWorldY < minY || floorWorldY > maxY)
                        {
                            continue;
                        }

                        // Test whether an agent standing at (worldX, floorWorldY, worldZ) overlaps the obstacle.
                        Vector3 sampleMid = new(worldX, Math.Clamp(volume.Center.Y, floorWorldY, floorWorldY + agentHeight), worldZ);
                        bool overlaps;

                        if (volume.IsCapsule)
                        {
                            float dx = sampleMid.X - volume.Center.X;
                            float dz = sampleMid.Z - volume.Center.Z;
                            float r = volume.Radius + agentRadius;
                            float halfH = volume.Height * 0.5f;
                            bool verticalOverlap = (floorWorldY + agentHeight) >= (volume.Center.Y - halfH)
                                && floorWorldY <= (volume.Center.Y + halfH);
                            overlaps = verticalOverlap && ((dx * dx) + (dz * dz)) <= (r * r);
                        }
                        else
                        {
                            Vector3 local = Vector3.Transform(sampleMid - volume.Center, invRot);
                            Vector3 expanded = volume.HalfExtents + new Vector3(agentRadius, 0f, agentRadius);
                            overlaps = MathF.Abs(local.X) <= expanded.X
                                && MathF.Abs(local.Y) <= (volume.HalfExtents.Y + 0.05f)
                                && MathF.Abs(local.Z) <= expanded.Z;
                        }

                        if (overlaps)
                        {
                            span.Walkable = false;
                            span.Area = NavMesh.NotWalkable;
                            openSpans[idx] = span;
                        }
                    }
                }
            }
        }
    }

    private static void SetNeighbor(ref OpenSpan span, int dir, int neighbor)
    {
        switch (dir)
        {
            case 0: span.Neighbors0 = neighbor; break;
            case 1: span.Neighbors1 = neighbor; break;
            case 2: span.Neighbors2 = neighbor; break;
            case 3: span.Neighbors3 = neighbor; break;
        }
    }

    private static void AppendMeshTriangles(
        Mesh mesh,
        Matrix4x4 world,
        int area,
        BoundingBox? clipBounds,
        List<NavMeshBuildSourceTriangle> output)
    {
        Vertex[] verts = mesh.Vertices;
        uint[] indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = Vector3.Transform(verts[indices[i]].Position, world);
            Vector3 b = Vector3.Transform(verts[indices[i + 1]].Position, world);
            Vector3 c = Vector3.Transform(verts[indices[i + 2]].Position, world);

            // Check whether world matrix has negative determinant (mirrored scale) or mesh is wound CW in OpenGL;
            // Mesh.CreatePlane winds (0,2,1) with normal +Y: let's ensure upward-facing vertex normals orient the triangle!
            Vector3 avgVertexNormal = Vector3.TransformNormal(
                verts[indices[i]].Normal + verts[indices[i + 1]].Normal + verts[indices[i + 2]].Normal,
                world);
            Vector3 geoCross = Vector3.Cross(b - a, c - a);
            if (avgVertexNormal.LengthSquared() > 1e-6f && Vector3.Dot(geoCross, avgVertexNormal) < 0f)
            {
                (b, c) = (c, b);
            }

            if (clipBounds is { } clip && !TriangleOverlapsAabb(a, b, c, clip))
            {
                continue;
            }

            output.Add(new NavMeshBuildSourceTriangle(a, b, c, area));
        }
    }

    private static void AppendColliderTriangles(
        Collider collider,
        int area,
        BoundingBox? clipBounds,
        List<NavMeshBuildSourceTriangle> output)
    {
        collider.GetWorldPose(out Vector3 center, out Quaternion rotation, out Vector3 rawScale);
        Vector3 scale = Vector3.Abs(rawScale);

        Mesh? approxMesh = collider switch
        {
            BoxCollider box => CreateBoxApproximation(Vector3.Abs(box.Size) * scale),
            SphereCollider sphere => CreateSphereApproximation(sphere.Radius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z))),
            CapsuleCollider capsule => CreateBoxApproximation(new Vector3(
                capsule.Radius * 2f * MathF.Max(scale.X, scale.Z),
                MathF.Max(capsule.Height * scale.Y, capsule.Radius * 2f * MathF.Max(scale.X, scale.Z)),
                capsule.Radius * 2f * MathF.Max(scale.X, scale.Z))),
            _ => null
        };

        if (approxMesh is null)
        {
            return;
        }

        Matrix4x4 pose = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center);
        AppendMeshTriangles(approxMesh, pose, area, clipBounds, output);
    }

    private static Mesh CreateBoxApproximation(Vector3 size)
    {
        Mesh cube = Mesh.CreateCube();
        Vertex[] scaled = new Vertex[cube.Vertices.Length];
        for (int i = 0; i < scaled.Length; i++)
        {
            Vertex v = cube.Vertices[i];
            scaled[i] = new Vertex(v.Position * size, v.Normal, v.Color, v.TexCoord);
        }

        return new Mesh(scaled, cube.Indices);
    }

    private static Mesh CreateSphereApproximation(float radius)
    {
        const int latSegments = 8;
        const int lonSegments = 12;

        List<Vertex> verts = [];
        List<uint> indices = [];

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * MathF.PI / latSegments;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = lon * MathF.Tau / lonSegments;
                Vector3 normal = new(sinTheta * MathF.Cos(phi), cosTheta, sinTheta * MathF.Sin(phi));
                verts.Add(new Vertex(normal * radius, normal, Veldrid.RgbaFloat.White, Vector2.Zero));
            }
        }

        int stride = lonSegments + 1;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                uint first = (uint)((lat * stride) + lon);
                uint second = first + (uint)stride;

                indices.Add(first);
                indices.Add(first + 1);
                indices.Add(second);

                indices.Add(second);
                indices.Add(first + 1);
                indices.Add(second + 1);
            }
        }

        return new Mesh([.. verts], [.. indices]);
    }

    private static bool TriangleOverlapsAabb(Vector3 a, Vector3 b, Vector3 c, BoundingBox box)
    {
        Vector3 tMin = Vector3.Min(a, Vector3.Min(b, c));
        Vector3 tMax = Vector3.Max(a, Vector3.Max(b, c));
        return tMax.X >= box.Min.X && tMin.X <= box.Max.X
            && tMax.Y >= box.Min.Y && tMin.Y <= box.Max.Y
            && tMax.Z >= box.Min.Z && tMin.Z <= box.Max.Z;
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
