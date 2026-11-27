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

/// <summary>
/// Shuriken-style 3D particle emitter, deterministic CPU simulator, and renderer settings — Unity's <c>ParticleSystem</c>.
/// </summary>
/// <remarks>
/// <para>
/// Properties are exposed directly on the component for clean YAML serialization, undo/redo, and inspector editing,
/// while lightweight module accessors (<see cref="Main"/>, <see cref="Emission"/>, <see cref="Shape"/>,
/// <see cref="VelocityOverLifetime"/>, <see cref="ForceOverLifetime"/>, <see cref="SizeOverLifetime"/>,
/// <see cref="ColorOverLifetime"/>, <see cref="RotationOverLifetime"/>, <see cref="RendererModule"/>) provide an
/// ergonomic Unity-like scripting surface.
/// </para>
/// <para>
/// Simulation uses a deterministic XorShift32 PRNG seeded by <see cref="RandomSeed"/> (when <see cref="AutoRandomSeed"/>
/// is false), making <see cref="Simulate(float, bool, bool)"/> bit-for-bit reproducible in tests and scrubbing.
/// </para>
/// </remarks>
public class ParticleSystem : Component, IDisposable
{
    private Particle[] _particles = new Particle[64];
    private int _aliveCount;
    private float _playbackTime;
    private float _cycleTime;
    private float _emissionAccumulator;
    private Vector3 _previousWorldPosition;
    private bool _hasPreviousPosition;
    private uint _rngState = 0x9E3779B9u;
    private readonly List<ParticleBurst> _bursts = [];
    private readonly List<BurstRuntimeState> _burstStates = [];

    private struct BurstRuntimeState
    {
        public int FiredCycles;
        public float NextFireTime;
    }

    // ---------------------------------------------------------------- Main Module

    /// <summary>Length of one emission cycle in seconds.</summary>
    [Range(0.01f, 600f, 0.1f)]
    public float Duration { get; set; } = 5f;

    /// <summary>Whether the emission cycle repeats once <see cref="Duration"/> elapses.</summary>
    public bool Looping { get; set; } = true;

    /// <summary>Pre-populates the system by simulating one full cycle when playback begins.</summary>
    public bool Prewarm { get; set; } = false;

    /// <summary>Delay in seconds before emission starts after <see cref="Play"/>.</summary>
    [Range(0f, 60f, 0.05f)]
    public float StartDelay { get; set; } = 0f;

    /// <summary>Minimum initial lifetime in seconds.</summary>
    [Range(0.01f, 120f, 0.05f)]
    public float StartLifetime { get; set; } = 3f;

    /// <summary>Maximum initial lifetime in seconds (interpolated randomly with <see cref="StartLifetime"/>).</summary>
    [Range(0.01f, 120f, 0.05f)]
    public float StartLifetimeMax { get; set; } = 3f;

    /// <summary>Minimum initial speed along the shape's emission direction.</summary>
    public float StartSpeed { get; set; } = 5f;

    /// <summary>Maximum initial speed along the shape's emission direction.</summary>
    public float StartSpeedMax { get; set; } = 5f;

    /// <summary>Minimum initial quad size in world units.</summary>
    [Range(0.001f, 100f, 0.01f)]
    public float StartSize { get; set; } = 0.5f;

    /// <summary>Maximum initial quad size in world units.</summary>
    [Range(0.001f, 100f, 0.01f)]
    public float StartSizeMax { get; set; } = 0.5f;

    /// <summary>Initial roll rotation in degrees.</summary>
    public float StartRotation { get; set; } = 0f;

    /// <summary>Maximum initial roll rotation in degrees.</summary>
    public float StartRotationMax { get; set; } = 0f;

    /// <summary>Initial RGB tint (supports HDR values &gt; 1 for bloom).</summary>
    [ColorUsage]
    public Vector3 StartColor { get; set; } = Vector3.One;

    /// <summary>Initial alpha opacity in <c>[0, 1]</c>.</summary>
    [Range(0f, 1f, 0.01f)]
    public float StartAlpha { get; set; } = 1f;

    /// <summary>Multiplier applied to scene gravity (<c>(0, -9.81, 0)</c> when no physics gravity is active).</summary>
    public float GravityModifier { get; set; } = 0f;

