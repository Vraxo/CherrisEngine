using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Physics;

/// <summary>
/// The static queries scripts reach for — rays, casts and volumes — against the three collider shapes, and the
/// switches that decide what they may hit.
/// </summary>
public class PhysicsQueryTests
{
    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] gameObjects)
    {
        Scene scene = new(string.Empty, [.. gameObjects]);
        scene.Start(physics, audio);

        return scene;
    }

    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        GameObject gameObject = new(name);
        gameObject.Transform.WorldPosition = position;
        gameObject.AddComponent(new BoxCollider { Size = size });
        return gameObject;
    }

    [Fact]
    public void ARayReportsTheFaceItMeets()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = Box("Block", Vector3.Zero, new Vector3(2.0f, 2.0f, 2.0f));
        _ = StartScene(physics, audio, block);

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out RaycastHit hit).Should().BeTrue();

        hit.Distance.Should().BeApproximately(4.0f, 0.001f);
        hit.Point.Should().Be(new Vector3(0.0f, 0.0f, -1.0f));
        hit.Normal.Should().Be(new Vector3(0.0f, 0.0f, -1.0f));
        hit.GameObject.Should().BeSameAs(block);
        hit.Transform.Should().BeSameAs(block.Transform);
    }

    [Fact]
    public void ARayThatMissesSaysSo()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        _ = StartScene(physics, audio, Box("Block", Vector3.Zero, new Vector3(2.0f, 2.0f, 2.0f)));

        Physics.Raycast(new Vector3(0.0f, 10.0f, -5.0f), Vector3.UnitZ, out _).Should().BeFalse();

        // Out of reach is the same as out of the way.
        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out _, maxDistance: 2.0f).Should().BeFalse();
    }

    [Fact]
    public void ARayStartingInsideAShapeHitsAtItsOrigin()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        _ = StartScene(physics, audio, Box("Block", Vector3.Zero, new Vector3(4.0f, 4.0f, 4.0f)));

        Physics.Raycast(Vector3.Zero, Vector3.UnitZ, out RaycastHit hit).Should().BeTrue();
        hit.Distance.Should().Be(0.0f);
        hit.Point.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void RaycastAllReportsEveryShapeNearestFirst()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject near = Box("Near", new Vector3(0.0f, 0.0f, -2.0f), new Vector3(1.0f));
        GameObject far = Box("Far", new Vector3(0.0f, 0.0f, 2.0f), new Vector3(1.0f));

        _ = StartScene(physics, audio, near, far);

        RaycastHit[] hits = Physics.RaycastAll(new Vector3(0.0f, 0.0f, -10.0f), Vector3.UnitZ);

        hits.Should().HaveCount(2);
        hits[0].GameObject.Should().BeSameAs(near);
        hits[1].GameObject.Should().BeSameAs(far);
        hits[0].Distance.Should().BeLessThan(hits[1].Distance);
    }

    [Fact]
    public void ARayCanBeToldWhichLayersToLookAt()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = Box("Block", Vector3.Zero, new Vector3(2.0f));
        block.Layer = Layers.Water;
        _ = StartScene(physics, audio, block);

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out _, layerMask: LayerMask.ToMask(Layers.Default))
            .Should().BeFalse();

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out _, layerMask: LayerMask.GetMask("Water"))
            .Should().BeTrue();
    }

    [Fact]
    public void ATriggerIsReportedOnlyWhenQueriesAreToldToSeeIt()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject volume = new("Volume");
        volume.AddComponent(new BoxCollider { Size = new Vector3(2.0f), IsTrigger = true });
        _ = StartScene(physics, audio, volume);

        Vector3 origin = new(0.0f, 0.0f, -5.0f);

        Physics.Raycast(origin, Vector3.UnitZ, out _).Should().BeTrue("triggers count as hits by default");
        Physics.Raycast(origin, Vector3.UnitZ, out _, queryTriggerInteraction: QueryTriggerInteraction.Ignore)
            .Should().BeFalse();

        Physics.QueriesHitTriggers = false;

        try
        {
            Physics.Raycast(origin, Vector3.UnitZ, out _).Should().BeFalse();
            Physics.Raycast(origin, Vector3.UnitZ, out _, queryTriggerInteraction: QueryTriggerInteraction.Collide)
                .Should().BeTrue();
        }
        finally
        {
            Physics.QueriesHitTriggers = true;
        }
    }

    [Fact]
    public void ASphereAndACapsuleAreHitWhereTheyAreWidest()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(0.0f, 0.0f, 0.0f);
        ball.AddComponent(new SphereCollider { Radius = 1.0f });

        GameObject pillar = new("Pillar");
        pillar.Transform.WorldPosition = new Vector3(0.0f, 5.0f, 0.0f);
        pillar.AddComponent(new CapsuleCollider { Radius = 0.5f, Height = 2.0f });

        _ = StartScene(physics, audio, ball, pillar);

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out RaycastHit ballHit).Should().BeTrue();
        ballHit.Distance.Should().BeApproximately(4.0f, 0.01f);
        ballHit.Normal.Should().Be(new Vector3(0.0f, 0.0f, -1.0f));

        Physics.Raycast(new Vector3(0.0f, 5.0f, -5.0f), Vector3.UnitZ, out RaycastHit pillarHit).Should().BeTrue();
        pillarHit.Distance.Should().BeApproximately(4.5f, 0.01f);
    }

    [Fact]
    public void ASweptSphereStopsItsRadiusEarly()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        _ = StartScene(physics, audio, Box("Wall", new Vector3(0.0f, 0.0f, 0.0f), new Vector3(2.0f)));

        Physics.SphereCast(
            new Vector3(0.0f, 0.0f, -5.0f),
            0.5f,
            Vector3.UnitZ,
            out RaycastHit hit).Should().BeTrue();

        // The sphere's centre stops half a radius short of the wall, and the surface it touches is on the wall.
        hit.Distance.Should().BeApproximately(3.5f, 0.001f);
        hit.Point.Should().Be(new Vector3(0.0f, 0.0f, -1.0f));
        hit.Normal.Should().Be(new Vector3(0.0f, 0.0f, -1.0f));
    }

    [Fact]
    public void ALinecastIsARayWithAnEnd()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = Box("Block", Vector3.Zero, new Vector3(2.0f));
        _ = StartScene(physics, audio, block);

        Physics.Linecast(new Vector3(0.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, 5.0f), out RaycastHit hit)
            .Should().BeTrue();
        hit.GameObject.Should().BeSameAs(block);

        Physics.Linecast(new Vector3(0.0f, 0.0f, -5.0f), new Vector3(0.0f, 0.0f, -3.0f), out _).Should().BeFalse();
    }

    [Fact]
    public void VolumesReportWhatIsInsideThem()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(1.0f, 0.0f, 0.0f);
        SphereCollider sphere = ball.AddComponent(new SphereCollider { Radius = 0.5f });

        GameObject block = Box("Block", new Vector3(-4.0f, 0.0f, 0.0f), new Vector3(2.0f));

        _ = StartScene(physics, audio, ball, block);

        Physics.OverlapSphere(Vector3.Zero, 2.0f).Should().ContainSingle().Which.Should().BeSameAs(sphere);
        Physics.CheckSphere(Vector3.Zero, 2.0f).Should().BeTrue();
        Physics.CheckSphere(new Vector3(20.0f, 0.0f, 0.0f), 2.0f).Should().BeFalse();

        Collider[] inBox = Physics.OverlapBox(new Vector3(-4.0f, 0.0f, 0.0f), new Vector3(1.0f));
        inBox.Should().ContainSingle().Which.GameObject.Should().BeSameAs(block);

        Physics.CheckBox(new Vector3(0.0f, 20.0f, 0.0f), Vector3.One).Should().BeFalse();
    }

    [Fact]
    public void QueriesWithoutAWorldAnswerNothing()
    {
        // No scene has been started, so there is no world to ask.
        Physics.Raycast(Vector3.Zero, Vector3.UnitZ, out _).Should().BeFalse();
        Physics.RaycastAll(Vector3.Zero, Vector3.UnitZ).Should().BeEmpty();
        Physics.OverlapSphere(Vector3.Zero, 10.0f).Should().BeEmpty();
        Physics.CheckSphere(Vector3.Zero, 10.0f).Should().BeFalse();
    }

    [Fact]
    public void ADisabledColliderCannotBeHit()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = Box("Block", Vector3.Zero, new Vector3(2.0f));
        _ = StartScene(physics, audio, block);

        block.GetComponent<BoxCollider>()!.Enabled = false;

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out _).Should().BeFalse();

        block.SetActive(false);
        block.GetComponent<BoxCollider>()!.Enabled = true;

        Physics.Raycast(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ, out _).Should().BeFalse();
    }
}
