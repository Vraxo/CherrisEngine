using System.Numerics;
using Veldrid;

namespace Cherris.Core;

public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public RgbaFloat Color;
    public Vector2 TexCoord;

    public const uint SizeInBytes = 12 + 12 + 16 + 8; // 48

    public Vertex(Vector3 position, Vector3 normal, RgbaFloat color, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TexCoord = texCoord;
    }
}