    /// <summary>Space in which particles live once spawned.</summary>
    public ParticleSimulationSpace SimulationSpace { get; set; } = ParticleSimulationSpace.World;

    /// <summary>Time multiplier applied to simulation steps.</summary>
    [Range(0f, 10f, 0.05f)]
    public float SimulationSpeed { get; set; } = 1f;

    /// <summary>Whether simulation uses <see cref="Overcore.Time.UnscaledDeltaTime"/>.</summary>
    public bool UseUnscaledTime { get; set; } = false;

    /// <summary>Whether playback starts automatically when the scene starts (and previews in the editor).</summary>
    public bool PlayOnAwake { get; set; } = true;

    /// <summary>Upper cap on live particles at any instant.</summary>
    [Range(1f, 100000f, 1f)]
    public int MaxParticles
    {
        get => _maxParticles;
        set => _maxParticles = Math.Max(1, value);
    }

    private int _maxParticles = 1000;

    /// <summary>When true, picks a non-deterministic seed on <see cref="Play"/>; when false, uses <see cref="RandomSeed"/>.</summary>
    public bool AutoRandomSeed { get; set; } = false;

    /// <summary>Deterministic PRNG seed used when <see cref="AutoRandomSeed"/> is false.</summary>
    public int RandomSeed { get; set; } = 1337;

    /// <summary>Action performed when a non-looping system completes and its last particle dies.</summary>
    public ParticleStopAction StopAction { get; set; } = ParticleStopAction.None;

    // ---------------------------------------------------------------- Emission Module

    /// <summary>Enables continuous/burst particle emission.</summary>
    public bool EmissionEnabled { get; set; } = true;

    /// <summary>Particles spawned per second.</summary>
    [Range(0f, 10000f, 1f)]
    public float RateOverTime { get; set; } = 10f;

    /// <summary>Particles spawned per unit of world-space emitter travel.</summary>
    [Range(0f, 1000f, 0.1f)]
    public float RateOverDistance { get; set; } = 0f;

    // ---------------------------------------------------------------- Shape Module

    /// <summary>Enables shape volume sampling; when disabled, particles spawn at the origin moving along +Z.</summary>
    public bool ShapeEnabled { get; set; } = true;

    /// <summary>Volume shape from which particles spawn.</summary>
    public ParticleShapeType ShapeType { get; set; } = ParticleShapeType.Cone;

    /// <summary>Radius of Cone, Sphere, Hemisphere, or Circle shapes.</summary>
    [Range(0.001f, 500f, 0.05f)]
    public float ShapeRadius { get; set; } = 0.5f;

    /// <summary>Half-angle of a Cone emitter in degrees (<c>0</c> = cylinder beam, <c>90</c> = flat disc).</summary>
    [Range(0f, 90f, 0.5f)]
    public float ConeAngle { get; set; } = 25f;

    /// <summary>Extents (size) of a Box emitter.</summary>
    public Vector3 BoxScale { get; set; } = Vector3.One;

    /// <summary>0 emits throughout the volume; 1 emits only from the outer shell/rim.</summary>
    [Range(0f, 1f, 0.01f)]
    public float RadiusThickness { get; set; } = 0f;

    /// <summary>Blends emission direction toward a random unit vector (<c>0..1</c>).</summary>
    [Range(0f, 1f, 0.01f)]
    public float RandomizeDirection { get; set; } = 0f;

    // ---------------------------------------------------------------- Velocity & Force Over Lifetime

    /// <summary>Enables constant linear velocity offset and drag over particle lifetime.</summary>
    public bool VelocityOverLifetimeEnabled { get; set; } = false;

    /// <summary>Additional linear velocity applied each second.</summary>
    public Vector3 LinearVelocity { get; set; } = Vector3.Zero;

    /// <summary>Space in which <see cref="LinearVelocity"/> is interpreted.</summary>
    public ParticleSimulationSpace VelocitySpace { get; set; } = ParticleSimulationSpace.Local;

    /// <summary>Exponential drag coefficient (<c>0</c> = no drag).</summary>
    [Range(0f, 50f, 0.05f)]
    public float Drag { get; set; } = 0f;

