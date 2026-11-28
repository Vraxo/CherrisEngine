using System.Numerics;

namespace Overcore.Terrain;

/// <summary>What a brush stroke does to the ground under it.</summary>
public enum TerrainBrushMode
{
    /// <summary>Lifts the ground toward the sky.</summary>
    Raise,

    /// <summary>Pushes the ground down. The exact inverse of <see cref="Raise"/>.</summary>
    Lower,

    /// <summary>Pulls the ground toward a chosen height: a plateau, a road bed, a building pad.</summary>
    Flatten,

    /// <summary>Blends each point toward the average of its neighbours, wearing edges away.</summary>
    Smooth,

    /// <summary>Paints one layer's weight up and the others down, at a normalised place.</summary>
    Paint
}

/// <summary>
/// A sculpting or painting stroke over a <see cref="TerrainData"/>, Unity's terrain brush as arithmetic.
/// </summary>
/// <remarks>
/// A stroke is a pure function of the data, a centre, a radius and a strength, with a cosine falloff that
/// reaches zero at the rim — so a stroke is testable without an editor, a window or a GPU, and the editor's
/// job is only to turn mouse movement into strokes. Heights read and written are metres; the centre and
/// radius are normalised (0..1 across the terrain) so a brush does not change size when the terrain does.
/// </remarks>
public static class TerrainBrush
{
    /// <summary>
    /// Applies one height stroke. Returns how many grid points moved, which is how a caller knows the mesh
    /// is stale (the data's <see cref="TerrainData.Version"/> says the same thing).
    /// </summary>
    /// <param name="data">The ground to sculpt.</param>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised. Points at the rim do not move.</param>
    /// <param name="strength">Metres of lift at the centre for Raise/Lower; a 0..1 pull for Flatten/Smooth.</param>
    /// <param name="mode">What the stroke does. <see cref="TerrainBrushMode.Paint"/> is not a height mode and moves nothing here.</param>
    /// <param name="flattenHeight">The height Flatten pulls toward, in metres.</param>
    public static int ApplyHeight(
        TerrainData data,
        Vector2 center,
        float radius,
        float strength,
        TerrainBrushMode mode,
        float flattenHeight = 0f)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (mode == TerrainBrushMode.Paint || radius <= 0f)
        {
            return 0;
        }

        int resolution = data.HeightmapResolution;
        float centerX = center.X * (resolution - 1);
        float centerZ = center.Y * (resolution - 1);
        float radiusPoints = radius * (resolution - 1);

        int xLow = Math.Max((int)MathF.Floor(centerX - radiusPoints), 0);
        int xHigh = Math.Min((int)MathF.Ceiling(centerX + radiusPoints), resolution - 1);
        int zLow = Math.Max((int)MathF.Floor(centerZ - radiusPoints), 0);
        int zHigh = Math.Min((int)MathF.Ceiling(centerZ + radiusPoints), resolution - 1);

        // Smooth reads the ground it is averaging, so it works from a snapshot of the region rather than
        // smearing its own writes across the stroke.
        float[,]? snapshot = mode == TerrainBrushMode.Smooth
            ? data.GetHeights(xLow, zLow, xHigh - xLow + 1, zHigh - zLow + 1)
            : null;

        int moved = 0;
        for (int z = zLow; z <= zHigh; z++)
        {
            for (int x = xLow; x <= xHigh; x++)
            {
                float distance = MathF.Sqrt(((x - centerX) * (x - centerX)) + ((z - centerZ) * (z - centerZ)));
                float falloff = Falloff(distance / radiusPoints);
                if (falloff <= 0f)
                {
                    continue;
                }

                float current = data.GetHeight(x, z);
                float next = mode switch
                {
                    TerrainBrushMode.Raise => current + (strength * falloff),
                    TerrainBrushMode.Lower => current - (strength * falloff),
                    TerrainBrushMode.Flatten => float.Lerp(current, flattenHeight, Math.Clamp(strength, 0f, 1f) * falloff),
                    TerrainBrushMode.Smooth => float.Lerp(
                        current,
                        NeighbourAverage(snapshot!, x - xLow, z - zLow),
                        Math.Clamp(strength, 0f, 1f) * falloff),
                    _ => current
                };

                if (next != current)
                {
                    data.SetHeight(x, z, next);
                    moved++;
                }
            }
        }

