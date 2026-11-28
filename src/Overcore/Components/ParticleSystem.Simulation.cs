using Overcore.Attributes;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

public partial class ParticleSystem : Component, IDisposable
{
    /// <summary>
    /// Deterministically advances or scrubs the particle simulation by <paramref name="seconds"/> (Unity's <c>ParticleSystem.Simulate</c>).
    /// </summary>
    public void Simulate(float seconds, bool withChildren, bool restart, bool fixedTimeStep = true)
    {
        SimulateSingle(seconds, restart, fixedTimeStep);

        if (!withChildren || GameObject is null)
        {
            return;
        }

        foreach (Transform child in GameObject.Transform.Children)
        {
            foreach (ParticleSystem childPs in child.GameObject.GetComponentsInChildren<ParticleSystem>())
            {
                childPs.SimulateSingle(seconds, restart, fixedTimeStep);
            }
        }
    }

    /// <summary>
    /// Deterministically advances or scrubs this particle emitter by <paramref name="seconds"/>.
    /// </summary>
    public void Simulate(float seconds, bool restart = true) =>
        SimulateSingle(seconds, restart, fixedTimeStep: true);

    private void SimulateSingle(float seconds, bool restart, bool fixedTimeStep)
    {
        if (restart)
        {
            Clear();
            Reseed();
            _playbackTime = 0f;
            _cycleTime = 0f;
            _emissionAccumulator = 0f;
            _hasPreviousPosition = false;
            ResetBurstStates();
            IsPlaying = true;
            IsPaused = false;
            IsEmitting = true;
        }

        if (seconds <= 0f || !float.IsFinite(seconds))
        {
            return;
        }

        if (!fixedTimeStep)
        {
            StepInternal(seconds * SimulationSpeed);
            return;
        }

        const float maxSubStep = 1f / 60f;
        float remaining = seconds;
        while (remaining > 0f)
        {
            float dt = MathF.Min(remaining, maxSubStep);
            StepInternal(dt * SimulationSpeed);
            remaining -= dt;
        }
    }

    /// <summary>Advances the emitter by one frame step (called by <see cref="Scene.Update"/> and editor preview).</summary>
    public void Step(float deltaTime)
    {
        if (!Enabled || GameObject is null || !GameObject.ActiveInHierarchy)
        {
            return;
        }

        // Auto-start PlayOnAwake emitters even if added in editor or before Scene.Start.
        if (!IsPlaying && !IsPaused && PlayOnAwake && _playbackTime == 0f && _aliveCount == 0)
        {
            Play();
        }

        if (!IsPlaying || IsPaused)
        {
            return;
        }

        float rawDt = UseUnscaledTime ? Time.UnscaledDeltaTime : deltaTime;
        if (rawDt <= 0f || !float.IsFinite(rawDt))
        {
            return;
        }

        StepInternal(rawDt * SimulationSpeed);
    }

    private void StepInternal(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        Matrix4x4 emitterWorld = GameObject?.Transform.GetModelMatrix() ?? Matrix4x4.Identity;
        Vector3 worldPos = emitterWorld.Translation;

        float previousPlayback = _playbackTime;
        _playbackTime += dt;

        // 1. Advance and expire existing particles first (matches Unity's order so newborn particles start at Age = 0).
        IntegrateParticles(dt, emitterWorld);

        // 2. Spawn new particles if past StartDelay and still emitting.
        if (IsEmitting && _playbackTime >= StartDelay)
        {
            float activeDt = _playbackTime - MathF.Max(previousPlayback, StartDelay);
            if (activeDt > 0f)
            {
                AdvanceEmission(activeDt, emitterWorld, worldPos);
            }
        }

        _previousWorldPosition = worldPos;
        _hasPreviousPosition = true;

        // 3. Check stop action when non-looping emission has ended and all particles have expired.
        if (!IsEmitting && _aliveCount == 0)
        {
            IsPlaying = false;
            ApplyStopAction();
        }
    }

    private void AdvanceEmission(float activeDt, Matrix4x4 emitterWorld, Vector3 worldPos)
    {
        float cycleDuration = MathF.Max(0.01f, Duration);
        float prevCycleTime = _cycleTime;
        _cycleTime += activeDt;

        if (_cycleTime >= cycleDuration)
        {
            if (Looping)
            {
                // Fire any bursts scheduled near the end of the wrapping cycle before resetting.
                ProcessBursts(prevCycleTime, cycleDuration, emitterWorld);
                _cycleTime %= cycleDuration;
                ResetBurstStates();
                prevCycleTime = 0f;
            }
            else
            {
                ProcessBursts(prevCycleTime, cycleDuration, emitterWorld);
                _cycleTime = cycleDuration;
                IsEmitting = false;
                return;
            }
        }

        if (!EmissionEnabled)
        {
            return;
        }

        // Rate over time (1e-5f epsilon avoids losing a boundary particle to 60 Hz sub-step float rounding, e.g. 20 * 0.5 = 9.999999)
        if (RateOverTime > 0f)
        {
            _emissionAccumulator += RateOverTime * activeDt;
            int toEmit = (int)MathF.Floor(_emissionAccumulator + 1e-5f);
            if (toEmit > 0)
            {
                _emissionAccumulator = MathF.Max(0f, _emissionAccumulator - toEmit);
                EmitWithTransform(toEmit, emitterWorld);
            }
        }

        // Rate over distance
        if (RateOverDistance > 0f && _hasPreviousPosition)
        {
            float moved = Vector3.Distance(_previousWorldPosition, worldPos);
            if (moved > 0f)
            {
                _emissionAccumulator += RateOverDistance * moved;
                int toEmit = (int)MathF.Floor(_emissionAccumulator);
                if (toEmit > 0)
                {
                    _emissionAccumulator -= toEmit;
                    EmitWithTransform(toEmit, emitterWorld);
                }
            }
        }

        // Timed bursts
        ProcessBursts(prevCycleTime, _cycleTime, emitterWorld);
    }

