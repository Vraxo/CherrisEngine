using System.Numerics;
using Veldrid;

namespace Cherris;

public class Mesh
{
    public Vertex[] Vertices { get; }
    public ushort[] Indices { get; }

    public Mesh(Vertex[] vertices, ushort[] indices)
    {
        Vertices = vertices;
        Indices = indices;
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

        // This cube is colored via vertex colors for demonstration.
        // Let's re-color them now that they are not white.
        var magenta = new RgbaFloat(1, 0, 1, 1);
        vertices[0].Color = RgbaFloat.Red; vertices[1].Color = RgbaFloat.Red; vertices[2].Color = RgbaFloat.Red; vertices[3].Color = RgbaFloat.Red;
        vertices[4].Color = RgbaFloat.Green; vertices[5].Color = RgbaFloat.Green; vertices[6].Color = RgbaFloat.Green; vertices[7].Color = RgbaFloat.Green;
        vertices[8].Color = RgbaFloat.Blue; vertices[9].Color = RgbaFloat.Blue; vertices[10].Color = RgbaFloat.Blue; vertices[11].Color = RgbaFloat.Blue;
        vertices[12].Color = RgbaFloat.Yellow; vertices[13].Color = RgbaFloat.Yellow; vertices[14].Color = RgbaFloat.Yellow; vertices[15].Color = RgbaFloat.Yellow;
        vertices[16].Color = RgbaFloat.Cyan; vertices[17].Color = RgbaFloat.Cyan; vertices[18].Color = RgbaFloat.Cyan; vertices[19].Color = RgbaFloat.Cyan;
        vertices[20].Color = magenta; vertices[21].Color = magenta; vertices[22].Color = magenta; vertices[23].Color = magenta;


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
        var darkGrey = new RgbaFloat(0.25f, 0.25f, 0.25f, 1.0f);
        Vertex[] vertices =
        {
            new(new(-halfSize, 0, +halfSize), darkGrey, new(0, 0)), // 0 (top-left)
            new(new(+halfSize, 0, +halfSize), darkGrey, new(size, 0)), // 1 (top-right)
            new(new(+halfSize, 0, -halfSize), darkGrey, new(size, size)), // 2 (bottom-right)
            new(new(-halfSize, 0, -halfSize), darkGrey, new(0, size))  // 3 (bottom-left)
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