        return moved;
    }

    /// <summary>
    /// Paints one layer over a stroke: the layer's weight rises toward one at the centre and every other
    /// layer is scaled down to keep the pixel summing to one. Returns how many alphamap pixels changed.
    /// </summary>
    /// <param name="data">The ground to paint.</param>
    /// <param name="center">The stroke's centre, normalised across the terrain.</param>
    /// <param name="radius">The stroke's radius, normalised.</param>
    /// <param name="strength">How hard the stroke paints at its centre, 0..1.</param>
    /// <param name="layerIndex">The layer to paint with.</param>
    public static int ApplyPaint(TerrainData data, Vector2 center, float radius, float strength, int layerIndex)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (radius <= 0f || layerIndex < 0 || layerIndex >= data.Layers.Count)
        {
            return 0;
        }

        int resolution = data.AlphamapResolution;
        float centerX = center.X * (resolution - 1);
        float centerZ = center.Y * (resolution - 1);
        float radiusPoints = radius * (resolution - 1);

        int xLow = Math.Max((int)MathF.Floor(centerX - radiusPoints), 0);
        int xHigh = Math.Min((int)MathF.Ceiling(centerX + radiusPoints), resolution - 1);
        int zLow = Math.Max((int)MathF.Floor(centerZ - radiusPoints), 0);
        int zHigh = Math.Min((int)MathF.Ceiling(centerZ + radiusPoints), resolution - 1);

        int painted = 0;
        float[] weights = new float[data.Layers.Count];
        for (int z = zLow; z <= zHigh; z++)
        {
            for (int x = xLow; x <= xHigh; x++)
            {
                float distance = MathF.Sqrt(((x - centerX) * (x - centerX)) + ((z - centerZ) * (z - centerZ)));
                float falloff = Falloff(distance / radiusPoints) * Math.Clamp(strength, 0f, 1f);
                if (falloff <= 0f)
                {
                    continue;
                }

                float u = resolution == 1 ? 0f : (float)x / (resolution - 1);
                float v = resolution == 1 ? 0f : (float)z / (resolution - 1);
                float[] current = data.GetAlphamapWeights(u, v);
                Array.Copy(current, weights, Math.Min(current.Length, weights.Length));

                float target = float.Lerp(weights[layerIndex], 1f, falloff);
                float others = 0f;
                for (int layer = 0; layer < weights.Length; layer++)
                {
                    if (layer != layerIndex)
                    {
                        others += weights[layer];
                    }
                }

                float remaining = 1f - target;
                for (int layer = 0; layer < weights.Length; layer++)
                {
                    weights[layer] = layer == layerIndex
                        ? target
                        : (others > 0f ? weights[layer] / others * remaining : 0f);
                }

                data.SetAlphamapWeights(x, z, weights);
                painted++;
            }
        }

        return painted;
    }

    /// <summary>The brush's edge: one at the centre, zero at and past the rim, a cosine between.</summary>
    /// <param name="t">Distance from the centre as a fraction of the radius.</param>
    public static float Falloff(float t)
    {
        if (t >= 1f)
        {
            return 0f;
        }

        if (t <= 0f)
        {
            return 1f;
        }

        return 0.5f + (0.5f * MathF.Cos(t * MathF.PI));
    }

    private static float NeighbourAverage(float[,] snapshot, int x, int z)
    {
        int width = snapshot.GetLength(1);
        int depth = snapshot.GetLength(0);
        float sum = 0f;
        int count = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nx >= width || nz < 0 || nz >= depth)
                {
                    continue;
                }

                sum += snapshot[nz, nx];
                count++;
            }
        }

        return count > 0 ? sum / count : snapshot[z, x];
    }
}
