using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Physics;

/// <summary>
/// The callbacks a contact produces: <c>OnCollisionEnter</c>, <c>Stay</c> and <c>Exit</c> for a surface, and the
/// trigger trio for a volume — including that a trigger does not block anything and that a layer pair told to
/// ignore each other never reports at all.
/// </summary>
public class CollisionEventTests
{
    private const float StepSeconds = 1.0f / 60.0f;

    /// <summary>Counts what a contact reports, and keeps the payload of the last one.</summary>
    private sealed class ContactProbe : Script
    {
        public int CollisionEnters { get; private set; }
        public int CollisionStays { get; private set; }
        public int CollisionExits { get; private set; }
        public int TriggerEnters { get; private set; }
        public int TriggerStays { get; private set; }
        public int TriggerExits { get; private set; }

        public Collision? LastCollision { get; private set; }
        public Collider? LastTrigger { get; private set; }

        public override void OnCollisionEnter(Collision collision)
        {
            CollisionEnters++;
            LastCollision = collision;
        }

        public override void OnCollisionStay(Collision collision)
        {
            CollisionStays++;
            LastCollision = collision;
        }

        public override void OnCollisionExit(Collision collision) => CollisionExits++;

        public override void OnTriggerEnter(Collider other)
        {
            TriggerEnters++;
            LastTrigger = other;
        }

        public override void OnTriggerStay(Collider other)
        {
            TriggerStays++;
            LastTrigger = other;
        }