    /// <summary>Enables continuous acceleration force over lifetime.</summary>
    public bool ForceOverLifetimeEnabled { get; set; } = false;

    /// <summary>Continuous acceleration force in world or local space.</summary>
    public Vector3 Force { get; set; } = Vector3.Zero;

    /// <summary>Space in which <see cref="Force"/> is interpreted.</summary>
    public ParticleSimulationSpace ForceSpace { get; set; } = ParticleSimulationSpace.World;

    // ---------------------------------------------------------------- Size, Color & Rotation Over Lifetime

    /// <summary>Enables size interpolation over normalized lifetime.</summary>
    public bool SizeOverLifetimeEnabled { get; set; } = false;

    /// <summary>Size multiplier at birth (<c>t = 0</c>).</summary>
    [Range(0f, 10f, 0.01f)]
    public float SizeMultiplierStart { get; set; } = 1f;

    /// <summary>Size multiplier at death (<c>t = 1</c>).</summary>
    [Range(0f, 10f, 0.01f)]
    public float SizeMultiplierEnd { get; set; } = 0f;

    /// <summary>Optional custom curve overriding linear start/end size multipliers when non-null.</summary>
    [HideInInspector]
    public Animation.AnimationCurve? SizeCurve { get; set; }

    /// <summary>Enables colour and alpha tinting over normalized lifetime.</summary>
    public bool ColorOverLifetimeEnabled { get; set; } = false;

    /// <summary>Colour multiplier at birth (<c>t = 0</c>).</summary>
    [ColorUsage]
    public Vector3 EndColor { get; set; } = Vector3.One;

    /// <summary>Alpha multiplier at death (<c>t = 1</c>).</summary>
    [Range(0f, 1f, 0.01f)]
    public float EndAlpha { get; set; } = 0f;

    /// <summary>Enables angular velocity (degrees/second) over lifetime.</summary>
    public bool RotationOverLifetimeEnabled { get; set; } = false;

    /// <summary>Roll angular velocity in degrees per second.</summary>
    public float AngularVelocity { get; set; } = 45f;

    // ---------------------------------------------------------------- Renderer Module

    /// <summary>Quad orientation mode.</summary>
    public ParticleRenderMode RenderMode { get; set; } = ParticleRenderMode.Billboard;

    /// <summary>Blend mode used in the particle pass.</summary>
    public ParticleBlendMode BlendMode { get; set; } = ParticleBlendMode.Alpha;

    /// <summary>Optional sprite/texture asset name; empty uses a procedural soft radial glow.</summary>
    [DragDropTarget("ASSET_PATH_TEXTURE")]
    public string TextureName { get; set; } = string.Empty;

    /// <summary>Loaded particle texture, or null to use the procedural soft particle disc.</summary>
    [HideInInspector]
    public ITexture? Texture { get; set; }

    /// <summary>Velocity stretch factor for <see cref="ParticleRenderMode.StretchedBillboard"/>.</summary>
    public float VelocityScale { get; set; } = 0.1f;

    /// <summary>Base length multiplier along velocity for <see cref="ParticleRenderMode.StretchedBillboard"/>.</summary>
    public float LengthScale { get; set; } = 2f;

    /// <summary>Back-to-front depth sorting toggle for alpha-blended particles.</summary>
    public bool SortByDepth { get; set; } = true;

    /// <summary>Backend GPU buffer cache owned by the active renderer.</summary>
    [HideInInspector]
    public object? BackendData { get; set; }

    // ---------------------------------------------------------------- Runtime State & Module Views

    [HideInInspector]
    public bool IsPlaying { get; private set; }

    [HideInInspector]
    public bool IsPaused { get; private set; }

    [HideInInspector]
    public bool IsStopped => !IsPlaying && !IsPaused;

    [HideInInspector]
    public bool IsEmitting { get; private set; }

    [HideInInspector]
    public int ParticleCount => _aliveCount;

    [HideInInspector]
    public float PlaybackTime => _playbackTime;

    /// <summary>Read-only span over the currently alive particles (<c>0 .. ParticleCount - 1</c>).</summary>
    public ReadOnlySpan<Particle> Particles => _particles.AsSpan(0, _aliveCount);