    private void ProcessBursts(float fromTime, float toTime, Matrix4x4 emitterWorld)
    {
        if (!EmissionEnabled || _bursts.Count == 0)
        {
            return;
        }

        EnsureBurstStateCount();

        for (int i = 0; i < _bursts.Count; i++)
        {
            ParticleBurst burst = _bursts[i];
            BurstRuntimeState state = _burstStates[i];

            int maxCycles = burst.CycleCount <= 0 ? int.MaxValue : burst.CycleCount;
            float interval = MathF.Max(0.001f, burst.RepeatInterval);

            while (state.FiredCycles < maxCycles && state.NextFireTime <= toTime)
            {
                if (state.NextFireTime >= fromTime || (state.FiredCycles == 0 && fromTime == 0f && state.NextFireTime == 0f))
                {
                    if (burst.Probability >= 1f || NextFloat01() <= burst.Probability)
                    {
                        int min = Math.Max(0, burst.MinCount);
                        int max = Math.Max(min, burst.MaxCount);
                        int count = min == max ? min : min + (int)MathF.Floor(NextFloat01() * (max - min + 1));
                        EmitWithTransform(count, emitterWorld);
                    }
                }

                state.FiredCycles++;
                state.NextFireTime += interval;
            }

            _burstStates[i] = state;
        }
    }

    private void IntegrateParticles(float dt, Matrix4x4 emitterWorld)
    {
        Vector3 gravity = new Vector3(0f, -9.81f, 0f) * GravityModifier;

        Vector3 extraVelocity = Vector3.Zero;
        if (VelocityOverLifetimeEnabled && LinearVelocity != Vector3.Zero)
        {
            extraVelocity = VelocitySpace == ParticleSimulationSpace.Local && SimulationSpace == ParticleSimulationSpace.World
                ? Vector3.TransformNormal(LinearVelocity, emitterWorld)
                : LinearVelocity;
        }

        Vector3 extraForce = Vector3.Zero;
        if (ForceOverLifetimeEnabled && Force != Vector3.Zero)
        {
            extraForce = ForceSpace == ParticleSimulationSpace.Local && SimulationSpace == ParticleSimulationSpace.World
                ? Vector3.TransformNormal(Force, emitterWorld)
                : Force;
        }

        float dragFactor = VelocityOverLifetimeEnabled && Drag > 0f
            ? MathF.Exp(-Drag * dt)
            : 1f;

        int i = 0;
        while (i < _aliveCount)
        {
            ref Particle p = ref _particles[i];
            p.Age += dt;

            if (p.Age >= p.Lifetime)
            {
                // Swap-remove expired particle in O(1).
                _aliveCount--;
                if (i < _aliveCount)
                {
                    _particles[i] = _particles[_aliveCount];
                }

                continue;
            }

            p.Velocity += (gravity + extraForce) * dt;
            if (dragFactor < 1f)
            {
                p.Velocity *= dragFactor;
            }

            p.Position += (p.Velocity + extraVelocity) * dt;
            p.Rotation += p.AngularVelocity * dt;
            i++;
        }
    }

    private void EmitWithTransform(int count, Matrix4x4 emitterWorld)
    {
        for (int i = 0; i < count; i++)
        {
            if (_aliveCount >= _maxParticles)
            {
                break;
            }

            SpawnOne(emitterWorld);
        }
    }

    private void SpawnOne(Matrix4x4 emitterWorld)
    {
        EnsureCapacity(_aliveCount + 1);

        (Vector3 localPos, Vector3 localDir) = SampleShape();

        if (RandomizeDirection > 0f)
        {
            Vector3 randomDir = SampleUnitSphereSurface();
            localDir = Vector3.Normalize(Vector3.Lerp(localDir, randomDir, Math.Clamp(RandomizeDirection, 0f, 1f)));
            if (!float.IsFinite(localDir.X))
            {
                localDir = Vector3.UnitY;
            }
        }

        float speed = SampleRange(StartSpeed, StartSpeedMax);
        float lifetime = MathF.Max(0.01f, SampleRange(StartLifetime, StartLifetimeMax));
        float size = MathF.Max(0.0001f, SampleRange(StartSize, StartSizeMax));
        float startRotRad = SampleRange(StartRotation, StartRotationMax) * (MathF.PI / 180f);
        float angularVelRad = RotationOverLifetimeEnabled ? AngularVelocity * (MathF.PI / 180f) : 0f;

        Vector3 position;
        Vector3 velocity;

        if (SimulationSpace == ParticleSimulationSpace.World)
        {
            position = Vector3.Transform(localPos, emitterWorld);
            Vector3 worldDir = Vector3.TransformNormal(localDir, emitterWorld);
            float dirLen = worldDir.Length();
            worldDir = dirLen > 1e-6f ? worldDir / dirLen : Vector3.UnitY;
            velocity = worldDir * speed;
        }
        else
        {
            position = localPos;
            velocity = localDir * speed;
        }

        _particles[_aliveCount++] = new Particle
        {
            Position = position,
            Velocity = velocity,
            StartColor = new Vector4(StartColor.X, StartColor.Y, StartColor.Z, Math.Clamp(StartAlpha, 0f, 1f)),
            StartSize = size,
            Rotation = startRotRad,
            AngularVelocity = angularVelRad,
            Age = 0f,
            Lifetime = lifetime
        };
    }

}
