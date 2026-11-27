using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.World;

public sealed class CharacterControllerTests
{
    private sealed class HitRecorder : Script
    {
        public ControllerColliderHit? LastHit { get; private set; }
        public override void OnControllerColliderHit(ControllerColliderHit hit) => LastHit = hit;
    }

    private sealed class DisableControllerOnTrigger : Script
    {
        public CharacterController? Controller { get; set; }
        public override void OnTriggerEnter(Collider other) => Controller!.Enabled = false;
    }

    private sealed class TriggerRecorder : Script
    {
        public int EnterCount { get; private set; }
        public int StayCount { get; private set; }
        public int ExitCount { get; private set; }
        public int ControllerEnterCount { get; private set; }
        public int ControllerStayCount { get; private set; }
        public int ControllerExitCount { get; private set; }
        public override void OnTriggerEnter(Collider other) => EnterCount++;
        public override void OnTriggerStay(Collider other) => StayCount++;
        public override void OnTriggerExit(Collider other) => ExitCount++;
        public override void OnTriggerEnter(CharacterController other) => ControllerEnterCount++;
        public override void OnTriggerStay(CharacterController other) => ControllerStayCount++;
        public override void OnTriggerExit(CharacterController other) => ControllerExitCount++;
    }

    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] objects)
    {
        Scene scene = new(string.Empty, [.. objects]);
        scene.Start(physics, audio);
        return scene;
    }

    private static GameObject Floor()
    {
        GameObject floor = new("Floor");
        floor.Transform.WorldPosition = new Vector3(0f, -0.5f, 0f);
        return floor;
    }

    [Fact]
    public void CapsuleCastReportsTheFirstSurfaceHitByTheSweptCapsule()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject wall = new("Wall");
        wall.AddComponent(new BoxCollider { Size = new Vector3(2f) });
        _ = StartScene(physics, audio, wall);

        Physics.CapsuleCast(new Vector3(0f, -0.5f, -5f), new Vector3(0f, 0.5f, -5f), 0.5f,
            Vector3.UnitZ, out RaycastHit hit, maxDistance: 10f).Should().BeTrue();

        hit.GameObject.Should().BeSameAs(wall);
        hit.Distance.Should().BeApproximately(3.5f, 0.03f);
    }

    [Fact]
    public void CapsuleCastUsesRoundedBoxCornerSeparationRatherThanAnExpandedBox()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject block = new("Block");
        block.AddComponent(new BoxCollider { Size = new Vector3(2f) });
        _ = StartScene(physics, audio, block);

        Physics.CapsuleCast(new Vector3(-5f, -0.5f, 1.4f), new Vector3(-5f, 0.5f, 1.4f), 0.5f,
            Vector3.UnitX, out RaycastHit hit, maxDistance: 10f).Should().BeTrue();

        hit.Distance.Should().BeApproximately(3.7f, 0.02f);
        hit.Normal.X.Should().BeLessThan(-0.5f);
        hit.Normal.Z.Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void HorizontalCapsuleCastIgnoresAnExactlyTouchingFloor()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject floor = Floor();
        floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        _ = StartScene(physics, audio, floor);

        Physics.CapsuleCast(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 1.5f, 0f), 0.5f,
            Vector3.UnitX, out _, maxDistance: 2f).Should().BeFalse();
    }

    [Fact]
    public void ControllerMovesFreelyAndReportsItsVelocity()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, actor);
        Time.Advance(1f / 60f);

        controller.Move(new Vector3(2f, 0f, 0f)).Should().Be(CollisionFlags.None);
        actor.Transform.WorldPosition.X.Should().BeApproximately(2f, 0.001f);
        controller.Velocity.X.Should().BeApproximately(120f, 0.01f);
        controller.IsGrounded.Should().BeFalse();
    }

    [Fact]
    public void ControllerStopsAtAWallAndReportsASideContact()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject wall = new("Wall");
        wall.Transform.WorldPosition = new Vector3(2f, 1f, 0f);
        wall.AddComponent(new BoxCollider { Size = new Vector3(1f, 2f, 4f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        _ = StartScene(physics, audio, wall, actor);

        CollisionFlags flags = controller.Move(new Vector3(4f, 0f, 0f));

        (flags & CollisionFlags.Sides).Should().Be(CollisionFlags.Sides);
        actor.Transform.WorldPosition.X.Should().BeLessThan(1.2f);
        actor.Transform.WorldPosition.X.Should().BeGreaterThan(0.8f);
    }

    [Fact]
    public void UpwardMotionDoesNotGetMarkedGroundedByTheProbeBelow()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject floor = Floor();
        floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, floor, actor);

        controller.Move(new Vector3(0f, 0.05f, 0f));

        controller.IsGrounded.Should().BeFalse();
        (controller.CollisionFlags & CollisionFlags.Below).Should().Be(CollisionFlags.None);
        actor.Transform.WorldPosition.Y.Should().BeApproximately(0.05f, 0.01f);
    }

    [Fact]
    public void ControllerDispatchesTriggerEnterStayAndExitForOverlappingVolumes()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject triggerObject = new("Sensor");
        triggerObject.Transform.WorldPosition = new Vector3(1f, 1f, 0f);
        triggerObject.AddComponent(new BoxCollider { Size = new Vector3(1f, 2f, 2f), IsTrigger = true });
        TriggerRecorder triggerRecorder = triggerObject.AddComponent(new TriggerRecorder());
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        TriggerRecorder recorder = actor.AddComponent(new TriggerRecorder());
        _ = StartScene(physics, audio, triggerObject, actor);

        controller.Move(Vector3.UnitX);
        controller.Move(Vector3.Zero);
        controller.Move(new Vector3(2f, 0f, 0f));

        recorder.EnterCount.Should().Be(1);
        recorder.StayCount.Should().Be(1);
        recorder.ExitCount.Should().Be(1);
        triggerRecorder.ControllerEnterCount.Should().Be(1);
        triggerRecorder.ControllerStayCount.Should().Be(1);
        triggerRecorder.ControllerExitCount.Should().Be(1);
    }

    [Fact]
    public void DisablingControllerDuringTriggerEnterQueuesExitAfterEnterDispatchCompletes()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject triggerObject = new("Sensor");
        triggerObject.AddComponent(new BoxCollider { Size = new Vector3(2f, 2f, 2f), IsTrigger = true });
        TriggerRecorder triggerRecorder = triggerObject.AddComponent(new TriggerRecorder());
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        TriggerRecorder actorRecorder = actor.AddComponent(new TriggerRecorder());
        actor.AddComponent(new DisableControllerOnTrigger { Controller = controller });
        _ = StartScene(physics, audio, triggerObject, actor);

        controller.Move(Vector3.Zero);

        controller.Enabled.Should().BeFalse();
        actorRecorder.EnterCount.Should().Be(1);
        actorRecorder.ExitCount.Should().Be(1);
        triggerRecorder.ControllerEnterCount.Should().Be(1);
        triggerRecorder.ControllerExitCount.Should().Be(1);
    }

    [Fact]
    public void ControllerReportsAnAboveCollisionWhenMovingIntoACeiling()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject ceiling = new("Ceiling");
        ceiling.Transform.WorldPosition = new Vector3(0f, 3f, 0f);
        ceiling.AddComponent(new BoxCollider { Size = new Vector3(10f, 1f, 10f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        _ = StartScene(physics, audio, ceiling, actor);

        CollisionFlags flags = controller.Move(new Vector3(0f, 2f, 0f));

        (flags & CollisionFlags.Above).Should().Be(CollisionFlags.Above);
        actor.Transform.WorldPosition.Y.Should().BeLessThan(0.5f);
        actor.Transform.WorldPosition.Y.Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public void ControllerHitCallbacksAreDeliveredToScriptsOnItsObject()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject wall = new("Wall");
        wall.Transform.WorldPosition = new Vector3(2f, 1f, 0f);
        wall.AddComponent(new BoxCollider { Size = new Vector3(1f, 2f, 4f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        HitRecorder recorder = actor.AddComponent(new HitRecorder());
        _ = StartScene(physics, audio, wall, actor);

        controller.Move(new Vector3(4f, 0f, 0f));

        recorder.LastHit.Should().NotBeNull();
        recorder.LastHit!.Value.GameObject.Should().BeSameAs(wall);
        recorder.LastHit.Value.Normal.X.Should().BeLessThan(-0.9f);
    }

    [Fact]
    public void ZeroDisplacementRefreshesGroundedState()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject floor = Floor();
        floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, floor, actor);

        (controller.Move(Vector3.Zero) & CollisionFlags.Below).Should().Be(CollisionFlags.Below);
        controller.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public void ControllerCanMoveHorizontallyWhileStandingOnGround()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject floor = Floor();
        floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, floor, actor);

        CollisionFlags flags = controller.Move(new Vector3(2f, 0f, 0f));

        actor.Transform.WorldPosition.X.Should().BeApproximately(2f, 0.02f);
        (flags & CollisionFlags.Below).Should().Be(CollisionFlags.Below);
        controller.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public void FastMoveThroughATriggerStillDispatchesEnterAndExit()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject triggerObject = new("Thin Sensor");
        triggerObject.Transform.WorldPosition = new Vector3(1f, 1f, 0f);
        triggerObject.AddComponent(new BoxCollider { Size = new Vector3(0.2f, 2f, 2f), IsTrigger = true });
        TriggerRecorder triggerRecorder = triggerObject.AddComponent(new TriggerRecorder());
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        TriggerRecorder actorRecorder = actor.AddComponent(new TriggerRecorder());
        _ = StartScene(physics, audio, triggerObject, actor);

        controller.Move(new Vector3(3f, 0f, 0f));

        actorRecorder.EnterCount.Should().Be(1);
        actorRecorder.ExitCount.Should().Be(1);
        triggerRecorder.ControllerEnterCount.Should().Be(1);
        triggerRecorder.ControllerExitCount.Should().Be(1);
    }

    [Fact]
    public void ControllerDoesNotClimbASlopeSteeperThanItsSlopeLimit()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject ramp = new("Steep Ramp");
        ramp.Transform.WorldRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 3f);
        ramp.AddComponent(new BoxCollider { Size = new Vector3(8f, 0.2f, 4f) });
        GameObject actor = new("Player");
        actor.Transform.WorldPosition = new Vector3(0f, 0.7f, 0f);
        CharacterController controller = actor.AddComponent(new CharacterController { SlopeLimit = 45f });
        _ = StartScene(physics, audio, ramp, actor);

        CollisionFlags flags = controller.Move(new Vector3(1f, 0f, 0f));

        (flags & CollisionFlags.Sides).Should().Be(CollisionFlags.Sides);
        actor.Transform.WorldPosition.X.Should().BeLessThan(0.3f);
        actor.Transform.WorldPosition.Y.Should().BeApproximately(0.7f, 0.05f);
    }

    [Fact]
    public void ControllerDoesNotStepOntoAnObstacleWhileAirborne()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject step = new("Floating Step");
        step.Transform.WorldPosition = new Vector3(0.8f, 5.1f, 0f);
        step.AddComponent(new BoxCollider { Size = new Vector3(0.4f, 0.2f, 2f) });
        GameObject actor = new("Player");
        actor.Transform.WorldPosition = new Vector3(0f, 5f, 0f);
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0.3f });
        _ = StartScene(physics, audio, step, actor);

        CollisionFlags flags = controller.Move(new Vector3(1f, 0f, 0f));

        (flags & CollisionFlags.Sides).Should().Be(CollisionFlags.Sides);
        actor.Transform.WorldPosition.X.Should().BeLessThan(0.3f);
        actor.Transform.WorldPosition.Y.Should().BeApproximately(5f, 0.02f);
    }

    [Fact]
    public void ControllerStepsUpAHeightWithinItsStepOffset()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject floor = Floor();
        floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject step = new("Step");
        step.Transform.WorldPosition = new Vector3(0.8f, 0.1f, 0f);
        step.AddComponent(new BoxCollider { Size = new Vector3(0.4f, 0.2f, 2f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0.3f });
        _ = StartScene(physics, audio, floor, step, actor);

        CollisionFlags flags = controller.Move(new Vector3(1f, 0f, 0f));

        actor.Transform.WorldPosition.X.Should().BeApproximately(1f, 0.05f);
        actor.Transform.WorldPosition.Y.Should().BeGreaterThan(0.15f);
        (flags & CollisionFlags.Below).Should().Be(CollisionFlags.Below);
    }

    [Fact]
    public void GroundedControllerIsCarriedByATranslatingPlatform()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject platform = Floor();
        platform.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, platform, actor);

        controller.Move(new Vector3(0.01f, 0f, 0f)).Should().Be(CollisionFlags.Below);
        platform.Transform.WorldPosition += Vector3.UnitX;
        controller.Move(Vector3.Zero);

        actor.Transform.WorldPosition.X.Should().BeApproximately(1.01f, 0.02f);
        controller.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public void MinMoveDistanceIgnoresSmallInputWithoutSuppressingPlatformCarry()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject platform = Floor();
        platform.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { MinMoveDistance = 0.1f });
        _ = StartScene(physics, audio, platform, actor);
        controller.Move(Vector3.Zero).Should().Be(CollisionFlags.Below);

        platform.Transform.WorldPosition += new Vector3(0.2f, 0f, 0f);
        controller.Move(new Vector3(0.05f, 0f, 0f));

        actor.Transform.WorldPosition.X.Should().BeApproximately(0.2f, 0.02f);
    }

    [Fact]
    public void GroundedControllerIsCarriedByPlatformRotation()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject platform = Floor();
        platform.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f) });
        GameObject actor = new("Player");
        actor.Transform.WorldPosition = Vector3.UnitX;
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, platform, actor);

        controller.Move(new Vector3(0.01f, 0f, 0f));
        platform.Transform.WorldRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        controller.Move(Vector3.Zero);

        MathF.Abs(actor.Transform.WorldPosition.X).Should().BeLessThan(0.05f);
        MathF.Abs(actor.Transform.WorldPosition.Z).Should().BeGreaterThan(0.9f);
    }

    [Fact]
    public void OverlapRecoveryPushesAControllerOutOfSpawnPenetration()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject wall = new("Wall");
        wall.Transform.WorldPosition = new Vector3(0f, 1.25f, 0f);
        wall.AddComponent(new BoxCollider { Size = new Vector3(2f, 2.5f, 2f) });
        GameObject actor = new("Player");
        actor.Transform.WorldPosition = new Vector3(0.9f, 0f, 0f);
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        _ = StartScene(physics, audio, wall, actor);

        controller.Move(new Vector3(0.1f, 0f, 0f));

        actor.Transform.WorldPosition.X.Should().BeGreaterThan(1.4f);
    }

    [Fact]
    public void OverlapRecoveryDoesNotDepenetrateFromDynamicRigidBodies()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject movingBox = new("Dynamic obstacle");
        movingBox.Transform.WorldPosition = new Vector3(0f, 1f, 0f);
        movingBox.AddComponent(new BoxCollider { Size = new Vector3(2f) });
        movingBox.AddComponent(new RigidBody { UseGravity = false });
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController { StepOffset = 0f });
        _ = StartScene(physics, audio, movingBox, actor);

        controller.Move(Vector3.Zero);

        actor.Transform.WorldPosition.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void MoveDoesNotApplyGravityButSimpleMoveDoes()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        GameObject actor = new("Player");
        CharacterController controller = actor.AddComponent(new CharacterController());
        _ = StartScene(physics, audio, actor);
        Time.Advance(1f / 60f);

        controller.Move(Vector3.Zero);
        actor.Transform.WorldPosition.Y.Should().BeApproximately(0f, 0.001f);
        controller.SimpleMove(Vector3.Zero);
        actor.Transform.WorldPosition.Y.Should().BeLessThan(0f);
    }

    [Fact]
    public void ControllerCanBeAddedFromTheBuiltInComponentCatalog()
    {
        ComponentCatalog.Find("Character Controller")!.ComponentType.Should().Be(typeof(CharacterController));
    }

    [Fact]
    public void ControllerSettingsSurviveASceneSaveAndLoad()
    {
        GameObject actor = new("Player");
        actor.AddComponent(new CharacterController
        {
            Height = 1.8f,
            Radius = 0.35f,
            Center = new Vector3(0f, 0.9f, 0f),
            SlopeLimit = 50f,
            StepOffset = 0.25f,
            SkinWidth = 0.06f,
            MinMoveDistance = 0.001f,
            DetectCollisions = false
        });
        string yaml = new SceneSerializer().SerializeSceneToString([actor]);
        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        CharacterController loaded = loader.LoadSceneFromYaml(yaml)[0].GetComponent<CharacterController>()!;

        loaded.Height.Should().BeApproximately(1.8f, 0.001f);
        loaded.Radius.Should().BeApproximately(0.35f, 0.001f);
        loaded.Center.Should().Be(new Vector3(0f, 0.9f, 0f));
        loaded.SlopeLimit.Should().Be(50f);
        loaded.StepOffset.Should().BeApproximately(0.25f, 0.001f);
        loaded.SkinWidth.Should().BeApproximately(0.06f, 0.001f);
        loaded.MinMoveDistance.Should().BeApproximately(0.001f, 0.0001f);
        loaded.DetectCollisions.Should().BeFalse();
    }
}
