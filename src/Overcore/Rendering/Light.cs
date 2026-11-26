using Overcore.Attributes;
using System.Numerics;

namespace Overcore.Components;

public enum LightType
{
    Directional,
    Point,
    Spot
}

/// <summary>How a light casts a shadow. <see cref="Soft"/> is the one that reads as a shadow rather than an edge.</summary>
public enum ShadowType
{
    /// <summary>Nothing occludes this light. It still receives other lights' shadows.</summary>
    None,

    /// <summary>A single depth comparison: a crisp, stair-stepped edge.</summary>
    Hard,

    /// <summary>A weighted tap set around the fragment: a soft edge, and the default.</summary>
    Soft
}

/// <summary>How many texels a light's shadow map is on a side.</summary>
public enum ShadowResolution
{
    Low,
    Medium,
    High,
    VeryHigh
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

    // ---------------------------------------------------------------- shadows

    /// <summary>
    /// How this light casts. The default is <see cref="ShadowType.Soft"/> on every light whatever its
    /// <see cref="Type"/>, so switching a light from point to directional does not silently leave it casting nothing.
    /// Only a directional light's value has an effect until point and spot shadows exist.
    /// </summary>
    public ShadowType Shadows { get; set; } = ShadowType.Soft;

    /// <summary>
    /// How dark the shadow is, where <c>0</c> is no shadow and <c>1</c> is black. A value below 1 is the stand-in for
    /// the bounce light a scene has and a real-time shadow map does not: it lets a little of the ambient through.
    /// </summary>
    [Range(0f, 1f, 0.01f)]
    public float ShadowStrength { get; set; } = 1f;

    /// <summary>
    /// Constant depth bias, in light-space units, before the slope term. Too little is acne; too much detaches a
    /// shadow from the feet of the thing casting it.
    /// </summary>
    [Range(0f, 0.2f, 0.001f)]
    public float ShadowBias { get; set; } = 0.02f;

    /// <summary>
    /// The normal offset, in shadow-map texels, applied to a receiver's world position before it is projected. This is
    /// the bias that fixes acne on a curved surface without darkening the contact point.
    /// </summary>
    [Range(0f, 4f, 0.01f)]
    public float ShadowNormalBias { get; set; } = 0.4f;

    /// <summary>Depth in front of the camera inside which no shadow is sampled, in world units.</summary>
    [Range(0f, 10f, 0.01f)]
    public float ShadowNearPlane { get; set; } = 0.2f;

    /// <summary>This light's shadow-map size, unless the scene's <c>Shadows</c> component overrides it.</summary>
    public ShadowResolution ShadowResolution { get; set; } = ShadowResolution.High;
}