    /// <summary>Configured timed bursts on the emission module.</summary>
    [HideInInspector]
    public IReadOnlyList<ParticleBurst> Bursts => _bursts;

    [HideInInspector]
    public MainModuleView Main => new(this);

    [HideInInspector]
    public EmissionModuleView Emission => new(this);

    [HideInInspector]
    public ShapeModuleView Shape => new(this);

    [HideInInspector]
    public VelocityModuleView VelocityOverLifetime => new(this);

    [HideInInspector]
    public ForceModuleView ForceOverLifetime => new(this);

    [HideInInspector]
    public SizeModuleView SizeOverLifetime => new(this);

    [HideInInspector]
    public ColorModuleView ColorOverLifetime => new(this);

    [HideInInspector]
    public RotationModuleView RotationOverLifetime => new(this);

    [HideInInspector]
    public RendererModuleView RendererModule => new(this);

    public readonly struct MainModuleView(ParticleSystem owner)
    {
        public float Duration { get => owner.Duration; set => owner.Duration = value; }
        public bool Looping { get => owner.Looping; set => owner.Looping = value; }
        public bool Prewarm { get => owner.Prewarm; set => owner.Prewarm = value; }
        public float StartLifetime { get => owner.StartLifetime; set { owner.StartLifetime = value; owner.StartLifetimeMax = value; } }
        public float StartSpeed { get => owner.StartSpeed; set { owner.StartSpeed = value; owner.StartSpeedMax = value; } }
        public float StartSize { get => owner.StartSize; set { owner.StartSize = value; owner.StartSizeMax = value; } }
        public Vector3 StartColor { get => owner.StartColor; set => owner.StartColor = value; }
        public float GravityModifier { get => owner.GravityModifier; set => owner.GravityModifier = value; }
        public ParticleSimulationSpace SimulationSpace { get => owner.SimulationSpace; set => owner.SimulationSpace = value; }
        public int MaxParticles { get => owner.MaxParticles; set => owner.MaxParticles = value; }
    }

