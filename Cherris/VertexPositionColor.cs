using System.Numerics;
using Veldrid;

namespace VeldridCube
{
    public struct VertexPositionColor
    {
        public Vector3 Position;
        public RgbaFloat Color;

        public const uint SizeInBytes = 28;

        public VertexPositionColor(Vector3 position, RgbaFloat color)
        {
            Position = position;
            Color = color;
        }
    }
}