        public override void OnTriggerExit(Collider other) => TriggerExits++;
    }

    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] gameObjects)
    {
        Scene scene = new(string.Empty, [.. gameObjects]);
        scene.Start(physics, audio);

        return scene;
    }

    private static void Advance(PhysicsSystem physics, int steps)
    {
        for (int step = 0; step < steps; step++)
        {
            physics.Update(StepSeconds);
        }
    }

    [Fact]
    public void APairThatTouchesIsReportedToBothObjects()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        ground.Transform.WorldPosition = new Vector3(0.0f, -0.5f, 0.0f);
        ground.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f) });
        ContactProbe groundProbe = ground.AddComponent(new ContactProbe());

        GameObject crate = new("Crate");
        crate.Transform.WorldPosition = new Vector3(0.0f, 0.5f, 0.0f);
        crate.AddComponent(new BoxCollider { Size = Vector3.One });
        crate.AddComponent(new RigidBody());
        ContactProbe crateProbe = crate.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, ground, crate);

        Advance(physics, 5);

        crateProbe.CollisionEnters.Should().BeGreaterThan(0, "the touch began");
        crateProbe.CollisionStays.Should().BeGreaterThan(0, "and lasted more than one step");
        groundProbe.CollisionEnters.Should().Be(
            crateProbe.CollisionEnters, "both objects hear about the same touches");

        crateProbe.LastCollision.Should().NotBeNull();

        Collision collision = crateProbe.LastCollision!;
        collision.GameObject.Should().BeSameAs(ground, "a collision reports the other object, never the one it is on");
        collision.ContactCount.Should().BeGreaterThan(0);

        ContactPoint contact = collision.Contacts[0];
        contact.Normal.Should().Be(new Vector3(0.0f, 1.0f, 0.0f), "the floor pushes the crate up");
        contact.Point.Y.Should().BeApproximately(0.0f, 0.1f, "the contact sits on the floor's surface");
        contact.ThisCollider.Should().BeSameAs(crate.GetComponent<Collider>());
        contact.OtherCollider.Should().BeSameAs(ground.GetComponent<Collider>());
    }

    [Fact]
    public void ThePairIsReportedAsExitingWhenItComesApart()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject wall = new("Wall");
        wall.AddComponent(new BoxCollider { Size = new Vector3(2.0f, 2.0f, 0.5f) });
        ContactProbe wallProbe = wall.AddComponent(new ContactProbe());

        // A moving body is needed for there to be a contact at all: two fixed shapes are never tested against each
        // other, which is what keeps a scene of scenery cheap.
        GameObject crate = new("Crate");
        crate.Transform.WorldPosition = new Vector3(0.0f, 0.0f, 0.4f);
        crate.AddComponent(new BoxCollider { Size = Vector3.One });
        RigidBody body = crate.AddComponent(new RigidBody { UseGravity = false, Mass = 1000.0f });
        ContactProbe crateProbe = crate.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, wall, crate);

        // Held against the wall, so the pair lasts as long as this does.
        for (int step = 0; step < 3; step++)
        {
            body.Teleport(new Vector3(0.0f, 0.0f, 0.4f));
            physics.Update(StepSeconds);
        }

        wallProbe.CollisionEnters.Should().Be(1);
        crateProbe.CollisionStays.Should().BeGreaterThan(0);

        wall.Transform.WorldPosition = new Vector3(20.0f, 0.0f, 0.0f);
        Advance(physics, 1);

        wallProbe.CollisionExits.Should().Be(1);

        Advance(physics, 1);
        wallProbe.CollisionExits.Should().Be(1, "an exit is reported once, not every step afterwards");
    }

    [Fact]
    public void AFallingBodyLandsOnAFixedCollider()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        ground.Transform.WorldPosition = new Vector3(0.0f, -0.5f, 0.0f);
        ground.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f) });

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(0.0f, 1.5f, 0.0f);
        ball.AddComponent(new SphereCollider { Radius = 0.5f });
        RigidBody body = ball.AddComponent(new RigidBody());
        ContactProbe probe = ball.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, ground, ball);

        Advance(physics, 120);

        probe.CollisionEnters.Should().BeGreaterThan(0, "it landed");
        ball.Transform.WorldPosition.Y.Should().BeApproximately(0.5f, 0.15f, "it rests on the floor, a radius up");
        body.GetVelocity().Y.Should().BeApproximately(0.0f, 0.2f, "it stopped falling");
    }

    [Fact]
    public void ATriggerReportsWhatIsInsideItAndWhatLeaves()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject volume = new("Volume");
        BoxCollider trigger = volume.AddComponent(new BoxCollider { Size = new Vector3(2.0f), IsTrigger = true });
        ContactProbe volumeProbe = volume.AddComponent(new ContactProbe());

        GameObject visitor = new("Visitor");
        BoxCollider visitorCollider = visitor.AddComponent(new BoxCollider { Size = Vector3.One });
        ContactProbe visitorProbe = visitor.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, volume, visitor);

        Advance(physics, 2);

        volumeProbe.TriggerEnters.Should().Be(1);
        volumeProbe.LastTrigger.Should().BeSameAs(visitorCollider, "a trigger reports the collider that came in");
        visitorProbe.TriggerEnters.Should().Be(1, "the visitor hears about it too");
        visitorProbe.LastTrigger.Should().BeSameAs(trigger);

        volumeProbe.CollisionEnters.Should().Be(0, "a trigger is a volume, so it solves no contacts");

        visitor.Transform.WorldPosition = new Vector3(20.0f, 0.0f, 0.0f);
        Advance(physics, 1);

        volumeProbe.TriggerExits.Should().Be(1);
        visitorProbe.TriggerExits.Should().Be(1);
    }

    [Fact]
    public void ATriggerLetsABodyFallStraightThroughIt()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        ground.Transform.WorldPosition = new Vector3(0.0f, -0.5f, 0.0f);
        ground.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f) });

        GameObject volume = new("Volume");
        volume.Transform.WorldPosition = new Vector3(0.0f, 2.0f, 0.0f);
        volume.AddComponent(new BoxCollider { Size = new Vector3(3.0f, 0.5f, 3.0f), IsTrigger = true });
        ContactProbe volumeProbe = volume.AddComponent(new ContactProbe());

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(0.0f, 4.0f, 0.0f);
        ball.AddComponent(new SphereCollider { Radius = 0.5f });
        ball.AddComponent(new RigidBody());
        ContactProbe ballProbe = ball.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, ground, volume, ball);

        Advance(physics, 150);

        ballProbe.TriggerEnters.Should().Be(1, "the ball passed through the volume");
        ballProbe.CollisionEnters.Should().BeGreaterThan(0, "and then landed on the floor below it");
        ball.Transform.WorldPosition.Y.Should().BeApproximately(0.5f, 0.2f);
        _ = volumeProbe;
    }

    [Fact]
    public void TwoLayersToldToIgnoreEachOtherNeverReport()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        Physics.IgnoreLayerCollision(Layers.Default, Layers.Water, true);

        try
        {
            GameObject floor = new("Floor");
            floor.Layer = Layers.Water;
            floor.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f) });

            GameObject ball = new("Ball");
            ball.Transform.WorldPosition = new Vector3(0.0f, 3.0f, 0.0f);
            ball.AddComponent(new SphereCollider { Radius = 0.5f });
            ball.AddComponent(new RigidBody());
            ContactProbe probe = ball.AddComponent(new ContactProbe());

            _ = StartScene(physics, audio, floor, ball);

            Advance(physics, 120);

            probe.CollisionEnters.Should().Be(0, "the floor is on a layer the ball was told to pass through");
            ball.Transform.WorldPosition.Y.Should().BeLessThan(-1.0f, "so it fell through");
        }
        finally
        {
            Physics.IgnoreLayerCollision(Layers.Default, Layers.Water, false);
            Physics.ResetLayers();
        }
    }

    [Fact]
    public void AScriptThatIsSwitchedOffHearsNothing()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject wall = new("Wall");
        wall.AddComponent(new BoxCollider { Size = new Vector3(2.0f, 2.0f, 0.5f) });

        GameObject crate = new("Crate");
        crate.Transform.WorldPosition = new Vector3(0.0f, 0.0f, 0.4f);
        crate.AddComponent(new BoxCollider { Size = Vector3.One });
        crate.AddComponent(new RigidBody { UseGravity = false, Mass = 1000.0f });

        ContactProbe listening = crate.AddComponent(new ContactProbe());
        ContactProbe switchedOff = crate.AddComponent(new ContactProbe());
        switchedOff.Enabled = false;

        _ = StartScene(physics, audio, wall, crate);

        Advance(physics, 3);

        listening.CollisionEnters.Should().BeGreaterThan(0, "the pair really is touching");
        switchedOff.CollisionEnters.Should().Be(0, "a script the author switched off is not called");
    }

    [Fact]
    public void AFixedShapeBlocksWhereItHasBeenMovedTo()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject floor = new("Floor");
        floor.Transform.WorldPosition = new Vector3(0.0f, -20.0f, 0.0f);
        floor.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f) });

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(0.0f, 3.0f, 0.0f);
        ball.AddComponent(new SphereCollider { Radius = 0.5f });
        ball.AddComponent(new RigidBody());
        ContactProbe probe = ball.AddComponent(new ContactProbe());

        _ = StartScene(physics, audio, floor, ball);

        // Slid into place after the scene started: the world's copy of a fixed shape has to follow the transform.
        floor.Transform.WorldPosition = new Vector3(0.0f, -0.5f, 0.0f);

        Advance(physics, 120);

        probe.CollisionEnters.Should().BeGreaterThan(0);
        ball.Transform.WorldPosition.Y.Should().BeApproximately(0.5f, 0.15f);
    }

    [Fact]
    public void AFrictionAndBouncinessPairReachesTheSolver()
    {
        // The material the solver is handed is what decides how a pair slides and bounces. It is private to the
        // world, so this checks the effect that can be seen from outside: a bouncy ball comes back up off a floor.
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        ground.Transform.WorldPosition = new Vector3(0.0f, -0.5f, 0.0f);
        ground.AddComponent(new BoxCollider { Size = new Vector3(10.0f, 1.0f, 10.0f), Bounciness = 1.0f });

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(0.0f, 2.0f, 0.0f);
        ball.AddComponent(new SphereCollider { Radius = 0.5f, Bounciness = 1.0f });
        RigidBody body = ball.AddComponent(new RigidBody());

        _ = StartScene(physics, audio, ground, ball);

        bool rose = false;
        float previous = ball.Transform.WorldPosition.Y;

        for (int step = 0; step < 180 && !rose; step++)
        {
            physics.Update(StepSeconds);

            float current = ball.Transform.WorldPosition.Y;
            rose = current > previous + 0.001f;
            previous = current;
        }

        rose.Should().BeTrue("a lively surface pushes the ball back up after it lands");
        body.IsInitialized.Should().BeTrue();
    }
}