    public readonly struct EmissionModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.EmissionEnabled; set => owner.EmissionEnabled = value; }
        public float RateOverTime { get => owner.RateOverTime; set => owner.RateOverTime = value; }
        public float RateOverDistance { get => owner.RateOverDistance; set => owner.RateOverDistance = value; }
        public IReadOnlyList<ParticleBurst> Bursts => owner.Bursts;
        public void SetBursts(IEnumerable<ParticleBurst> bursts) => owner.SetBursts(bursts);
        public void AddBurst(ParticleBurst burst) => owner.AddBurst(burst);
        public void ClearBursts() => owner.ClearBursts();
    }

    public readonly struct ShapeModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.ShapeEnabled; set => owner.ShapeEnabled = value; }
        public ParticleShapeType ShapeType { get => owner.ShapeType; set => owner.ShapeType = value; }
        public float Radius { get => owner.ShapeRadius; set => owner.ShapeRadius = value; }
        public float Angle { get => owner.ConeAngle; set => owner.ConeAngle = value; }
        public Vector3 Scale { get => owner.BoxScale; set => owner.BoxScale = value; }
    }

    public readonly struct VelocityModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.VelocityOverLifetimeEnabled; set => owner.VelocityOverLifetimeEnabled = value; }
        public Vector3 Linear { get => owner.LinearVelocity; set => owner.LinearVelocity = value; }
        public float Drag { get => owner.Drag; set => owner.Drag = value; }
    }

    public readonly struct ForceModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.ForceOverLifetimeEnabled; set => owner.ForceOverLifetimeEnabled = value; }
        public Vector3 Force { get => owner.Force; set => owner.Force = value; }
    }

    public readonly struct SizeModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.SizeOverLifetimeEnabled; set => owner.SizeOverLifetimeEnabled = value; }
        public float StartMultiplier { get => owner.SizeMultiplierStart; set => owner.SizeMultiplierStart = value; }
        public float EndMultiplier { get => owner.SizeMultiplierEnd; set => owner.SizeMultiplierEnd = value; }
        public Animation.AnimationCurve? Curve { get => owner.SizeCurve; set => owner.SizeCurve = value; }
    }

    public readonly struct ColorModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.ColorOverLifetimeEnabled; set => owner.ColorOverLifetimeEnabled = value; }
        public Vector3 EndColor { get => owner.EndColor; set => owner.EndColor = value; }
        public float EndAlpha { get => owner.EndAlpha; set => owner.EndAlpha = value; }
    }

    public readonly struct RotationModuleView(ParticleSystem owner)
    {
        public bool Enabled { get => owner.RotationOverLifetimeEnabled; set => owner.RotationOverLifetimeEnabled = value; }
        public float AngularVelocity { get => owner.AngularVelocity; set => owner.AngularVelocity = value; }
    }

    public readonly struct RendererModuleView(ParticleSystem owner)
    {
        public ParticleRenderMode RenderMode { get => owner.RenderMode; set => owner.RenderMode = value; }
        public ParticleBlendMode BlendMode { get => owner.BlendMode; set => owner.BlendMode = value; }
        public string TextureName { get => owner.TextureName; set => owner.TextureName = value; }
        public ITexture? Texture { get => owner.Texture; set => owner.Texture = value; }
    }

    // ---------------------------------------------------------------- Burst Management

    public void SetBursts(IEnumerable<ParticleBurst> bursts)
    {
        ArgumentNullException.ThrowIfNull(bursts);
        _bursts.Clear();
        _bursts.AddRange(bursts);
        ResetBurstStates();
    }

    public void AddBurst(ParticleBurst burst)
    {
        _bursts.Add(burst);
        _burstStates.Add(new BurstRuntimeState { FiredCycles = 0, NextFireTime = MathF.Max(0f, burst.Time) });
    }

    public void ClearBursts()
    {
        _bursts.Clear();
        _burstStates.Clear();
    }

    // ---------------------------------------------------------------- Lifecycle & Controls

    public override void Awake()
    {
        if (PlayOnAwake && Enabled)
        {
            Play();
        }
    }

    /// <summary>Starts or resumes particle emission and simulation.</summary>
    public void Play()
    {
        if (IsPaused)
        {
            IsPaused = false;
            IsPlaying = true;
            return;
        }

        if (IsPlaying)
        {
            return;
        }

        Reseed();
        _playbackTime = 0f;
        _cycleTime = 0f;
        _emissionAccumulator = 0f;
        _hasPreviousPosition = false;
        ResetBurstStates();

        IsPlaying = true;
        IsPaused = false;
        IsEmitting = true;

        if (Prewarm && Looping && Duration > 0.001f)
        {
            const int prewarmSteps = 20;
            float dt = Duration / prewarmSteps;
            for (int i = 0; i < prewarmSteps; i++)
            {
                StepInternal(dt);
            }
        }
    }

    /// <summary>Pauses emission and simulation, keeping all live particles frozen in place.</summary>
    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        IsPaused = true;
    }

    /// <summary>Stops emission, optionally clearing live particles immediately.</summary>
    public void Stop(ParticleStopBehavior behavior = ParticleStopBehavior.StopEmitting)
    {
        IsEmitting = false;
        IsPaused = false;

        if (behavior == ParticleStopBehavior.StopEmittingAndClear)
        {
            IsPlaying = false;
            Clear();
        }
    }

    /// <summary>Removes all live particles immediately.</summary>
    public void Clear()
    {
        _aliveCount = 0;
        _emissionAccumulator = 0f;
    }

    /// <summary>Immediately spawns <paramref name="count"/> particles using the configured shape and start modules.</summary>
    public void Emit(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Matrix4x4 emitterWorld = GameObject?.Transform.GetModelMatrix() ?? Matrix4x4.Identity;
        for (int i = 0; i < count; i++)
        {
            if (_aliveCount >= _maxParticles)
            {
                break;
            }

            SpawnOne(emitterWorld);
        }
    }

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

        // Rate over time
        if (RateOverTime > 0f)
        {
            _emissionAccumulator += RateOverTime * activeDt;
            int toEmit = (int)MathF.Floor(_emissionAccumulator);
            if (toEmit > 0)
            {
                _emissionAccumulator -= toEmit;
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

    public override void OnDestroy()
    {
        Dispose();
    }

    public void Dispose()
    {
        (BackendData as IDisposable)?.Dispose();
        BackendData = null;
    }
}
