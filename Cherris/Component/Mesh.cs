using System.Numerics;
using Veldrid;

namespace Cherris;

public struct BoundingBox
{
    public Vector3 Min;
    public Vector3 Max;

    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }
}

public class Mesh
{
    public Vertex[] Vertices { get; }
    public ushort[] Indices { get; }
    public BoundingBox AABB { get; }

    public Mesh(Vertex[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
        AABB = CalculateAABB();
    }

    private BoundingBox CalculateAABB()
    {
        if (Vertices.Length == 0)
        {
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var vertex in Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return new BoundingBox(min, max);
    }

    public static Mesh CreateCube()
    {
        Vertex[] vertices =
        {
            // Top
            new(new(-0.5f, +0.5f, -0.5f), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, -0.5f), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, +0.5f, +0.5f), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, +0.5f, +0.5f), RgbaFloat.White, new(0, 1)),
            // Bottom
            new(new(-0.5f, -0.5f, +0.5f), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, -0.5f, +0.5f), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, -0.5f), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, -0.5f), RgbaFloat.White, new(0, 1)),
            // Left
            new(new(-0.5f, +0.5f, -0.5f), RgbaFloat.White, new(0, 0)),
            new(new(-0.5f, +0.5f, +0.5f), RgbaFloat.White, new(1, 0)),
            new(new(-0.5f, -0.5f, +0.5f), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, -0.5f), RgbaFloat.White, new(0, 1)),
            // Right
            new(new(+0.5f, +0.5f, +0.5f), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, -0.5f), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, -0.5f), RgbaFloat.White, new(1, 1)),
            new(new(+0.5f, -0.5f, +0.5f), RgbaFloat.White, new(0, 1)),
            // Front
            new(new(-0.5f, +0.5f, +0.5f), RgbaFloat.White, new(0, 0)),
            new(new(+0.5f, +0.5f, +0.5f), RgbaFloat.White, new(1, 0)),
            new(new(+0.5f, -0.5f, +0.5f), RgbaFloat.White, new(1, 1)),
            new(new(-0.5f, -0.5f, +0.5f), RgbaFloat.White, new(0, 1)),
            // Back
            new(new(+0.5f, +0.5f, -0.5f), RgbaFloat.White, new(0, 0)),
            new(new(-0.5f, +0.5f, -0.5f), RgbaFloat.White, new(1, 0)),
            new(new(-0.5f, -0.5f, -0.5f), RgbaFloat.White, new(1, 1)),
            new(new(+0.5f, -0.5f, -0.5f), RgbaFloat.White, new(0, 1)),
        };

        ushort[] indices =
        {
            0,1,2, 0,2,3,
            4,5,6, 4,6,7,
            8,9,10, 8,10,11,
            12,13,14, 12,14,15,
            16,17,18, 16,18,19,
            20,21,22, 20,22,23,
        };

        return new Mesh(vertices, indices);
    }

    public static Mesh CreatePlane(float size)
    {
        float halfSize = size * 0.5f;
        // Give the plane a tiny thickness so the inverted hull outline technique works.
        float thickness = 0.001f;

        Vertex[] vertices =
        {
            // Top face (Y = 0)
            new(new(-halfSize, 0, -halfSize), RgbaFloat.White, new(0, 1)),
            new(new(+halfSize, 0, -halfSize), RgbaFloat.White, new(1, 1)),
            new(new(+halfSize, 0, +halfSize), RgbaFloat.White, new(1, 0)),
            new(new(-halfSize, 0, +halfSize), RgbaFloat.White, new(0, 0)),
            // Bottom face (Y = -thickness)
            new(new(-halfSize, -thickness, -halfSize), RgbaFloat.White, new(0, 1)),
            new(new(+halfSize, -thickness, -halfSize), RgbaFloat.White, new(1, 1)),
            new(new(+halfSize, -thickness, +halfSize), RgbaFloat.White, new(1, 0)),
            new(new(-halfSize, -thickness, +halfSize), RgbaFloat.White, new(0, 0)),
            // Front face (+Z)
            new(new(-halfSize, 0, +halfSize), RgbaFloat.White, new(0, 0)),
            new(new(+halfSize, 0, +halfSize), RgbaFloat.White, new(1, 0)),
            new(new(+halfSize, -thickness, +halfSize), RgbaFloat.White, new(1, 1)),
            new(new(-halfSize, -thickness, +halfSize), RgbaFloat.White, new(0, 1)),
            // Back face (-Z)
            new(new(+halfSize, 0, -halfSize), RgbaFloat.White, new(0, 0)),
            new(new(-halfSize, 0, -halfSize), RgbaFloat.White, new(1, 0)),
            new(new(-halfSize, -thickness, -halfSize), RgbaFloat.White, new(1, 1)),
            new(new(+halfSize, -thickness, -halfSize), RgbaFloat.White, new(0, 1)),
            // Left face (-X)
            new(new(-halfSize, 0, -halfSize), RgbaFloat.White, new(0, 0)),
            new(new(-halfSize, 0, +halfSize), RgbaFloat.White, new(1, 0)),
            new(new(-halfSize, -thickness, +halfSize), RgbaFloat.White, new(1, 1)),
            new(new(-halfSize, -thickness, -halfSize), RgbaFloat.White, new(0, 1)),
            // Right face (+X)
            new(new(+halfSize, 0, +halfSize), RgbaFloat.White, new(0, 0)),
            new(new(+halfSize, 0, -halfSize), RgbaFloat.White, new(1, 0)),
            new(new(+halfSize, -thickness, -halfSize), RgbaFloat.White, new(1, 1)),
            new(new(+halfSize, -thickness, +halfSize), RgbaFloat.White, new(0, 1)),
        };

        ushort[] indices =
        {
            0,1,2,   0,2,3,   // Top
            4,5,6,   4,6,7,   // Bottom
            8,9,10,  8,10,11, // Front
            12,13,14, 12,14,15, // Back
            16,17,18, 16,18,19, // Left
            20,21,22, 20,22,23  // Right
        };

        return new Mesh(vertices, indices);
    }
}