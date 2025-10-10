using System.Numerics;
using Veldrid;

namespace Cherris;

public class Mesh
{
    public VertexPositionColor[] Vertices { get; }
    public ushort[] Indices { get; }

    public Mesh(VertexPositionColor[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
    }

    public static Mesh CreateCube()
    {
        VertexPositionColor[] vertices =
        {
            new(new(-0.5f, +0.5f, -0.5f), RgbaFloat.Red),
            new(new(+0.5f, +0.5f, -0.5f), RgbaFloat.Green),
            new(new(+0.5f, -0.5f, -0.5f), RgbaFloat.Blue),
            new(new(-0.5f, -0.5f, -0.5f), RgbaFloat.Yellow),
            new(new(-0.5f, +0.5f, +0.5f), RgbaFloat.Cyan),
            new(new(+0.5f, +0.5f, +0.5f), new RgbaFloat(1, 0, 1, 1)), // Magenta
            new(new(+0.5f, -0.5f, +0.5f), RgbaFloat.White),
            new(new(-0.5f, -0.5f, +0.5f), RgbaFloat.Grey)
        };

        ushort[] indices =
        {
            0,1,2, 0,2,3, // Front
            4,5,1, 4,1,0, // Top
            7,6,5, 7,5,4, // Back
            3,2,6, 3,6,7, // Bottom
            4,0,3, 4,3,7, // Left
            1,5,6, 1,6,2  // Right
        };

        return new Mesh(vertices, indices);
    }

    public static Mesh CreatePlane(float size)
    {
        float halfSize = size * 0.5f;
        var darkGrey = new RgbaFloat(0.25f, 0.25f, 0.25f, 1.0f);
        VertexPositionColor[] vertices =
        {
            new(new(-halfSize, 0, +halfSize), darkGrey), // 0 (top-left)
            new(new(+halfSize, 0, +halfSize), darkGrey), // 1 (top-right)
            new(new(+halfSize, 0, -halfSize), darkGrey), // 2 (bottom-right)
            new(new(-halfSize, 0, -halfSize), darkGrey)  // 3 (bottom-left)
        };

        // Reversed winding order to be visible with existing pipeline state.
        // The original order (0,1,2) was being culled.
        ushort[] indices =
        {
            0,2,1, 0,3,2
        };

        return new Mesh(vertices, indices);
    }
}