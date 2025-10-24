using System;
using System.Numerics;

namespace Cherris;

public static class EngineMath
{
    // Helper to convert Quaternion to Euler angles (in radians) for display
    public static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // Roll (x-axis rotation)
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = float.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (y-axis rotation)
        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (float.Abs(sinp) >= 1)
        {
            angles.Y = float.CopySign(MathF.PI / 2, sinp); // use 90 degrees if out of range
        }
        else
        {
            angles.Y = float.Asin(sinp);
        }

        // Yaw (z-axis rotation)
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = float.Atan2(siny_cosp, cosy_cosp);

        return angles;
    }
}