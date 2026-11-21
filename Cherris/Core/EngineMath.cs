using System.Numerics;

namespace Cherris;

public static class EngineMath
{
    /// <summary>
    /// Helper to convert a Quaternion to Euler angles (in radians).
    /// The returned Vector3 contains (Pitch, Yaw, Roll).
    /// Pitch is rotation around the X-axis.
    /// Yaw is rotation around the Y-axis.
    /// Roll is rotation around the Z-axis.
    /// </summary>
    public static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // Pitch (x-axis rotation)
        float sinp_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosp_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = float.Atan2(sinp_cosp, cosp_cosp);

        // Yaw (y-axis rotation)
        float siny = 2 * (q.W * q.Y - q.Z * q.X);
        if (float.Abs(siny) >= 1)
        {
            angles.Y = float.CopySign(MathF.PI / 2, siny); // use 90 degrees if out of range
        }
        else
        {
            angles.Y = float.Asin(siny);
        }

        // Roll (z-axis rotation)
        float sinr_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosr_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = float.Atan2(sinr_cosp, cosr_cosp);

        return angles;
    }
}