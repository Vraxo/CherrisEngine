using Overcore.Attributes;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

public partial class ParticleSystem : Component, IDisposable
{
    private (Vector3 Position, Vector3 Direction) SampleShape()
    {
        if (!ShapeEnabled)
        {
            return (Vector3.Zero, Vector3.UnitY);
        }

        float radius = MathF.Max(0.0001f, ShapeRadius);
        float minRadiusFraction = Math.Clamp(RadiusThickness, 0f, 1f);

        switch (ShapeType)
        {
            case ParticleShapeType.Sphere:
            {
                Vector3 dir = SampleUnitSphereSurface();
                float r = radius * MathF.Cbrt(MathF.Max(minRadiusFraction * minRadiusFraction * minRadiusFraction, NextFloat01()));
                return (dir * r, dir);
            }

            case ParticleShapeType.Hemisphere:
            {
                Vector3 dir = SampleUnitSphereSurface();
                if (dir.Y < 0f)
                {
                    dir.Y = -dir.Y;
                }

                float r = radius * MathF.Cbrt(MathF.Max(minRadiusFraction * minRadiusFraction * minRadiusFraction, NextFloat01()));
                return (dir * r, dir);
            }

            case ParticleShapeType.Box:
            {
                Vector3 half = BoxScale * 0.5f;
                Vector3 pos = new(
                    SampleRange(-half.X, half.X),
                    SampleRange(-half.Y, half.Y),
                    SampleRange(-half.Z, half.Z));
                return (pos, Vector3.UnitY);
            }

            case ParticleShapeType.Circle:
            {
                float angle = NextFloat01() * MathF.Tau;
                float r = radius * MathF.Sqrt(float.Lerp(minRadiusFraction * minRadiusFraction, 1f, NextFloat01()));
                Vector3 radial = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
                return (radial * r, radial.LengthSquared() > 1e-6f ? Vector3.Normalize(radial) : Vector3.UnitX);
            }

            case ParticleShapeType.Cone:
            default:
            {
                // Base disc lies on XZ, cone emits upward along +Y (matching default scene up so a newly added
                // ParticleSystem fountains upward immediately without rotating its Transform).
                float theta = NextFloat01() * MathF.Tau;
                float u = MathF.Sqrt(float.Lerp(minRadiusFraction * minRadiusFraction, 1f, NextFloat01()));
                Vector2 disc = new(MathF.Cos(theta) * u, MathF.Sin(theta) * u);
                Vector3 basePos = new(disc.X * radius, 0f, disc.Y * radius);

                float coneRad = Math.Clamp(ConeAngle, 0f, 89.9f) * (MathF.PI / 180f);
                float tanAngle = MathF.Tan(coneRad);
                Vector3 dir = Vector3.Normalize(new Vector3(disc.X * tanAngle, 1f, disc.Y * tanAngle));
                return (basePos, dir);
            }
        }
    }

    /// <summary>Evaluates a live particle's current world-space size.</summary>
    public float EvaluateParticleSize(in Particle particle)
    {
        float size = particle.StartSize;
        if (!SizeOverLifetimeEnabled)
        {
            return size;
        }

        float t = particle.NormalizedAge;
        float multiplier = SizeCurve is not null
            ? MathF.Max(0f, SizeCurve.Evaluate(t))
            : float.Lerp(SizeMultiplierStart, SizeMultiplierEnd, t);

        return MathF.Max(0f, size * multiplier);
    }

    /// <summary>Evaluates a live particle's current RGBA colour (with HDR RGB preserved).</summary>
    public Vector4 EvaluateParticleColor(in Particle particle)
    {
        Vector4 start = particle.StartColor;
        if (!ColorOverLifetimeEnabled)
        {
            return start;
        }

        float t = particle.NormalizedAge;
        Vector4 end = new(EndColor.X, EndColor.Y, EndColor.Z, Math.Clamp(EndAlpha, 0f, 1f));
        return Vector4.Lerp(start, end, t);
    }

    /// <summary>Computes a live particle's world-space position based on <see cref="SimulationSpace"/>.</summary>
    public Vector3 GetParticleWorldPosition(in Particle particle)
    {
        if (SimulationSpace == ParticleSimulationSpace.World || GameObject is null)
        {
            return particle.Position;
        }

        return Vector3.Transform(particle.Position, GameObject.Transform.GetModelMatrix());
    }

    private void ApplyStopAction()
    {
        if (GameObject is null)
        {
            return;
        }

        switch (StopAction)
        {
            case ParticleStopAction.Disable:
                GameObject.SetActive(false);
                break;

            case ParticleStopAction.Destroy:
                GameObject.Destroy(GameObject);
                break;
        }
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _particles.Length)
        {
            return;
        }

        int nextSize = _particles.Length;
        while (nextSize < needed)
        {
            nextSize *= 2;
        }

        Array.Resize(ref _particles, Math.Min(nextSize, Math.Max(needed, _maxParticles)));
    }

    private void Reseed()
    {
        uint seed = AutoRandomSeed
            ? (uint)Environment.TickCount ^ (uint)GetHashCode()
            : (uint)RandomSeed;

        // XorShift32 must never hold 0.
        _rngState = seed == 0u ? 0x6D2B79F5u : seed;
    }

    private void ResetBurstStates()
    {
        _burstStates.Clear();
        foreach (ParticleBurst burst in _bursts)
        {
            _burstStates.Add(new BurstRuntimeState
            {
                FiredCycles = 0,
                NextFireTime = MathF.Max(0f, burst.Time)
            });
        }
    }

    private void EnsureBurstStateCount()
    {
        while (_burstStates.Count < _bursts.Count)
        {
            int idx = _burstStates.Count;
            _burstStates.Add(new BurstRuntimeState
            {
                FiredCycles = 0,
                NextFireTime = MathF.Max(0f, _bursts[idx].Time)
            });
        }
    }

    private uint NextUInt()
    {
        uint x = _rngState == 0u ? 0x9E3779B9u : _rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rngState = x;
        return x;
    }

    private float NextFloat01() => (NextUInt() & 0x00FFFFFFu) / (float)0x01000000u;

    private float SampleRange(float min, float max) =>
        MathF.Abs(max - min) <= 1e-6f ? min : min + ((max - min) * NextFloat01());

    private Vector3 SampleUnitSphereSurface()
    {
        float z = (NextFloat01() * 2f) - 1f;
        float phi = NextFloat01() * MathF.Tau;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
        return new Vector3(r * MathF.Cos(phi), z, r * MathF.Sin(phi));
    }

    internal override void OnInstantiated()
    {
        base.OnInstantiated();

        // Give the clone its own particle array, burst state lists, and GPU buffer handle so it never shares
        // live particles or frees the original emitter's VBO.
        _particles = new Particle[Math.Max(64, _particles.Length)];
        _aliveCount = 0;
        _playbackTime = 0f;
        _cycleTime = 0f;
        _emissionAccumulator = 0f;
        _hasPreviousPosition = false;
        IsPlaying = false;
        IsPaused = false;
        IsEmitting = false;
        BackendData = null;
    }
}
