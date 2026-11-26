using System.Numerics;
using Veldrid;

namespace Overcore.Components;

public class Mesh
{
    public Vertex[] Vertices { get; }
    public uint[] Indices { get; }
    public BoundingBox AABB { get; }

    public Mesh(Vertex[] vertices, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        Vertices = vertices;
        Indices = indices;
        AABB = CalculateAABB();
    }

    /// <summary>
    /// True when the mesh has fewer than 65,536 vertices, so its indices fit in a 16-bit buffer — the choice a
    /// backend makes between <c>UnsignedShort</c> and <c>UnsignedInt</c> draw calls.
    /// </summary>
    /// <remarks>
    /// Indices are 32-bit because a character routinely passes 65,535 vertices once it is split into UV seams;
    /// a <c>ushort</c> index array wrapped silently at that point and drew garbage. Small meshes still take the
    /// narrow path, so nothing that fits in 16 bits pays for the width.
    /// </remarks>
    public bool UsesSixteenBitIndices => Vertices.Length <= ushort.MaxValue + 1;

    private BoundingBox CalculateAABB()
    {
        if (Vertices.Length == 0)
        {
            return new() 
            { 
                Min = Vector3.Zero, 
                Max = Vector3.Zero 
            };
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (Vertex vertex in Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return new()
        {
            Min = min,
            Max = max
        };
    }

    public static Mesh CreateCube()
    {
        Vertex[] vertices =
        {
            // Top (+Y)
            new(new(-0.5f, +0.5f, -0.5f), new(0, 1, 0), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, -0.5f), new(0, 1, 0), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, +0.5f, +0.5f), new(0, 1, 0), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, +0.5f, +0.5f), new(0, 1, 0), RgbaFloat.White, new(0, 1)),
            // Bottom (-Y)
            new(new(-0.5f, -0.5f, +0.5f), new(0, -1, 0), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, -0.5f, +0.5f), new(0, -1, 0), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, -0.5f), new(0, -1, 0), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, -0.5f), new(0, -1, 0), RgbaFloat.White, new(0, 1)),
            // Left (-X)
            new(new(-0.5f, +0.5f, -0.5f), new(-1, 0, 0), RgbaFloat.White, new(0, 0)),
            new(new(-0.5f, +0.5f, +0.5f), new(-1, 0, 0), RgbaFloat.White, new(1, 0)),
            new(new(-0.5f, -0.5f, +0.5f), new(-1, 0, 0), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, -0.5f), new(-1, 0, 0), RgbaFloat.White, new(0, 1)),
            // Right (+X)
            new(new(+0.5f, +0.5f, +0.5f), new(1, 0, 0), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, -0.5f), new(1, 0, 0), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, -0.5f), new(1, 0, 0), RgbaFloat.White, new(1, 1)),
            new(new(+0.5f, -0.5f, +0.5f), new(1, 0, 0), RgbaFloat.White, new(0, 1)),
            // Front (+Z)
            new(new(-0.5f, +0.5f, +0.5f), new(0, 0, 1), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, +0.5f), new(0, 0, 1), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, +0.5f), new(0, 0, 1), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, +0.5f), new(0, 0, 1), RgbaFloat.White, new(0, 1)),
            // Back (-Z)
            new(new(+0.5f, +0.5f, -0.5f), new(0, 0, -1), RgbaFloat.White, new(0, 0)),
            new(new(-0.5f, +0.5f, -0.5f), new(0, 0, -1), RgbaFloat.White, new(1, 0)),
            new(new(-0.5f, -0.5f, -0.5f), new(0, 0, -1), RgbaFloat.White, new(1, 1)),
            new(new(+0.5f, -0.5f, -0.5f), new(0, 0, -1), RgbaFloat.White, new(0, 1)),
        };

        uint[] indices =
        {
            0,1,2, 0,2,3,
            4,5,6, 4,6,7,
            8,9,10, 8,10,11,
            12,13,14, 12,14,15,
            16,17,18, 16,18,19,
            20,21,22, 20,22,23,
        };

        return new(vertices, indices);
    }

    public static Mesh CreatePlane(float size)
    {
        float halfSize = size * 0.5f;
        var normal = new Vector3(0, 1, 0);
        Vertex[] vertices =
        {
            new(new(-halfSize, 0, +halfSize), normal, RgbaFloat.White, new(0, 0)), // 0 (top-left)
            new(new(+halfSize, 0, +halfSize), normal, RgbaFloat.White, new(1, 0)), // 1 (top-right)
            new(new(+halfSize, 0, -halfSize), normal, RgbaFloat.White, new(1, 1)), // 2 (bottom-right)
            new(new(-halfSize, 0, -halfSize), normal, RgbaFloat.White, new(0, 1))  // 3 (bottom-left)
        };

        // Reversed winding order to be visible with existing pipeline state.
        // The original order (0,1,2) was being culled.
        uint[] indices =
        {
            0,2,1, 0,3,2
        };

        return new(vertices, indices);
    }
}