using Overcore.Attributes;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

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
public partial class ParticleSystem : Component, IDisposable
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
