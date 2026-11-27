using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Particles;

/// <summary>
/// Covers the Shuriken-style <see cref="ParticleSystem"/> component: deterministic PRNG simulation, rate &amp; burst
/// emission, lifetime curves (size, color, force/gravity, drag, rotation), local vs world simulation space, stop
/// actions, and YAML scene round-tripping.
/// </summary>
public class ParticleSystemTests
{
    [Fact]
    public void RateOverTime_SpawnsExpectedParticleCount_AndExpiresCleanly()
    {
        GameObject emitter = new("Sparks");
        ParticleSystem ps = emitter.AddComponent<ParticleSystem>();
        ps.Duration = 2.0f;
        ps.Looping = false;
        ps.StartLifetime = 1.0f;
        ps.StartLifetimeMax = 1.0f;
        ps.RateOverTime = 20f;
        ps.Play();

        ps.Simulate(0.5f, withChildren: false, restart: false);
        ps.ParticleCount.Should().Be(10);
        ps.IsEmitting.Should().BeTrue();

        // Advance past Duration + StartLifetime: emission stops at 2.0s and all particles expire by 3.0s.
        for (int i = 0; i < 30; i++)
        {
            ps.Simulate(0.1f, withChildren: false, restart: false);
        }

        ps.ParticleCount.Should().Be(0);
        ps.IsEmitting.Should().BeFalse();
        ps.IsStopped.Should().BeTrue();
    }

    [Fact]
    public void Bursts_FireAtConfiguredTimesAndCycles()
    {
        GameObject explosion = new("Explosion");
        ParticleSystem ps = explosion.AddComponent<ParticleSystem>();
        ps.RateOverTime = 0f;
        ps.Looping = false;
        ps.Duration = 2.0f;
        ps.StartLifetime = 5.0f;
        ps.StartLifetimeMax = 5.0f;
        ps.SetBursts([
            new ParticleBurst(0.0f, 15),
            new ParticleBurst(0.5f, 8, 8, CycleCount: 2, RepeatInterval: 0.25f)
        ]);

        ps.Play();
        ps.Simulate(0.1f, withChildren: false, restart: false);
        ps.ParticleCount.Should().Be(15, "the t=0 burst fires immediately");

        ps.Simulate(0.45f, withChildren: false, restart: false); // t = 0.55
        ps.ParticleCount.Should().Be(23, "the first cycle of the t=0.5 burst adds 8 particles");

        ps.Simulate(0.25f, withChildren: false, restart: false); // t = 0.80
        ps.ParticleCount.Should().Be(31, "the second cycle at t=0.75 adds another 8 particles");
    }

    [Fact]
    public void DeterministicSeed_ProducesIdenticalTrajectoriesOnRestart()
    {
        GameObject go = new("Fountain");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.AutoRandomSeed = false;
        ps.RandomSeed = 424242;
        ps.RateOverTime = 30f;
        ps.ShapeType = ParticleShapeType.Sphere;
        ps.ShapeRadius = 2.5f;
        ps.GravityModifier = 1.0f;

        ps.Simulate(0.4f, withChildren: false, restart: true);
        Vector3[] firstRun = ps.Particles.ToArray().Select(p => p.Position).ToArray();
        firstRun.Should().NotBeEmpty();

        ps.Simulate(0.4f, withChildren: false, restart: true);
        Vector3[] secondRun = ps.Particles.ToArray().Select(p => p.Position).ToArray();

        secondRun.Should().Equal(firstRun);
    }

    [Fact]
    public void LifetimeModules_ApplyGravityForceSizeCurveColorFadeAndRotation()
    {
        GameObject go = new("Smoke");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.RateOverTime = 0f;
        ps.ShapeEnabled = false;
        ps.StartSpeed = 0f;
        ps.StartSpeedMax = 0f;
        ps.StartLifetime = 2.0f;
        ps.StartLifetimeMax = 2.0f;
        ps.StartSize = 2.0f;
        ps.StartSizeMax = 2.0f;
        ps.GravityModifier = 0f;

        ps.ForceOverLifetimeEnabled = true;
        ps.Force = new Vector3(4.0f, 2.0f, 0.0f);

        ps.SizeOverLifetimeEnabled = true;
        ps.SizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 3f);

