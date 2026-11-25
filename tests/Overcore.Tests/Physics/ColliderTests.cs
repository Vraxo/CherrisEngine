using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Physics;

/// <summary>
/// The collider layer: a shape that is a component of its own, registered with the world when the scene starts,
/// posed by its object, and able to say what it holds and how big it is.
/// </summary>
public class ColliderTests
{
    /// <summary>A scene that has been started, which is what registers its colliders with the world.</summary>
    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] gameObjects)
    {
        Scene scene = new(string.Empty, [.. gameObjects]);
        scene.Start(physics, audio);

        return scene;
    }

    [Fact]
    public void AColliderWithoutABodyBecomesAFixedShapeInTheWorld()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        BoxCollider collider = ground.AddComponent(new BoxCollider());

        _ = StartScene(physics, audio, ground);

        collider.IsRegistered.Should().BeTrue("the scene started, so the collider joined the world");
        collider.IsActive.Should().BeTrue();
        physics.Colliders.Should().ContainSingle().Which.Should().BeSameAs(collider);
    }

    [Fact]
    public void ABodyTakesItsShapeFromTheColliderOnTheSameObject()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject crate = new("Crate");
        crate.AddComponent(new BoxCollider { Size = new Vector3(2.0f, 1.0f, 3.0f) });
        RigidBody body = crate.AddComponent(new RigidBody { Mass = 2.0f });

        _ = StartScene(physics, audio, crate);

        body.BepuBodyHandle.HasValue.Should().BeTrue("the body is dynamic, so it is a collidable that moves");
        body.BepuStaticHandle.HasValue.Should().BeFalse();

        // Nothing is under it, so it does nothing but fall.
        for (int step = 0; step < 30; step++)
        {
            physics.Update(1.0f / 60.0f);
        }

        crate.Transform.WorldPosition.Y.Should().BeLessThan(-0.5f, "gravity pulled it down for half a second");
    }

    [Fact]
    public void ATrackerReportsTheWorldBoxItOccupies()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = new("Block");
        block.Transform.WorldPosition = new Vector3(1.0f, 2.0f, 3.0f);
        BoxCollider collider = block.AddComponent(new BoxCollider { Size = new Vector3(2.0f, 4.0f, 6.0f) });

        _ = StartScene(physics, audio, block);

        BoundingBox bounds = collider.Bounds;

        bounds.Min.Should().Be(new Vector3(0.0f, 0.0f, 0.0f));
        bounds.Max.Should().Be(new Vector3(2.0f, 4.0f, 6.0f));
        collider.WorldCenter.Should().Be(new Vector3(1.0f, 2.0f, 3.0f));
    }

    [Fact]
    public void CollidersScaleWithTheObjectAndRespectTheirCentre()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = new("Block");
        block.Transform.Scale = new Vector3(2.0f);
        SphereCollider sphere = block.AddComponent(new SphereCollider { Radius = 1.0f, Center = new Vector3(0.0f, 1.0f, 0.0f) });

        _ = StartScene(physics, audio, block);

        sphere.WorldCenter.Should().Be(new Vector3(0.0f, 2.0f, 0.0f), "the centre is scaled with the object");

        sphere.ContainsPoint(new Vector3(0.0f, 2.5f, 0.0f)).Should().BeTrue();
        sphere.ContainsPoint(new Vector3(0.0f, 5.0f, 0.0f)).Should().BeFalse();
    }

    [Fact]
    public void ATriggerIsRegisteredButIsNotASurface()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject volume = new("Volume");
        BoxCollider trigger = volume.AddComponent(new BoxCollider { IsTrigger = true });

        _ = StartScene(physics, audio, volume);

        trigger.IsRegistered.Should().BeTrue();
        trigger.AttachedRigidBody.Should().BeNull();
        physics.Colliders.Should().Contain(trigger);
    }

    [Fact]
    public void AShapeThatChangesIsRebuiltWithoutLeavingTheWorld()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = new("Block");
        BoxCollider collider = block.AddComponent(new BoxCollider { Size = Vector3.One });

        _ = StartScene(physics, audio, block);

        collider.Size = new Vector3(4.0f, 4.0f, 4.0f);

        collider.IsRegistered.Should().BeTrue();
        collider.Bounds.Max.Should().Be(new Vector3(2.0f, 2.0f, 2.0f));

        // The world's shape was replaced rather than added to: a query sees one collider, not two.
        physics.Colliders.Should().ContainSingle();
        Physics.Raycast(new Vector3(0.0f, 0.0f, -10.0f), Vector3.UnitZ, out RaycastHit hit).Should().BeTrue();
        hit.Distance.Should().BeApproximately(8.0f, 0.001f);
    }

    [Fact]
    public void ATrackerDisappearingTakesItOutOfTheWorld()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject block = new("Block");
        BoxCollider collider = block.AddComponent(new BoxCollider());

        _ = StartScene(physics, audio, block);

        collider.Enabled = false;

        collider.IsRegistered.Should().BeFalse();
        physics.Colliders.Should().BeEmpty();

        collider.Enabled = true;

        collider.IsRegistered.Should().BeTrue();
        physics.Colliders.Should().ContainSingle();
    }

    [Fact]
    public void CollidersSurviveBeingSavedAndLoaded()
    {
        GameObject crate = new("Crate");
        crate.AddComponent(new BoxCollider
        {
            Size = new Vector3(2.0f, 1.0f, 3.0f),
            IsTrigger = true,
            Friction = 0.2f,
            Bounciness = 0.7f
        });
        crate.AddComponent(new SphereCollider { Radius = 0.75f });
        crate.AddComponent(new CapsuleCollider { Radius = 0.4f, Height = 1.6f });

        SceneSerializer serializer = new();
        string yaml = serializer.SerializeSceneToString([crate]);

        yaml.Should().Contain("BoxCollider:").And.Contain("SphereCollider:").And.Contain("CapsuleCollider:");
        yaml.Should().Contain("IsTrigger: true");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        GameObject loaded = loader.LoadSceneFromYaml(yaml).Should().ContainSingle().Subject;

        BoxCollider? boxComponent = loaded.GetComponent<BoxCollider>();
        boxComponent.Should().NotBeNull();

        BoxCollider box = boxComponent!;
        box.Size.Should().Be(new Vector3(2.0f, 1.0f, 3.0f));
        box.IsTrigger.Should().BeTrue();
        box.Friction.Should().BeApproximately(0.2f, 0.0001f);
        box.Bounciness.Should().BeApproximately(0.7f, 0.0001f);

        loaded.GetComponent<SphereCollider>()!.Radius.Should().BeApproximately(0.75f, 0.0001f);
        loaded.GetComponent<CapsuleCollider>()!.Height.Should().BeApproximately(1.6f, 0.0001f);
    }

    [Fact]
    public void TheAddedColliderShapesAreOfferedByTheComponentCatalog()
    {
        ComponentCatalog.Find("Box Collider")!.ComponentType.Should().Be(typeof(BoxCollider));
        ComponentCatalog.Find("Sphere Collider")!.ComponentType.Should().Be(typeof(SphereCollider));
        ComponentCatalog.Find("Capsule Collider")!.ComponentType.Should().Be(typeof(CapsuleCollider));
        ComponentCatalog.Find("Rigid Body")!.ComponentType.Should().Be(typeof(RigidBody));
    }
}
