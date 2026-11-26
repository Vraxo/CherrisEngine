using AwesomeAssertions;
using Overcore.Animation;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// Curve evaluation: the part of the animation layer that is pure arithmetic, and therefore pinned exactly rather
/// than approximately.
/// </summary>
public class AnimationCurveTests
{
    private static AnimationCurve Curve(params (float Time, float Value)[] keys)
    {
        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y);

        foreach ((float time, float value) in keys)
        {
            curve.AddKey(time, value);
        }

        return curve;
    }

    [Fact]
    public void Evaluate_ReturnsTheKeyValue_AtAKeysExactTime()
    {
        AnimationCurve curve = Curve((0f, 0f), (1f, 10f));

        curve.Evaluate(0f, AnimationInterpolation.Linear).Should().Be(0f);
        curve.Evaluate(1f, AnimationInterpolation.Linear).Should().Be(10f);
    }

    [Fact]
    public void Evaluate_InterpolatesLinearly_BetweenKeys()
    {
        AnimationCurve curve = Curve((0f, 0f), (2f, 10f));

        curve.Evaluate(0.5f, AnimationInterpolation.Linear).Should().BeApproximately(2.5f, 1e-5f);
        curve.Evaluate(1f, AnimationInterpolation.Linear).Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void Evaluate_HoldsTheEarlierValue_UnderConstantInterpolation()
    {
        AnimationCurve curve = Curve((0f, 1f), (1f, 5f));

        // Constant means the value steps at the next key, it does not ramp toward it.
        curve.Evaluate(0.1f, AnimationInterpolation.Constant).Should().Be(1f);
        curve.Evaluate(0.99f, AnimationInterpolation.Constant).Should().Be(1f);
        curve.Evaluate(1f, AnimationInterpolation.Constant).Should().Be(5f);
    }

    [Fact]
    public void Evaluate_ClampsOutsideTheKeys_RatherThanZeroing()
    {
        AnimationCurve curve = Curve((1f, 4f), (2f, 8f));

        curve.Evaluate(-5f, AnimationInterpolation.Linear).Should().Be(4f);
        curve.Evaluate(50f, AnimationInterpolation.Linear).Should().Be(8f);
    }

    [Fact]
    public void Evaluate_IsFlat_ForASingleKey()
    {
        AnimationCurve curve = Curve((3f, 7f));

        curve.Evaluate(0f, AnimationInterpolation.Linear).Should().Be(7f);
        curve.Evaluate(3f, AnimationInterpolation.Linear).Should().Be(7f);
        curve.Evaluate(100f, AnimationInterpolation.Linear).Should().Be(7f);
    }

    [Fact]
    public void Evaluate_WithNoKeysAtAll_IsZero()
    {
        AnimationCurve curve = Curve();

        curve.Evaluate(1f, AnimationInterpolation.Linear).Should().Be(0f);
    }

    [Fact]
    public void AddKey_KeepsKeysSorted_WhenTheyArriveOutOfOrder()
    {
        // An importer reads a file's keys in file order, which is not guaranteed to be sorted.
        AnimationCurve curve = Curve((1f, 10f), (0f, 0f), (0.5f, 5f));

        curve.Keys.Select(key => key.Time).Should().Equal(0f, 0.5f, 1f);
        curve.StartTime.Should().Be(0f);
        curve.EndTime.Should().Be(1f);
    }

    [Fact]
    public void AddKey_IgnoresATimeThatIsNotANumber()
    {
        AnimationCurve curve = Curve((0f, 0f));

        curve.AddKey(float.NaN, 5f);

        curve.Keys.Should().ContainSingle();
    }

    [Fact]
    public void EvaluateCubic_FollowsTheHermiteBasis_WithTangentsInValuePerSecond()
    {
        // A curve that starts at 0 with an outgoing slope of 1 per second, and ends at 1 with no incoming slope.
        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.X);
        curve.AddKey(new AnimationKey(0f, 0f, 0f, 1f));
        curve.AddKey(new AnimationKey(1f, 1f, 0f, 0f));

        // At the midpoint the Hermite basis is h00=0.5, h10=0.125, h01=0.5, h11=-0.125, so these values give
        // 0.5*0 + 0.125*1 + 0.5*1 + (-0.125)*0 = 0.625. The outgoing slope lifts the curve above the straight line
        // and the incoming flat tangent brings it back to 1 at the end.
        curve.Evaluate(0.5f, AnimationInterpolation.Cubic).Should().BeApproximately(0.625f, 1e-5f);

        // The slope is positive throughout: the value rises rather than dipping.
        curve.Evaluate(0.25f, AnimationInterpolation.Cubic)
            .Should().BeLessThan(curve.Evaluate(0.75f, AnimationInterpolation.Cubic));
    }

    [Fact]
    public void Evaluate_WithARepeatedTime_DoesNotDivideByZero()
    {
        // Two keys at the same time: the segment has zero span, so the earlier value wins instead of producing NaN.
        AnimationCurve curve = Curve((0f, 1f), (1f, 2f), (1f, 3f));

        float value = curve.Evaluate(1f, AnimationInterpolation.Linear);

        float.IsNaN(value).Should().BeFalse();
    }
}
