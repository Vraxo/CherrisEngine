using Jitter.LinearMath;
using System.Numerics;

namespace Cherris;

public static class JitterConvert
{
    public static JVector ToJitter(this Vector3 v)
    {
        return new(v.X, v.Y, v.Z);
    }

    public static Vector3 ToNumerics(this JVector v)
    {
        return new(v.X, v.Y, v.Z);
    }

    public static JMatrix ToJitter(this Quaternion q)
    {
        JQuaternion jq = new(q.X, q.Y, q.Z, q.W);
        JMatrix.CreateFromQuaternion(ref jq, out var matrix);
        return matrix;
    }

    public static Quaternion ToNumerics(this JMatrix m)
    {
        JQuaternion.CreateFromMatrix(ref m, out var jq);
        return new(jq.X, jq.Y, jq.Z, jq.W);
    }
}