        ps.ColorOverLifetimeEnabled = true;
        ps.StartColor = new Vector3(1f, 1f, 1f);
        ps.StartAlpha = 1f;
        ps.EndColor = new Vector3(0f, 0f, 0f);
        ps.EndAlpha = 0f;

        ps.RotationOverLifetimeEnabled = true;
        ps.AngularVelocity = 90f; // degrees/sec

        ps.Emit(1);
        ps.Play();
        ps.Simulate(1.0f, withChildren: false, restart: false); // Halfway through 2.0s lifetime (t = 0.5).

        ps.ParticleCount.Should().Be(1);
        Particle p = ps.Particles[0];

        p.Position.X.Should().BeGreaterThan(1.5f, "constant +X force accelerates the particle");
        p.Size.Should().BeApproximately(4.0f, 0.25f, "startSize 2.0 * curve(0.5)=2.0 -> ~4.0");
        p.Color.W.Should().BeApproximately(0.5f, 0.1f, "alpha fades linearly from 1 to 0");
        p.Rotation.Should().BeApproximately(MathF.PI * 0.5f, 0.1f, "90 deg/s over 1s = pi/2 radians");
    }

    [Fact]
    public void WorldVsLocalSimulationSpace_HandlesEmitterMovementCorrectly()
    {
        GameObject worldEmitter = new("WorldTrail");
        ParticleSystem worldPs = worldEmitter.AddComponent<ParticleSystem>();
        worldPs.SimulationSpace = ParticleSimulationSpace.World;
        worldPs.RateOverTime = 0f;
        worldPs.StartSpeed = 0f;
        worldPs.StartSpeedMax = 0f;
        worldPs.ShapeEnabled = false;
        worldPs.Emit(1);

        GameObject localEmitter = new("LocalAura");
        ParticleSystem localPs = localEmitter.AddComponent<ParticleSystem>();
        localPs.SimulationSpace = ParticleSimulationSpace.Local;
        localPs.RateOverTime = 0f;
        localPs.StartSpeed = 0f;
        localPs.StartSpeedMax = 0f;
        localPs.ShapeEnabled = false;
        localPs.Emit(1);

        // Move both emitters to X = 10.
        worldEmitter.Transform.Position = new Vector3(10f, 0f, 0f);
        localEmitter.Transform.Position = new Vector3(10f, 0f, 0f);

        worldPs.GetParticleWorldPosition(in worldPs.Particles[0]).X.Should().BeApproximately(0f, 1e-4f,
            "world-space particles stay where they spawned");
        localPs.GetParticleWorldPosition(in localPs.Particles[0]).X.Should().BeApproximately(10f, 1e-4f,
            "local-space particles ride with the emitter's Transform");
    }

    [Fact]
    public void SceneYaml_RoundTripsParticleSystemSettingsAndBursts()
    {
        GameObject vfx = new("TorchFire");
        ParticleSystem ps = vfx.AddComponent<ParticleSystem>();
        ps.Duration = 3.5f;
        ps.RateOverTime = 45f;
        ps.ShapeType = ParticleShapeType.Cone;
        ps.ConeAngle = 18f;
        ps.BlendMode = ParticleBlendMode.Additive;
        ps.RenderMode = ParticleRenderMode.StretchedBillboard;
        ps.SetBursts([new ParticleBurst(0.25f, 12, 24, 3, 0.5f, 0.85f)]);

        SceneSerializer serializer = new();
        string yaml = serializer.SerializeSceneToString([vfx]);

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();
        ParticleSystem loadedPs = loaded.GetComponent<ParticleSystem>()!;

        loadedPs.Should().NotBeNull();
        loadedPs.Duration.Should().Be(3.5f);
        loadedPs.RateOverTime.Should().Be(45f);
        loadedPs.ConeAngle.Should().Be(18f);
        loadedPs.BlendMode.Should().Be(ParticleBlendMode.Additive);
        loadedPs.RenderMode.Should().Be(ParticleRenderMode.StretchedBillboard);
        loadedPs.Bursts.Should().ContainSingle().Which.Should().Be(
            new ParticleBurst(0.25f, 12, 24, 3, 0.5f, 0.85f));
    }
}
