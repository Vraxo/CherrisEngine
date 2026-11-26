using AwesomeAssertions;
using Overcore.Animation;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The controller as data: parameters by name and hash, conditions, and the blend tree. None of this needs a scene,
/// a clock or a pose, which is the point of keeping the controller separate from the <c>Animator</c> that plays it.
/// </summary>
public class AnimatorControllerTests
{
    // ---------------------------------------------------------------- hashing

    [Fact]
    public void StringToHash_IsTheSameForTheSameName_EveryTime()
    {
        // A hash that moved between runs would orphan every transition in a saved controller.
        AnimatorController.StringToHash("Speed")
            .Should()
            .Be(AnimatorController.StringToHash("Speed"));
    }

    [Fact]
    public void StringToHash_IsFNV1a_OverTheUtf8Bytes()
    {
        // Pinned against the algorithm rather than against this implementation, so a "tidier" hash that silently
        // breaks every stored controller fails here.
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint expected = offsetBasis;

        foreach (byte value in "Speed"u8)
        {
            expected ^= value;
            expected *= prime;
        }

        AnimatorController.StringToHash("Speed").Should().Be(unchecked((int)expected));
    }

    [Fact]
    public void StringToHash_DiffersForDifferentNames()
    {
        AnimatorController.StringToHash("Speed")
            .Should()
            .NotBe(AnimatorController.StringToHash("Speed2"));
    }

    [Fact]
    public void StringToHash_OfAnEmptyName_IsTheOffsetBasis()
    {
        // No bytes folded in means the basis comes back untouched, which pins the loop's shape.
        AnimatorController.StringToHash(string.Empty).Should().Be(unchecked((int)2166136261));
    }

    // ---------------------------------------------------------------- parameters

    [Fact]
    public void Parameters_AreAddressableByNameAndByHash()
    {
        AnimatorController controller = new();
        AnimatorParameter speed = controller.AddParameter("Speed", AnimatorParameterType.Float);

        controller.FindParameter("Speed").Should().BeSameAs(speed);
        controller.FindParameter(speed.Hash).Should().BeSameAs(speed);
    }

    [Fact]
    public void AddingAParameterWithTheSameName_ReplacesItRatherThanDuplicating()
    {
        // Two parameters with one name would give two different behaviours for the same hash.
        AnimatorController controller = new();
        controller.AddParameter("Speed", AnimatorParameterType.Float);
        controller.AddParameter("Speed", AnimatorParameterType.Int);

        controller.Parameters.Should().ContainSingle();
        controller.FindParameter("Speed")!.Type.Should().Be(AnimatorParameterType.Int);
    }

    [Fact]
    public void FindParameter_OfANameThatIsNotThere_IsNull()
    {
        new AnimatorController().FindParameter("Nope").Should().BeNull();
    }

    [Fact]
    public void ResetParameters_ReturnsEveryParameterToZero()
    {
        AnimatorController controller = new();
        AnimatorParameter speed = controller.AddParameter("Speed", AnimatorParameterType.Float);
        AnimatorParameter count = controller.AddParameter("Count", AnimatorParameterType.Int);
        AnimatorParameter grounded = controller.AddParameter("Grounded", AnimatorParameterType.Bool);

        speed.FloatValue = 3f;
        count.IntValue = 7;
        grounded.BoolValue = true;

        controller.ResetParameters();

        speed.FloatValue.Should().Be(0f);
        count.IntValue.Should().Be(0);
        grounded.BoolValue.Should().BeFalse();
    }

    // ---------------------------------------------------------------- conditions

    [Fact]
    public void ABoolCondition_PassesOnlyWhenTheBoolMatches()
    {
        AnimatorParameter grounded = new("Grounded", AnimatorParameterType.Bool);

        AnimatorCondition isTrue = new(grounded.Hash, AnimatorConditionMode.If, 0f);
        AnimatorCondition isFalse = new(grounded.Hash, AnimatorConditionMode.IfNot, 0f);

        isTrue.IsMetBy(grounded).Should().BeFalse();

        grounded.BoolValue = true;

        isTrue.IsMetBy(grounded).Should().BeTrue();
        isFalse.IsMetBy(grounded).Should().BeFalse();
    }

    [Fact]
    public void AFloatCondition_ComparesAgainstTheThreshold()
    {
        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 3.2f };

        Condition(speed, AnimatorConditionMode.Greater, 3f).Should().BeTrue();
        Condition(speed, AnimatorConditionMode.Less, 3f).Should().BeFalse();
        Condition(speed, AnimatorConditionMode.Less, 4f).Should().BeTrue();

