using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The state machine: which state a frame is in, when a transition may be taken, what a trigger means, and how two
/// states blend while one becomes the other.
/// </summary>
public class AnimatorTests : IDisposable
{
    private readonly PhysicsSystem _physics = new();
    private readonly AudioSystem _audio = new();

    public void Dispose()
    {
        _physics.Dispose();
        _audio.Dispose();
    }

    // ---------------------------------------------------------------- start state

    [Fact]
    public void AnAnimatorWithNoController_WritesNothing()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator());

        animator.Invoking(a => a.Advance(0.1f)).Should().NotThrow();
        hips.Transform.Position.Y.Should().Be(0f);
    }

    [Fact]
    public void TheFirstAdvance_EntersTheDefaultState_AndWritesItsPose()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = IdleAt(5f) });

        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Idle");
        hips.Transform.Position.Y.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void AControllerWithNoLayers_DoesNothingRatherThanThrowing()
    {
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = new AnimatorController() });

        animator.Invoking(a => a.Advance(0.1f)).Should().NotThrow();
    }

    // ---------------------------------------------------------------- transitions

    [Fact]
    public void ATransitionWithoutConditions_IsTakenOnceItsExitTimeIsReached()
    {
        // The idle-to-run shape with no parameters: hold the state for its full length, then move on.
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");

        AnimatorState idle = layer.AddState("Idle", Clip("Idle", 0f));
        AnimatorState next = layer.AddState("Next", Clip("Next", 9f));

        idle.TransitionTo(next).HasExitTime = true;
        idle.Transitions[0].ExitTime = 1f;

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.CurrentState.Should().BeSameAs(idle);

        animator.Advance(1f);
        animator.CurrentState.Should().BeSameAs(next);
        hips.Transform.Position.Y.Should().BeApproximately(9f, 1e-5f);
    }

    [Fact]
    public void ATransitionWhoseConditionsFail_IsNotTaken()
    {
        AnimatorController controller = Running();
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        // Speed is zero, so Run's `Speed > 0.1` does not pass.
        animator.Advance(0.1f);
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Idle");
    }

    [Fact]
    public void ATransitionIsTaken_OnceItsConditionsPass()
    {
        AnimatorController controller = Running();
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetFloat("Speed", 3f);
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Run");
    }

    [Fact]
    public void OnlyOneTransitionIsTakenPerFrame()
    {
        // A chain A -> B -> C with unconditioned exits would run the whole graph in one tick if each transition were
        // evaluated after the previous landed. The frame that enters A may take A's exit, and stops there.
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");

        AnimatorState a = layer.AddState("A", Clip("A", 0f));
        AnimatorState b = layer.AddState("B", Clip("B", 1f));
        AnimatorState c = layer.AddState("C", Clip("C", 2f));

        a.TransitionTo(b);
        b.TransitionTo(c);

        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        // One advance enters A and takes its exit to B, and no further.
        animator.Advance(0.1f);
        animator.CurrentState.Should().BeSameAs(b);

        // The next frame takes B's exit; C is reached only now, not a frame earlier.
        animator.Advance(0.1f);
        animator.CurrentState.Should().BeSameAs(c);
    }

    [Fact]
    public void TheFirstTransitionWhoseConditionsPass_IsTheOneTaken()
    {
        // Transitions are tried in asset order, so the author's ordering decides which of two valid ways the machine
        // goes — not whichever happened to be evaluated last.
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter go = controller.AddParameter("Go", AnimatorParameterType.Bool);

        AnimatorState start = layer.AddState("Start", Clip("Start", 0f));
        AnimatorState first = layer.AddState("First", Clip("First", 1f));
        AnimatorState second = layer.AddState("Second", Clip("Second", 2f));

        start.TransitionTo(first).When(go.Hash, AnimatorConditionMode.If);
        start.TransitionTo(second).When(go.Hash, AnimatorConditionMode.If);

        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetBool("Go", true);
        animator.Advance(0.1f);

        animator.CurrentState.Should().BeSameAs(first);
    }

    [Fact]
    public void AnAnyStateTransition_InterruptsTheCurrentState()
    {
        // A hit reaction has to win over whatever is playing, which is why Any State is tried first.
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter hit = controller.AddParameter("Hit", AnimatorParameterType.Trigger);

        AnimatorState idle = layer.AddState("Idle", Clip("Idle", 0f));
        AnimatorState flinch = layer.AddState("Flinch", Clip("Flinch", 7f));

        // The Idle state has its own unconditional exit to the same place; Any State must still win the frame, which
        // is what an interrupt means.
        idle.TransitionTo(flinch).HasExitTime = true;
        idle.Transitions[0].ExitTime = 0.9f;

        layer.AnyState(flinch).When(hit.Hash, AnimatorConditionMode.If);

        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.CurrentState!.Name.Should().Be("Idle");

        animator.SetTrigger("Hit");
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Flinch");
    }

    // ---------------------------------------------------------------- triggers

    [Fact]
    public void ATrigger_IsConsumedByTheTransitionThatReadsIt()
    {
        // Set it, let the transition fire, and it is false again — so one press is one jump.
        AnimatorController controller = Jumping();
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetTrigger("Jump");
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Jump");
        animator.GetBool("Jump").Should().BeFalse();
    }

    [Fact]
    public void ATriggerSetTwiceBeforeItIsRead_IsStillOneTrigger()
    {
        // A trigger is set-state, not a count: two presses before the machine looks are one jump.
        AnimatorController controller = Jumping();
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetTrigger("Jump");
        animator.SetTrigger("Jump");
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Jump");
    }

    [Fact]
    public void ResetTrigger_CancelsATriggerBeforeItFires()
    {
        AnimatorController controller = Jumping();
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetTrigger("Jump");
        animator.ResetTrigger("Jump");
        animator.Advance(0.1f);

        animator.CurrentState!.Name.Should().Be("Idle");
    }

    // ---------------------------------------------------------------- blending

    [Fact]
    public void ATransitionWithADuration_BlendsTheTwoStates()
    {
        // A one-second cross-fade from height 0 to height 10.
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter go = controller.AddParameter("Go", AnimatorParameterType.Bool);

        AnimatorState from = layer.AddState("From", Clip("From", 0f));
        AnimatorState to = layer.AddState("To", Clip("To", 10f));

        AnimatorTransition transition = from.TransitionTo(to).When(go.Hash, AnimatorConditionMode.If);
        transition.Duration = 1f;

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetBool("Go", true);

        // The transition begins this frame at progress zero, then advances half a second.
        animator.Advance(0.5f);

        animator.IsInTransition.Should().BeTrue();

        // Halfway through the fade the destination is half in over the source: half of the way from 0 to 10.
        hips.Transform.Position.Y.Should().BeApproximately(5f, 1e-4f);
    }

    [Fact]
    public void AFinishedTransition_LandsExactlyOnTheDestination()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter go = controller.AddParameter("Go", AnimatorParameterType.Bool);

        AnimatorState from = layer.AddState("From", Clip("From", 0f));
        AnimatorState to = layer.AddState("To", Clip("To", 10f));

        from.TransitionTo(to).When(go.Hash, AnimatorConditionMode.If).Duration = 0.5f;

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetBool("Go", true);
        animator.Advance(0.6f);

        animator.IsInTransition.Should().BeFalse();
        animator.CurrentState.Should().BeSameAs(to);
        hips.Transform.Position.Y.Should().BeApproximately(10f, 1e-5f);
    }

    [Fact]
    public void AZeroDurationTransition_IsACut()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter go = controller.AddParameter("Go", AnimatorParameterType.Bool);

        AnimatorState from = layer.AddState("From", Clip("From", 0f));
        AnimatorState to = layer.AddState("To", Clip("To", 10f));

        from.TransitionTo(to).When(go.Hash, AnimatorConditionMode.If);

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.SetBool("Go", true);
        animator.Advance(0.1f);

        animator.IsInTransition.Should().BeFalse();
        hips.Transform.Position.Y.Should().BeApproximately(10f, 1e-5f);
    }

    [Fact]
    public void ALayerWithZeroWeight_ContributesNothing()
    {
        AnimatorController controller = IdleAt(9f);
        controller.Layers[0].Weight = 0f;

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);

        hips.Transform.Position.Y.Should().Be(0f);
    }

    // ---------------------------------------------------------------- blend trees

    [Fact]
    public void AStatePlayingABlendTree_FollowsItsParameter()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        AnimatorParameter speed = controller.AddParameter("Speed", AnimatorParameterType.Float);

        BlendTree1D locomotion = new BlendTree1D(speed.Hash)
            .Add(Clip("Walk", 0f), 0f)
            .Add(Clip("Run", 10f), 10f);

        layer.AddState("Locomotion", locomotion);

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.SetFloat("Speed", 5f);
        animator.Advance(0.1f);

        hips.Transform.Position.Y.Should().BeApproximately(5f, 1e-5f);
    }

    // ---------------------------------------------------------------- parameters

    [Fact]
    public void SetFloatWithADampTime_MovesTowardTheTargetOverSeveralFrames()
    {
        AnimatorController controller = new();
        controller.AddLayer("Base").AddState("Idle", Clip("Idle", 0f));
        controller.AddParameter("Speed", AnimatorParameterType.Float);

        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        // A critically damped approach starts from rest and closes on the target without overshooting it.
        animator.SetFloat("Speed", 10f, 1f, 0.25f);
        float first = animator.GetFloat("Speed");

        first.Should().BeGreaterThan(0f).And.BeLessThan(10f);

        animator.SetFloat("Speed", 10f, 1f, 0.25f);
        float second = animator.GetFloat("Speed");

        // Further along than the first step, and never past the target.
        second.Should().BeGreaterThan(first).And.BeLessThan(10f);

        // Given enough time it arrives, without ever reading past the value asked for.
        for (int frame = 0; frame < 200; frame++)
        {
            animator.SetFloat("Speed", 10f, 1f, 0.016f);
            animator.GetFloat("Speed").Should().BeLessThanOrEqualTo(10f + 1e-4f);
        }

        animator.GetFloat("Speed").Should().BeApproximately(10f, 0.05f);
    }

    [Fact]
    public void SetFloatWithNoDampTime_Snaps()
    {
        AnimatorController controller = new();
        controller.AddLayer("Base").AddState("Idle", Clip("Idle", 0f));
        controller.AddParameter("Speed", AnimatorParameterType.Float);

        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.SetFloat("Speed", 7.5f, 0f, 0.016f);

        animator.GetFloat("Speed").Should().Be(7.5f);
    }

    [Fact]
    public void SettingAParameterTheControllerLacks_IsIgnoredRatherThanThrowing()
    {
        AnimatorController controller = IdleAt(0f);
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Invoking(a => a.SetFloat("Nope", 1f)).Should().NotThrow();
        animator.GetFloat("Nope").Should().Be(0f);
    }

    [Fact]
    public void IsInState_ReportsTheCurrentStateByName()
    {
        AnimatorController controller = IdleAt(0f);
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);

        animator.IsInState("Idle").Should().BeTrue();
        animator.IsInState("Run").Should().BeFalse();
    }

    [Fact]
    public void Play_JumpsStraightToAState_WithNoTransition()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        layer.AddState("Idle", Clip("Idle", 0f));
        AnimatorState run = layer.AddState("Run", Clip("Run", 8f));

        GameObject root = Hierarchy("Hips", out GameObject hips);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.Play("Run");

        animator.CurrentState.Should().BeSameAs(run);
        animator.IsInTransition.Should().BeFalse();

        animator.Advance(0.1f);
        hips.Transform.Position.Y.Should().BeApproximately(8f, 1e-5f);
    }

    [Fact]
    public void Play_WithAStateThatDoesNotExist_LogsAndChangesNothing()
    {
        AnimatorController controller = IdleAt(0f);
        GameObject root = Hierarchy("Hips", out _);
        Animator animator = root.AddComponent(new Animator { Controller = controller });

        animator.Advance(0.1f);
        animator.Play("NoSuchState");

        animator.CurrentState!.Name.Should().Be("Idle");
    }

    // ---------------------------------------------------------------- the frame

    [Fact]
    public void Scene_AdvancesAnAnimator_BeforeLateUpdate()
    {
        GameObject root = new("Root");
        GameObject hips = new("Hips");
        hips.Transform.Parent = root.Transform;

        Scene scene = Start(root);

        Animator animator = root.AddComponent(new Animator { Controller = IdleAt(6f) });
        Reader reader = root.AddComponent(new Reader(hips));

        scene.Update(0.1f);

        animator.CurrentState.Should().NotBeNull();
        reader.Seen.Should().BeApproximately(6f, 1e-5f);
    }

    [Fact]
    public void Scene_SkipsAnAnimatorThatAskedForThePhysicsPhase()
    {
        // It is stepped by the fixed step instead, so it must not advance twice in one frame.
        GameObject root = new("Root");
        GameObject hips = new("Hips");
        hips.Transform.Parent = root.Transform;

        Scene scene = Start(root);

        root.AddComponent(new Animator
        {
            Controller = IdleAt(6f),
            UpdateMode = AnimatorUpdateMode.AnimatePhysics
        });

        scene.Update(0.1f);

        hips.Transform.Position.Y.Should().Be(0f);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A started scene whose root has one child, so a clip has a path to address.</summary>
    private GameObject Hierarchy(string childName, out GameObject child)
    {
        GameObject root = new("Root");

        child = new(childName);
        child.Transform.Parent = root.Transform;

        Start(root);

        return root;
    }

    private Scene Start(GameObject root)
    {
        Scene scene = new("Test.scene", [root]);
        scene.Start(_physics, _audio);

        return scene;
    }

    /// <summary>A controller whose only state holds one bone at a height, so a pose is one number to read.</summary>
    private static AnimatorController IdleAt(float height)
    {
        AnimatorController controller = new();
        controller.AddLayer("Base").AddState("Idle", Clip("Idle", height));

        return controller;
    }

    /// <summary>Idle that goes to Run once Speed passes a tenth.</summary>
    private static AnimatorController Running()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");

        AnimatorParameter speed = controller.AddParameter("Speed", AnimatorParameterType.Float);

        AnimatorState idle = layer.AddState("Idle", Clip("Idle", 0f));
        AnimatorState run = layer.AddState("Run", Clip("Run", 8f));

        idle.TransitionTo(run).When(speed.Hash, AnimatorConditionMode.Greater, 0.1f);

        return controller;
    }

    /// <summary>Idle that jumps on a trigger.</summary>
    private static AnimatorController Jumping()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");

        AnimatorParameter jump = controller.AddParameter("Jump", AnimatorParameterType.Trigger);

        AnimatorState idle = layer.AddState("Idle", Clip("Idle", 0f));
        AnimatorState air = layer.AddState("Jump", Clip("Jump", 4f));

        idle.TransitionTo(air).When(jump.Hash, AnimatorConditionMode.If);

        return controller;
    }

    /// <summary>A one-bone clip held at a height, one second long and looping.</summary>
    private static AnimationClip Clip(string name, float height)
    {
        AnimationCurve curve = new("Hips", AnimationProperty.LocalPosition, AnimationAxis.Y);
        curve.AddKey(0f, height);
        curve.AddKey(1f, height);

        AnimationClip clip = new(name) { Length = 1f, WrapMode = AnimationWrapMode.Loop };
        clip.AddCurve(curve);

        return clip;
    }

    /// <summary>Reads a bone's height in <c>LateUpdate</c>, to prove the animation step ran before it.</summary>
    private sealed class Reader(GameObject target) : Script
    {
        public float Seen { get; private set; }

        public override void LateUpdate() => Seen = target.Transform.Position.Y;
    }
}
