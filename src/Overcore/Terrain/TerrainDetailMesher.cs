using Overcore.Components;
using System.Numerics;
using Veldrid;

namespace Overcore.Terrain;

/// <summary>
/// The ground detail as one mesh: every detail pixel grows a handful of crossed-quad tufts, their count
/// read from the pixel's painted density, scattered inside the pixel by dice that roll the same way
/// every build — so rebuilding the forest after a sculpt puts the same blade in the same place, and a
/// tuft never swims between frames.
/// </summary>
/// <remarks>
/// Crossed quads are the classic grass card: two quads through each other at right angles, drawn from
/// both sides (each quad is emitted twice, once per facing), tinted dark at the root and full at the
/// tip. There is no wind in the shader yet, so the field stands still; the tufts' texture, when a
/// prototype names one, is the detail material's cutout texture.
/// </remarks>
public static class TerrainDetailMesher
{
    /// <summary>How many tufts the thickest pixel grows. Density reads against this.</summary>
    public const int MaxTuftsPerPixel = 4;

    /// <summary>
    /// Builds the detail mesh, or null when nothing grows: no prototypes, or every density zero.
    /// </summary>
    /// <param name="data">The ground and its painted detail densities.</param>
    public static Mesh? Build(TerrainData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.DetailPrototypes.Count == 0)
        {
            return null;
        }

        int resolution = data.AlphamapResolution;
        List<Vertex> vertices = [];
        List<uint> indices = [];

        for (int prototypeIndex = 0; prototypeIndex < data.DetailPrototypes.Count; prototypeIndex++)
        {
            TerrainDetailPrototype kind = data.DetailPrototypes[prototypeIndex];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float density = data.GetDetailDensity(x, z, prototypeIndex);
                    int tufts = (int)MathF.Round(density * MaxTuftsPerPixel);
                    if (tufts <= 0)
                    {
                        continue;
                    }

                    // The pixel's own dice: the scatter is a function of the pixel and the prototype,
                    // never of the build, so the same ground grows the same field twice.
                    uint state = Seed(x, z, prototypeIndex);
                    for (int tuft = 0; tuft < tufts; tuft++)
                    {
                        float u = (x + NextSingle(ref state)) / resolution;
                        float v = (z + NextSingle(ref state)) / resolution;
                        if (data.IsHoleAt(u, v))
                        {
                            continue;
                        }

                        float yaw = NextSingle(ref state) * MathF.Tau;
                        float scale = 0.8f + (NextSingle(ref state) * 0.4f);
                        Vector3 place = new(
                            u * data.Size.X,
                            data.SampleHeight(u, v) - 0.02f,
                            v * data.Size.Z);
                        AddTuft(vertices, indices, place, yaw, kind, scale);
                    }
                }
            }
        }

        return vertices.Count == 0 ? null : new Mesh([.. vertices], [.. indices]);
    }

    private static void AddTuft(
        List<Vertex> vertices,
        List<uint> indices,
        Vector3 place,
        float yaw,
        TerrainDetailPrototype kind,
        float scale)
    {
        float halfWidth = kind.Width * scale * 0.5f;
        float height = kind.Height * scale;
        RgbaFloat root = new(kind.Tint.X * 0.55f, kind.Tint.Y * 0.55f, kind.Tint.Z * 0.55f, 1f);
        RgbaFloat tip = new(kind.Tint.X, kind.Tint.Y, kind.Tint.Z, 1f);

        // Two quads crossing at the yaw and at right angles to it, each emitted facing both ways: a
        // grass card has no inside, and culling one facing would make blades vanish edge-on.
        for (int quad = 0; quad < 2; quad++)
        {
            float angle = yaw + (quad * MathF.PI * 0.5f);
            Vector3 across = new(MathF.Cos(angle) * halfWidth, 0f, MathF.Sin(angle) * halfWidth);
            Vector3 normal = new(-MathF.Sin(angle), 0f, MathF.Cos(angle));

            uint baseIndex = (uint)vertices.Count;
            vertices.Add(new Vertex(place - across, normal, root, new Vector2(0f, 0f)));
            vertices.Add(new Vertex(place + across, normal, root, new Vector2(1f, 0f)));
            vertices.Add(new Vertex(place + across + (Vector3.UnitY * height), normal, tip, new Vector2(1f, 1f)));
            vertices.Add(new Vertex(place - across + (Vector3.UnitY * height), normal, tip, new Vector2(0f, 1f)));

            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 2);
        }
    }

    private static uint Seed(int x, int z, int prototypeIndex)
    {
        uint seed = ((uint)(x + 1) * 0x9E3779B1u)
            ^ ((uint)(z + 1) * 0x85EBCA77u)
            ^ ((uint)(prototypeIndex + 1) * 0x27D4EB2Fu);
        return seed == 0u ? 0x9E3779B9u : seed;
    }

    /// <summary>The next xorshift32 roll as a 0..1 float. Small, stable across runtimes, and nobody's security.</summary>
    private static float NextSingle(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }
}
