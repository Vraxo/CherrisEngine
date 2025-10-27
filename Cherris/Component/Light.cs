using System.Numerics;

namespace Cherris;

public enum LightType
{
    Directional
}

public class Light : Component
{
    public LightType Type { get; set; } = LightType.Directional;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public float AmbientStrength { get; set; } = 2.5f;
}