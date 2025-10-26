using System.Numerics;
using Jitter.LinearMath;

namespace Cherris
{
    public static class JitterConvert
    {
        public static JVector ToJitter(this Vector3 v)
        {
            return new JVector(v.X, v.Y, v.Z);
        }

        public static Vector3 ToNumerics(this JVector v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        public static JMatrix ToJitter(this Quaternion q)
        {
            var jq = new JQuaternion(q.X, q.Y, q.Z, q.W);
            JMatrix.CreateFromQuaternion(ref jq, out var matrix);
            return matrix;
        }

        public static Quaternion ToNumerics(this JMatrix m)
        {
            JQuaternion.CreateFromMatrix(ref m, out var jq);
            return new Quaternion(jq.X, jq.Y, jq.Z, jq.W);
        }
    }
}