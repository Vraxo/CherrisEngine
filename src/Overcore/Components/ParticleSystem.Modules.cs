using Overcore.Attributes;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

public partial class ParticleSystem : Component, IDisposable
{
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
}
