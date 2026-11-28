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
public static partial class NavMeshBuilder
{
    private const int MaxGridCellsPerAxis = 512;

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
}
