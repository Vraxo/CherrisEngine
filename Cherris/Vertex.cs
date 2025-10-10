using System.Numerics;
using Veldrid;

namespace Cherris;

public struct Vertex
{
    public Vector3 Position;
    public RgbaFloat Color;
    public Vector2 TexCoord;

    public const uint SizeInBytes = 12 + 16 + 8; // 36

    public Vertex(Vector3 position, RgbaFloat color, Vector2 texCoord)
    {
        Position = position;
        Color = color;
        TexCoord = texCoord;
    }
}