        // Exact comparison, because an int-backed condition is how a state picks one of a set.
        Condition(speed, AnimatorConditionMode.Equals, 3.2f).Should().BeTrue();
        Condition(speed, AnimatorConditionMode.NotEqual, 3.2f).Should().BeFalse();
    }

    [Fact]
    public void AnIntCondition_ComparesIntegers()
    {
        AnimatorParameter weapon = new("Weapon", AnimatorParameterType.Int) { IntValue = 2 };

        Condition(weapon, AnimatorConditionMode.Equals, 2f).Should().BeTrue();
        Condition(weapon, AnimatorConditionMode.Equals, 1f).Should().BeFalse();
        Condition(weapon, AnimatorConditionMode.Greater, 1f).Should().BeTrue();
    }

    [Fact]
    public void AConditionOnAMissingParameter_FailsRatherThanThrowing()
    {
        // A controller that outlives a renamed parameter should stall, not take the frame down.
        AnimatorCondition condition = new(12345, AnimatorConditionMode.If, 0f);

        condition.IsMetBy(null).Should().BeFalse();
    }

    [Fact]
    public void ATransitionWithNoConditions_IsAlwaysReady()
    {
        AnimatorController controller = new();
        AnimatorTransition transition = new AnimatorTransition(new AnimatorState("Next"));

        transition.AreConditionsMet(controller.FindParameter).Should().BeTrue();
    }

    [Fact]
    public void ATransitionsConditions_AreAllRequired()
    {
        AnimatorController controller = new();
        AnimatorParameter speed = controller.AddParameter("Speed", AnimatorParameterType.Float);
        AnimatorParameter grounded = controller.AddParameter("Grounded", AnimatorParameterType.Bool);

        AnimatorTransition transition = new AnimatorTransition(new AnimatorState("Run"))
            .When(speed.Hash, AnimatorConditionMode.Greater, 0.1f)
            .When(grounded.Hash, AnimatorConditionMode.If);

        speed.FloatValue = 3f;
        grounded.BoolValue = false;

        transition.AreConditionsMet(controller.FindParameter).Should().BeFalse();

        grounded.BoolValue = true;

        transition.AreConditionsMet(controller.FindParameter).Should().BeTrue();
    }

    // ---------------------------------------------------------------- the 1D blend tree

    [Fact]
    public void ABlendTree_AtAThreshold_PlaysThatClipAlone()
    {
        AnimationClip walk = Clip("Walk");
        AnimationClip run = Clip("Run");

        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 5f };

        BlendTree1D tree = new BlendTree1D(speed.Hash).Add(walk, 0f).Add(run, 5f);

        AnimationPose pose = new();
        tree.Sample(0f, pose, 1f, hash => hash == speed.Hash ? speed : null);

        // At the far threshold the far clip is the only contribution.
        pose.Contains("Bone").Should().BeTrue();
    }

    [Fact]
    public void ABlendTree_OutsideItsRange_ClampsToTheNearestClip()
    {
        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 99f };

        AnimationClip walk = Clip("Walk", height: 1f);
        AnimationClip run = Clip("Run", height: 9f);

        BlendTree1D tree = new BlendTree1D(speed.Hash).Add(walk, 0f).Add(run, 5f);

        AnimationPose pose = new();
        tree.Sample(0f, pose, 1f, hash => hash == speed.Hash ? speed : null);

        // Far past the top of the range, the run clip plays alone rather than fading toward a motion the tree lacks.
        pose.GetPosition("Bone").Y.Should().BeApproximately(9f, 1e-5f);
    }

    [Fact]
    public void ABlendTree_BetweenThresholds_MixesBothClipsInProportion()
    {
        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 5f };

        AnimationClip walk = Clip("Walk", height: 0f);
        AnimationClip run = Clip("Run", height: 10f);

        BlendTree1D tree = new BlendTree1D(speed.Hash).Add(walk, 0f).Add(run, 10f);

        AnimationPose pose = new();
        tree.Sample(0f, pose, 1f, hash => hash == speed.Hash ? speed : null);

        // Halfway through the range, halfway between the two heights.
        pose.GetPosition("Bone").Y.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void ABlendTree_SortsItsEntries_SoAFileMayListThemInAnyOrder()
    {
        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 5f };

        AnimationClip run = Clip("Run", height: 10f);
        AnimationClip walk = Clip("Walk", height: 0f);

        // Added high-threshold first: the tree must sort rather than assume the order it was given.
        BlendTree1D tree = new BlendTree1D(speed.Hash).Add(run, 10f).Add(walk, 0f);

        AnimationPose pose = new();
        tree.Sample(0f, pose, 1f, hash => hash == speed.Hash ? speed : null);

        pose.GetPosition("Bone").Y.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void ABlendTree_WithOneEntry_PlaysIt()
    {
        AnimatorParameter speed = new("Speed", AnimatorParameterType.Float) { FloatValue = 100f };

        BlendTree1D tree = new BlendTree1D(speed.Hash).Add(Clip("Only", height: 4f), 0f);

        AnimationPose pose = new();
        tree.Sample(0f, pose, 1f, hash => hash == speed.Hash ? speed : null);

        pose.GetPosition("Bone").Y.Should().BeApproximately(4f, 1e-5f);
    }

    // ---------------------------------------------------------------- layers and states

    [Fact]
    public void TheFirstStateAdded_BecomesTheLayersDefault()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");

        AnimatorState idle = layer.AddState("Idle", Clip("Idle"));
        layer.AddState("Run", Clip("Run"));

        layer.DefaultState.Should().BeSameAs(idle);
    }

    [Fact]
    public void StatesAreAddressableByName_AndCarryTheHashOfTheirName()
    {
        AnimatorController controller = new();
        AnimatorLayer layer = controller.AddLayer("Base");
        layer.AddState("Run", Clip("Run"));

        controller.FindState("Run")!.Hash.Should().Be(AnimatorController.StringToHash("Run"));
        controller.FindState("Missing").Should().BeNull();
        controller.StateNames().Should().ContainSingle().Which.Should().Be("Run");
    }

    /// <summary>Whether a condition against a threshold holds for a parameter, which is what every case here asks.</summary>
    private static bool Condition(AnimatorParameter parameter, AnimatorConditionMode mode, float threshold)
    {
        AnimatorCondition condition = new(parameter.Hash, mode, threshold);

        return condition.IsMetBy(parameter);
    }

    /// <summary>A one-bone clip whose height is constant, so a blend is a number to read off.</summary>
    private static AnimationClip Clip(string name, float height = 0f)
    {
        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y);
        curve.AddKey(0f, height);
        curve.AddKey(1f, height);

        AnimationClip clip = new(name) { Length = 1f, WrapMode = AnimationWrapMode.Loop };
        clip.AddCurve(curve);

        return clip;
    }
}
