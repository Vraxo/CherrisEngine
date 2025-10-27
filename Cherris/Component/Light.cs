using System.Numerics;

namespace Cherris;

public enum LightType
{
    Directional,
    Point,
    Spot
}

public class Light : Component
{
    public LightType Type { get; set; } = LightType.Directional;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;

    // Used by DirectionalLight for global ambient term
    public float AmbientStrength { get; set; } = 0.3f;

    // Point Light / Spot Light
    public float Range { get; set; } = 50.0f;

    // Spot Light
    public float InnerConeAngle { get; set; } = 12.5f; // In degrees
    public float OuterConeAngle { get; set; } = 17.5f; // In degrees
}