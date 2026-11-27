# Particle System

`ParticleSystem` (`Overcore.Components`) is Overcore's Shuriken-style modular 3D particle emitter and deterministic CPU simulator, paired with an instanced OpenGL 3.3+ billboard renderer in the HDR MSAA pipeline.

## Modules & Properties

- **Main**: `Duration`, `Looping`, `Prewarm`, `PlayOnAwake`, `StartDelay`, `StartLifetime` / `StartLifetimeMax`, `StartSpeed` / `StartSpeedMax`, `StartSize` / `StartSizeMax`, `StartRotation` / `StartRotationMax`, `StartColor`, `StartAlpha`, `GravityModifier`, `SimulationSpace` (`Local` or `World`), `SimulationSpeed`, `MaxParticles`, `StopAction` (`None`, `Disable`, `Destroy`), `AutoRandomSeed`, `RandomSeed`.
- **Emission**: `EmissionEnabled`, `RateOverTime`, `RateOverDistance`, and timed `Bursts` (`ParticleBurst(Time, MinCount, MaxCount, CycleCount, RepeatInterval, Probability)` via `SetBursts`/`AddBurst`/`ClearBursts`).
- **Shape**: `ShapeEnabled`, `ShapeType` (`Cone`, `Sphere`, `Hemisphere`, `Box`, `Circle`), `ShapeRadius`, `ShapeRadiusThickness` (`0` = shell, `1` = volume), `ConeAngle`, `BoxScale`.
- **Velocity & Force Over Lifetime**: `VelocityOverLifetimeEnabled`, `LinearVelocity`, `Drag`, `ForceOverLifetimeEnabled`, `Force`.
- **Size, Color & Rotation Over Lifetime**: `SizeOverLifetimeEnabled`, `SizeMultiplierStart`, `SizeMultiplierEnd`, optional `SizeCurve` (`AnimationCurve`), `ColorOverLifetimeEnabled`, `EndColor`, `EndAlpha`, `RotationOverLifetimeEnabled`, `AngularVelocity`.
- **Renderer**: `RenderMode` (`Billboard`, `StretchedBillboard`, `HorizontalBillboard`), `VelocityScale`, `LengthScale`, `BlendMode` (`Alpha` with optional back-to-front depth sort, or `Additive` glow), `TextureName` / `Texture` (procedural soft radial glow when untextured).

## Scripting Example

```csharp
using System.Numerics;
using Overcore.Components;
using Overcore.Core;

public class HitSparkBurst : Script
{
    public override void Start()
    {
        if (GameObject is null)
        {
            return;
        }

        ParticleSystem ps = GameObject.GetComponent<ParticleSystem>()
            ?? GameObject.AddComponent<ParticleSystem>();

        ps.Looping = false;
        ps.Duration = 0.8f;
        ps.RateOverTime = 0f;
        ps.BlendMode = ParticleBlendMode.Additive;
        ps.RenderMode = ParticleRenderMode.StretchedBillboard;
        ps.StartColor = new Vector3(1.0f, 0.65f, 0.2f);
        ps.GravityModifier = 1.2f;
        ps.StopAction = ParticleStopAction.Destroy;

        ps.SetBursts([new ParticleBurst(0f, MinCount: 24, MaxCount: 36)]);
        ps.Play();
    }
}
```

## Playback & Determinism

- `Play()`, `Pause()`, `Stop(ParticleStopBehavior)`, `Clear()`, `Emit(count)`, and `Simulate(time, withChildren, restart)` mirror Unity's `ParticleSystem` control surface.
- Setting `AutoRandomSeed = false` with a fixed `RandomSeed` drives an internal `XorShift32` generator so `Simulate(..., restart: true)` reproduces identical trajectories across frames and tests.
- Selecting a `GameObject` with a `ParticleSystem` draws its emission shape gizmo in the viewport and exposes **Play / Pause / Restart / Clear** controls in the Inspector.
