using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMeshBuilder
{
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
}
