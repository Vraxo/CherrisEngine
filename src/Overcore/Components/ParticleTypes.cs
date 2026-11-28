using Overcore.Attributes;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>Coordinate space in which particles are simulated after emission.</summary>
public enum ParticleSimulationSpace
{
    /// <summary>Particles move along with the emitter's <see cref="Transform"/>.</summary>
    Local,

    /// <summary>Particles detach into world space the moment they spawn.</summary>
    World
}

/// <summary>Emitter shape volume used when spawning new particles.</summary>
public enum ParticleShapeType
{
    Cone,
    Sphere,
    Hemisphere,
    Box,
    Circle
}

/// <summary>Blend state used when drawing particles.</summary>
public enum ParticleBlendMode
{
    /// <summary>Standard alpha blending (<c>SrcAlpha, OneMinusSrcAlpha</c>).</summary>
    Alpha,

    /// <summary>Additive glow blending (<c>SrcAlpha, One</c>).</summary>
    Additive
}

/// <summary>How each particle quad is oriented toward the camera.</summary>
public enum ParticleRenderMode
{
    /// <summary>Camera-facing quad rotated around the view axis by the particle's Z rotation.</summary>
    Billboard,

    /// <summary>Quad aligned along the particle's velocity direction and camera view vector.</summary>
    StretchedBillboard,

    /// <summary>Quad lying flat on the world XZ plane.</summary>
    HorizontalBillboard
}

/// <summary>Action taken when a non-looping <see cref="ParticleSystem"/> finishes and all particles expire.</summary>
public enum ParticleStopAction
{
    None,
    Disable,
    Destroy
}

/// <summary>Behaviour when calling <see cref="ParticleSystem.Stop(ParticleStopBehavior)"/>.</summary>
public enum ParticleStopBehavior
{
    /// <summary>Stops spawning new particles while allowing existing particles to finish their lifetime.</summary>
    StopEmitting,

    /// <summary>Stops spawning and immediately clears all live particles.</summary>
    StopEmittingAndClear
}

/// <summary>
/// A timed burst of particles fired at a specific elapsed time within an emission cycle (Unity's <c>ParticleSystem.Burst</c>).
/// </summary>
public record struct ParticleBurst(
    float Time,
    int MinCount,
    int MaxCount,
    int CycleCount = 1,
    float RepeatInterval = 0.01f,
    float Probability = 1f)
{
    public ParticleBurst(float time, int count)
        : this(time, count, count, 1, 0.01f, 1f)
    {
    }
}

/// <summary>One live particle's simulation state.</summary>
public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector4 StartColor;
    public float StartSize;
    public float Rotation;
    public float AngularVelocity;
    public float Age;
    public float Lifetime;

    /// <summary>Normalized age in <c>[0, 1]</c>.</summary>
    public readonly float NormalizedAge => Lifetime > 0f ? Math.Clamp(Age / Lifetime, 0f, 1f) : 1f;
}
