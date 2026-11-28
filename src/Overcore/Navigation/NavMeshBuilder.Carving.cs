using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMeshBuilder
{
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
}
