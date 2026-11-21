using Cherris.Attributes;
using System.Numerics;

namespace Cherris.Components;

public enum LightType
{
    Directional,
    Point,
    Spot
}

public class Light : Component
{
    public LightType Type { get; set; } = LightType.Directional;

    [ColorUsage]
    public Vector3 Color { get; set; } = Vector3.One;

    [Range(0f, float.MaxValue, 0.1f)]
    public float Intensity { get; set; } = 1.0f;

    [Range(0f, 1f, 0.01f)]
    public float AmbientStrength { get; set; } = 0.3f;

    [Range(0.1f, float.MaxValue, 0.1f)]
    public float Range { get; set; } = 50.0f;

    [Range(0f, 89f, 0.1f)]
    public float InnerConeAngle { get; set; } = 12.5f;

    [Range(0f, 89f, 0.1f)]
    public float OuterConeAngle { get; set; } = 17.5f;
}