using Overcore.Components;
using System.Numerics;
using Veldrid;

namespace Overcore.Terrain;

/// <summary>
/// Turns a <see cref="TerrainData"/> heightfield into draw-ready <see cref="Mesh"/>es: the whole ground at
/// once, or one chunk of it.
/// </summary>
/// <remarks>
/// <para>
/// Pure and allocation-honest: the same data always mesher to the same vertices, which is what lets the
/// tests pin the geometry without a graphics device. Vertices sit in the terrain's own space — origin at
/// the corner, +X and +Z across the footprint, Y up — so the terrain object's transform places the ground
/// exactly the way Unity's does.
/// </para>
/// <para>
/// Each vertex carries its surface normal (from the data's central differences, so chunk borders agree)
/// and the tint its painted layers blend to, in the vertex colour the scene shader multiplies its texture
/// by. Texture coordinates run 0..1 over the whole terrain; a layer's own tiling is the material's tiling.
/// </para>
/// <para>
/// Triangles wind the way <see cref="Mesh.CreatePlane"/> winds them, which is the winding this renderer's
/// culling keeps — a chunk built the textbook way would be visible only from underneath.
/// </para>
/// </remarks>
public static class TerrainMesher
{
    /// <summary>Builds the whole terrain as one mesh.</summary>
    public static Mesh Build(TerrainData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return BuildChunk(data, 0, 0, data.HeightmapResolution, data.HeightmapResolution);
    }

    /// <summary>
    /// Builds the meshes of a terrain split into a grid of chunks, row-major. Chunk borders share the
    /// heightmap's edge vertices, so neighbouring chunks meet without cracks at the same resolution.
    /// </summary>
    /// <param name="data">The ground to mesh.</param>
    /// <param name="chunksPerSide">Chunks along each side. Each chunk spans an even share of grid points.</param>
    public static List<Mesh> BuildChunks(TerrainData data, int chunksPerSide)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (chunksPerSide < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chunksPerSide), chunksPerSide, "A terrain has at least one chunk.");
        }

        int resolution = data.HeightmapResolution;
        List<Mesh> meshes = [];
        for (int chunkZ = 0; chunkZ < chunksPerSide; chunkZ++)
        {
            for (int chunkX = 0; chunkX < chunksPerSide; chunkX++)
            {
                // Grid points are shared between chunks, so chunk boundaries fall on points, not between
                // them: chunk c covers points [c * (resolution - 1) / chunks, (c + 1) * (resolution - 1) /
                // chunks] inclusive, which covers the ground exactly even when the division is uneven.
                int startX = (chunkX * (resolution - 1)) / chunksPerSide;
                int startZ = (chunkZ * (resolution - 1)) / chunksPerSide;
                int endX = ((chunkX + 1) * (resolution - 1)) / chunksPerSide;
                int endZ = ((chunkZ + 1) * (resolution - 1)) / chunksPerSide;
                meshes.Add(BuildChunk(data, startX, startZ, endX - startX + 1, endZ - startZ + 1));
            }
        }

        return meshes;
    }

    /// <summary>Builds one rectangular patch of the heightfield, from a grid corner, in grid points.</summary>
    /// <param name="data">The ground to mesh.</param>
    /// <param name="startX">First grid column of the patch.</param>
    /// <param name="startZ">First grid row of the patch.</param>
    /// <param name="pointsX">Grid points across the patch, at least 1.</param>
    /// <param name="pointsZ">Grid points along the patch, at least 1.</param>
    public static Mesh BuildChunk(TerrainData data, int startX, int startZ, int pointsX, int pointsZ)
    {
        ArgumentNullException.ThrowIfNull(data);
        int resolution = data.HeightmapResolution;
        if (startX < 0 || startZ < 0 || pointsX < 1 || pointsZ < 1
            || startX + pointsX > resolution || startZ + pointsZ > resolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startX),
                $"The patch ({startX}, {startZ}) + {pointsX}×{pointsZ} leaves the {resolution}² heightmap.");
        }

        Vertex[] vertices = new Vertex[pointsX * pointsZ];
        for (int z = 0; z < pointsZ; z++)
        {
            for (int x = 0; x < pointsX; x++)
            {
                int gridX = startX + x;
                int gridZ = startZ + z;
                float u = (float)gridX / (resolution - 1);
                float v = (float)gridZ / (resolution - 1);

                Vector3 position = new(u * data.Size.X, data.GetHeight(gridX, gridZ), v * data.Size.Z);
                Vector3 normal = data.GetNormalAtGrid(gridX, gridZ);
                Vector3 tint = data.GetBlendedTint(u, v);
                RgbaFloat color = new(tint.X, tint.Y, tint.Z, 1f);

                vertices[(z * pointsX) + x] = new Vertex(position, normal, color, new Vector2(u, v));
            }
        }

        List<uint> indices = [];
        for (int z = 0; z < pointsZ - 1; z++)
        {
            for (int x = 0; x < pointsX - 1; x++)
            {
                // A hole cell has no ground in it: no quad, and therefore nothing to collide with or bake.
                if (data.IsHole(startX + x, startZ + z))
                {
                    continue;
                }

                // The CreatePlane winding: a is the far corner of the quad, and the triangles run a-c-b, a-d-c.
                uint a = (uint)(((z + 1) * pointsX) + x);
                uint b = (uint)(((z + 1) * pointsX) + x + 1);
                uint c = (uint)((z * pointsX) + x + 1);
                uint d = (uint)((z * pointsX) + x);

                indices.Add(a);
                indices.Add(c);
                indices.Add(b);
                indices.Add(a);
                indices.Add(d);
                indices.Add(c);
            }
        }

        return new Mesh(vertices, [.. indices]);
    }

    /// <summary>
    /// The terrain's triangles as bare positions, in the terrain's own space: what a physics mesh or a
    /// NavMesh bake consumes when it wants the ground without the drawing.
    /// </summary>
    public static List<(Vector3 A, Vector3 B, Vector3 C)> BuildTriangles(TerrainData data)
    {
        Mesh mesh = Build(data);
        List<(Vector3, Vector3, Vector3)> triangles = new(mesh.Indices.Length / 3);
        for (int index = 0; index < mesh.Indices.Length; index += 3)
        {
            triangles.Add((
                mesh.Vertices[mesh.Indices[index]].Position,
                mesh.Vertices[mesh.Indices[index + 1]].Position,
                mesh.Vertices[mesh.Indices[index + 2]].Position));
        }

        return triangles;
    }
}
