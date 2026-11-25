using AwesomeAssertions;
using Overcore.Animation;
using Xunit;

namespace Overcore.Tests.Animation;

/// <summary>A clip's length, its wrap arithmetic, and the shape of its events.</summary>
public class AnimationClipTests
{
    private static AnimationClip ClipOf(float end, AnimationWrapMode wrapMode = AnimationWrapMode.Loop)
    {
        AnimationClip clip = new("Test") { WrapMode = wrapMode };

        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y);
        curve.AddKey(0f, 0f);
        curve.AddKey(end, 1f);

        clip.AddCurve(curve);

        return clip;
    }

    [Fact]
    public void Length_IsTheLastKeyOfTheLongestCurve()
    {
        AnimationClip clip = new("Test");

        AnimationCurve shortCurve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.X);
        shortCurve.AddKey(0f, 0f);
        shortCurve.AddKey(0.5f, 1f);

        AnimationCurve longCurve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y);
        longCurve.AddKey(0f, 0f);
        longCurve.AddKey(2f, 1f);

        clip.AddCurve(shortCurve);
        clip.AddCurve(longCurve);

        clip.Length.Should().Be(2f);
    }

    [Fact]
    public void Length_CanBeSetExplicitly_ForACycleThatDoesNotEndOnAKey()
    {
        AnimationClip clip = ClipOf(1f);

        clip.Length = 1.25f;

        clip.Length.Should().Be(1.25f);
    }

    [Fact]
    public void Length_OfAClipWithNoKeys_IsInstant()
    {
        new AnimationClip("Empty").Length.Should().Be(0f);
        new AnimationClip("Empty").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void WrapTime_Loop_FoldsPastTheEnd()
    {
        AnimationClip clip = ClipOf(1f, AnimationWrapMode.Loop);

        clip.WrapTime(0.25f).Should().BeApproximately(0.25f, 1e-5f);
        clip.WrapTime(1.25f).Should().BeApproximately(0.25f, 1e-5f);
        clip.WrapTime(7.5f).Should().BeApproximately(0.5f, 1e-5f);
    }

    [Fact]
    public void WrapTime_Loop_HandlesNegativeTime_InsteadOfKeepingTheSign()
    {
        AnimationClip clip = ClipOf(1f, AnimationWrapMode.Loop);

        // A negative time is a frame before the start, which is the tail of the previous cycle. `%` would answer
        // -0.25 and the sampler would then clamp to the first key.
        clip.WrapTime(-0.25f).Should().BeApproximately(0.75f, 1e-5f);
    }

    [Fact]
    public void WrapTime_PingPong_ReversesAtEachEnd()
    {
        AnimationClip clip = ClipOf(1f, AnimationWrapMode.PingPong);

        clip.WrapTime(0.25f).Should().BeApproximately(0.25f, 1e-5f);

        // 1.25 is a quarter past the end, so it is a quarter before the end on the way back.
        clip.WrapTime(1.25f).Should().BeApproximately(0.75f, 1e-5f);

        // 1.75 is three quarters through the doubled cycle, still on the return leg.
        clip.WrapTime(1.75f).Should().BeApproximately(0.25f, 1e-5f);
    }

    [Fact]
    public void WrapTime_Once_And_ClampForever_HoldTheEnds()
    {
        AnimationClip once = ClipOf(1f, AnimationWrapMode.Once);
        AnimationClip clamp = ClipOf(1f, AnimationWrapMode.ClampForever);

        foreach (AnimationClip clip in new[] { once, clamp })
        {
            clip.WrapTime(-3f).Should().Be(0f);
            clip.WrapTime(0.5f).Should().Be(0.5f);
            clip.WrapTime(9f).Should().Be(1f);
        }
    }

    [Fact]
    public void AddCurve_ReplacesACurveForTheSameProperty()
    {
        AnimationClip clip = new("Test");

        AnimationCurve first = new("Bone", AnimationProperty.LocalScale, AnimationAxis.X);
        first.AddKey(0f, 1f);

        AnimationCurve replacement = new("Bone", AnimationProperty.LocalScale, AnimationAxis.X);
        replacement.AddKey(0f, 5f);

        clip.AddCurve(first);
        clip.AddCurve(replacement);

        clip.Curves.Should().ContainSingle();
        clip.Curves[0].Keys[0].Value.Should().Be(5f);
    }

    [Fact]
    public void AddCurve_KeepsCurvesForDifferentAxesAndDifferentBones()
    {
        AnimationClip clip = new("Test");

        clip.AddCurve(new AnimationCurve("A", AnimationProperty.LocalPosition, AnimationAxis.X));
        clip.AddCurve(new AnimationCurve("A", AnimationProperty.LocalPosition, AnimationAxis.Y));
        clip.AddCurve(new AnimationCurve("B", AnimationProperty.LocalPosition, AnimationAxis.X));

        clip.Curves.Should().HaveCount(3);
    }

    [Fact]
    public void Events_AreKeptInTimeOrder()
    {
        AnimationClip clip = ClipOf(1f);

        clip.AddEvent(new AnimationEvent("Footstep", 0.75f));
        clip.AddEvent(new AnimationEvent("Land", 0.25f));

        clip.Events.Select(animationEvent => animationEvent.Function).Should().Equal("Land", "Footstep");
    }

    [Fact]
    public void Events_CarryTheirParameters()
    {
        AnimationClip clip = ClipOf(1f);

        clip.AddEvent(new AnimationEvent("Footstep", 0.5f)
        {
            StringParameter = "grass",
            FloatParameter = 1.5f,
            IntParameter = 2
        });

        AnimationEvent single = clip.Events[0];

        single.StringParameter.Should().Be("grass");
        single.FloatParameter.Should().Be(1.5f);
        single.IntParameter.Should().Be(2);
    }
}
