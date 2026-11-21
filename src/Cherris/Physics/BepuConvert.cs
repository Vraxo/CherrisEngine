using System.Numerics;

namespace Cherris.Core.Physics;

public static class BepuConvert
{
    // BEPUphysics v2 uses System.Numerics directly, so no conversion needed
    public static Vector3 ToBepu(this Vector3 v)
    {
        return v;
    }

    public static Vector3 ToNumerics(this Vector3 v)
    {
        return v;
    }

    public static Quaternion ToBepu(this Quaternion q)
    {
        return q;
    }

    public static Quaternion ToNumerics(this Quaternion q)
    {
        return q;
    }
}